using System.Net;
using MDKOSS.Core.Drivers;
using MDKOSS.Core.Monitoring;

namespace MDKOSS.Core;

/// <summary>
/// Runtime host: wires setting, drivers, devices, tasks, and shared vars together.
/// </summary>
public sealed class MdkRuntime : IDisposable
{
    private const string DefaultMonitoringPrefix = "http://127.0.0.1:5080/";
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
        var monitorPrefix = string.IsNullOrWhiteSpace(Setting.MonitoringPrefix)
            ? DefaultMonitoringPrefix
            : Setting.MonitoringPrefix.Trim();

        _monitoringServer ??= new MonitoringServer(this, monitorPrefix);
        try
        {
            _monitoringServer.Start();
        }
        catch (HttpListenerException ex)
        {
            AppLog.Error(
                ex,
                $"Monitoring HTTP listener failed to start on '{monitorPrefix}'. " +
                "Another process may be using the port, or the URL is reserved. " +
                "Set \"monitoringPrefix\" in your settings JSON to a free URL (e.g. http://127.0.0.1:5081/).");
            throw;
        }

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
            var driver = DriverFactory.Create(config.Type);
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
            throw new MdkException(MdkErrorCode.DuplicateTaskName, $"Duplicate task name: {task.Name}");
        }

        _tasks[task.Name] = task;
        _scheduler.Register(task);
    }

    private MTaskBase? CreateTaskFromConfig(MdkSetting.TaskConfig config)
    {
        var taskType = string.IsNullOrWhiteSpace(config.Type)
            ? "pollDriver"
            : config.Type.Trim();

        var ctx = new TaskBootstrapContext(
            _drivers,
            _devices,
            Vars,
            GetSnapshot,
            () => _tasks.Values.ToList());

        return RuntimeTaskFactory.Create(taskType, ctx, config);
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
                _ => throw new MdkException(MdkErrorCode.UnsupportedDeviceType, $"Unsupported device type: {config.Type}")
            };

            device.Initialize();
            _devices[config.Id] = device;
        }
    }

    private GpioDevice BuildGpioDevice(MdkSetting.DeviceConfig config, string deviceName)
    {
        var scope = GpioDeviceParameterSet.ParseDriverScopeIds(config.Parameters);
        IReadOnlyDictionary<string, IDriver> driverMap;
        if (scope is null)
        {
            driverMap = _drivers;
        }
        else
        {
            var filtered = new Dictionary<string, IDriver>(StringComparer.OrdinalIgnoreCase);
            foreach (var id in scope)
            {
                if (_drivers.TryGetValue(id, out var d))
                {
                    filtered[id] = d;
                }
            }

            if (filtered.Count == 0)
            {
                throw new MdkException(
                    MdkErrorCode.GpioDriverScopeInvalid,
                    "GPIO driverIds did not match any enabled drivers.");
            }

            driverMap = filtered;
        }

        var bindings = GpioDeviceParameterSet.ParseBindings(config.Parameters);
        if (scope is not null)
        {
            foreach (var b in bindings)
            {
                if (!driverMap.ContainsKey(b.DriverId))
                {
                    throw new MdkException(
                        MdkErrorCode.GpioDriverScopeInvalid,
                        $"GPIO point '{b.Alias}' uses driver '{b.DriverId}' which is outside driverIds scope.");
                }
            }
        }

        var gpioDevice = new GpioDevice(config.Id, deviceName, driverMap, Vars);
        foreach (var binding in bindings)
        {
            if (binding.IsOutput)
            {
                gpioDevice.RegisterOutput(binding.Alias, binding.DriverId, binding.Address);
            }
            else
            {
                gpioDevice.RegisterInput(binding.Alias, binding.DriverId, binding.Address);
            }
        }

        return gpioDevice;
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
