using MDKOSS.Core.Drivers;
using MDKOSS.Core.Monitoring;
using MDKOSS.Tasks;

namespace MDKOSS.Core;

/// <summary>
/// Runtime host: wires setting, drivers, devices, tasks, and shared vars together.
/// </summary>
public sealed class MdkRuntime : IDisposable
{
    private const string DefaultMonitoringPrefix = "http://localhost:5080/";
    private readonly Dictionary<string, IDriver> _drivers = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, MDeviceBase> _devices = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, MTaskBase> _tasks = new(StringComparer.OrdinalIgnoreCase);
    private readonly MTaskScheduler _scheduler = new();
    private MonitoringServer? _monitoringServer;

    public MdkSetting Setting { get; }

    public MVarStore Vars { get; } = new();

    public bool IsRunning { get; private set; }

    public string MonitoringPrefix => _monitoringServer?.Prefix ?? DefaultMonitoringPrefix;

    public MdkRuntime(MdkSetting setting)
    {
        Setting = setting;
    }

    public static MdkRuntime CreateFromFile(string settingPath)
    {
        var setting = MdkSetting.Load(settingPath);
        return new MdkRuntime(setting);
    }

    /// <summary>
    /// One-time bootstrap for all runtime components.
    /// </summary>
    public void Initialize()
    {
        AppLog.Configure();
        AppLog.Info("MdkRuntime initializing.");

        BootstrapVars();
        BootstrapDrivers();
        BootstrapDevices();
        BootstrapTasks();

        AppLog.Info($"MdkRuntime initialized (project: {Setting.ProjectName}).");
    }

    /// <summary>
    /// Starts devices first, then task scheduler.
    /// </summary>
    public void Start()
    {
        _monitoringServer ??= new MonitoringServer(this, DefaultMonitoringPrefix);
        _monitoringServer.Start();

        foreach (var device in _devices.Values)
        {
            device.Start();
        }

        _scheduler.Start();
        IsRunning = true;
        AppLog.Info("MdkRuntime started.");
    }

    /// <summary>
    /// Stops scheduler first to avoid device operations during shutdown.
    /// </summary>
    public async Task StopAsync()
    {
        AppLog.Info("MdkRuntime stopping.");
        IsRunning = false;
        await _scheduler.StopAsync().ConfigureAwait(false);

        foreach (var device in _devices.Values)
        {
            device.Stop();
        }

        if (_monitoringServer is not null)
        {
            await _monitoringServer.StopAsync().ConfigureAwait(false);
        }
    }

    // Seed initial runtime vars from config.
    private void BootstrapVars()
    {
        foreach (var kv in Setting.Vars)
        {
            Vars.Set(kv.Key, kv.Value);
        }
    }

    // Instantiate and initialize all enabled drivers.
    private void BootstrapDrivers()
    {
        foreach (var config in Setting.Drivers.Where(d => d.Enabled))
        {
            IDriver driver = config.Type.ToLowerInvariant() switch
            {
                "gts" => new DrvGts(),
                "sim" => new DrvSim(),
                _ => throw new NotSupportedException($"Unsupported driver type: {config.Type}")
            };

            driver.Initialize(config);
            _drivers[config.Id] = driver;
        }
    }

    // Register tasks from runtime setting.
    private void BootstrapTasks()
    {
        foreach (var config in Setting.Tasks)
        {
            var task = CreateTaskFromConfig(config);
            if (task is null)
            {
                continue;
            }

            RegisterTask(task);
        }
    }

    private void RegisterTask(MTaskBase task)
    {
        if (_tasks.ContainsKey(task.Name))
        {
            throw new InvalidOperationException($"Duplicate task name: {task.Name}");
        }

        _tasks[task.Name] = task;
        _scheduler.Register(task);
    }

    private MTaskBase? CreateTaskFromConfig(MdkSetting.TaskConfig config)
    {
        var taskType = string.IsNullOrWhiteSpace(config.Type)
            ? "pollDriver"
            : config.Type.Trim();
        var taskName = string.IsNullOrWhiteSpace(config.Name) ? taskType : config.Name;

        switch (taskType.ToLowerInvariant())
        {
            case "poll":
            case "polldriver":
                if (!_drivers.TryGetValue(config.DriverId, out var driver))
                {
                    return null;
                }

                return new PollDriverTask(taskName, config.IntervalMs, driver, Vars);

            case "operation":
            case "taskoperation":
                var gpio = ResolveTaskGpio(config.Parameters);
                return new TaskOperationTask(Vars, gpio, config.IntervalMs);

            case "cycle":
            case "taskcycle":
                return new TaskCycleTask(Vars, GetSnapshot, () => _tasks.Values.ToList(), config.IntervalMs);

            default:
                throw new NotSupportedException($"Unsupported task type: {config.Type}");
        }
    }

    private GpioDevice? ResolveTaskGpio(IReadOnlyDictionary<string, string> parameters)
    {
        if (parameters.TryGetValue("gpioDeviceId", out var deviceId) && !string.IsNullOrWhiteSpace(deviceId))
        {
            if (_devices.TryGetValue(deviceId, out var mapped) && mapped is GpioDevice gpioDevice)
            {
                return gpioDevice;
            }
        }

        return _devices.Values.OfType<GpioDevice>().FirstOrDefault();
    }

    // Instantiate and initialize all enabled devices.
    private void BootstrapDevices()
    {
        foreach (var config in Setting.Devices.Where(d => d.Enabled))
        {
            var deviceName = string.IsNullOrWhiteSpace(config.Name) ? config.Id : config.Name;
            var deviceType = config.Type.ToLowerInvariant();
            IDriver? driver = null;

            if (deviceType != "gpio" && !_drivers.TryGetValue(config.DriverId, out driver))
            {
                continue;
            }

            MDeviceBase device = deviceType switch
            {
                "gpio" => BuildGpioDevice(config, deviceName),
                "axis" => new AxisDevice(config.Id, deviceName, driver!, Vars),
                "platform" => new PlatformDevice(config.Id, deviceName, driver!, Vars),
                "cameradev" => new CameraDevDevice(config.Id, deviceName, driver!, Vars),
                _ => throw new NotSupportedException($"Unsupported device type: {config.Type}")
            };

            device.Initialize();
            _devices[config.Id] = device;
        }
    }

    private GpioDevice BuildGpioDevice(MdkSetting.DeviceConfig config, string deviceName)
    {
        var gpioDevice = new GpioDevice(config.Id, deviceName, _drivers, Vars);
        foreach (var kv in config.Parameters)
        {
            if (kv.Key.StartsWith("in.", StringComparison.OrdinalIgnoreCase))
            {
                var alias = kv.Key[3..];
                if (TryParsePointRoute(kv.Value, out var driverId, out var address))
                {
                    gpioDevice.RegisterInput(alias, driverId, address);
                }
            }
            else if (kv.Key.StartsWith("out.", StringComparison.OrdinalIgnoreCase))
            {
                var alias = kv.Key[4..];
                if (TryParsePointRoute(kv.Value, out var driverId, out var address))
                {
                    gpioDevice.RegisterOutput(alias, driverId, address);
                }
            }
        }

        return gpioDevice;
    }

    private static bool TryParsePointRoute(string? raw, out string driverId, out string address)
    {
        driverId = string.Empty;
        address = string.Empty;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        var route = raw.Trim();
        var splitIndex = route.IndexOf(':');
        if (splitIndex <= 0 || splitIndex >= route.Length - 1)
        {
            return false;
        }

        driverId = route[..splitIndex].Trim();
        address = route[(splitIndex + 1)..].Trim();
        return !string.IsNullOrWhiteSpace(driverId) && !string.IsNullOrWhiteSpace(address);
    }

    public void Dispose()
    {
        AppLog.Info("MdkRuntime disposing.");
        IsRunning = false;
        _monitoringServer?.Dispose();
        _monitoringServer = null;
        _scheduler.Dispose();
        foreach (var driver in _drivers.Values)
        {
            driver.Dispose();
        }
        foreach (var device in _devices.Values)
        {
            device.Dispose();
        }

        _drivers.Clear();
        _devices.Clear();
        _tasks.Clear();

        AppLog.Shutdown();
    }


    /// <summary>
    /// Exposes a snapshot for monitoring APIs/UI.
    /// </summary>
    public RuntimeSnapshot GetSnapshot()
    {
        return new RuntimeSnapshot(
            Setting.ProjectName,
            IsRunning,
            _drivers.ToDictionary(
                kv => kv.Key,
                kv => new DriverSnapshot(kv.Value.Name, kv.Value.IsConnected),
                StringComparer.OrdinalIgnoreCase),
            _devices.ToDictionary(
                kv => kv.Key,
                kv => kv.Value.GetSnapshot(),
                StringComparer.OrdinalIgnoreCase),
            Vars.Snapshot());
    }
}

public sealed record DriverSnapshot(string Type, bool IsConnected);

public sealed record RuntimeSnapshot(
    string ProjectName,
    bool IsRunning,
    IReadOnlyDictionary<string, DriverSnapshot> Drivers,
    IReadOnlyDictionary<string, DeviceSnapshot> Devices,
    IReadOnlyDictionary<string, object?> Vars);
