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

            MDeviceBase device;
            if (string.Equals(deviceType, "gpio", StringComparison.OrdinalIgnoreCase))
            {
                device = BuildGpioDevice(config, deviceName);
            }
            else if (string.Equals(deviceType, "vio", StringComparison.OrdinalIgnoreCase))
            {
                if (!_drivers.TryGetValue(config.DriverId, out var vioDriver))
                {
                    continue;
                }

                device = BuildVioDevice(config, deviceName, vioDriver);
            }
            else if (PlatformDeviceParameterSet.IsPlatformFamilyType(deviceType))
            {
                var platform = BuildPlatformDevice(config, deviceName, deviceType);
                if (platform is null)
                {
                    continue;
                }

                device = platform;
            }
            else if (!_drivers.TryGetValue(config.DriverId, out var driver))
            {
                continue;
            }
            else
            {
                device = deviceType switch
                {
                    "axis" => new AxisDevice(config.Id, deviceName, driver, Vars),
                    "cameradev" => new CameraDevDevice(config.Id, deviceName, driver, Vars),
                    _ => throw new MdkException(MdkErrorCode.UnsupportedDeviceType, $"Unsupported device type: {config.Type}")
                };
            }

            device.Initialize();
            _devices[config.Id] = device;
        }
    }

    private PlatformDevice? BuildPlatformDevice(MdkSetting.DeviceConfig config, string deviceName, string deviceTypeLower)
    {
        MPlatformKind? fromAlias = PlatformDeviceParameterSet.TryKindFromDeviceType(deviceTypeLower, out var k)
            ? k
            : (MPlatformKind?)null;

        var kind = PlatformDeviceParameterSet.ParseKindOrDefault(config.Parameters, fromAlias);
        var letters = kind.AxisLetters();
        var defaultDriverId = config.DriverId ?? string.Empty;
        var axisRefs = new List<PlatformAxisRef>();
        foreach (var letter in letters)
        {
            var driverId = PlatformDeviceParameterSet.ResolveAxisDriverId(config.Parameters, letter, defaultDriverId);
            if (!_drivers.TryGetValue(driverId, out var axisDriver))
            {
                return null;
            }

            var axisId = $"{config.Id}.{letter}";
            var axisName = $"{deviceName} {letter}";
            var axisDevice = new AxisDevice(axisId, axisName, axisDriver, Vars);
            axisRefs.Add(new PlatformAxisRef(letter, driverId, axisDevice));
        }

        return new PlatformDevice(config.Id, deviceName, kind, axisRefs, Vars);
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

    private VioDevice BuildVioDevice(MdkSetting.DeviceConfig config, string deviceName, IDriver driver)
    {
        var bindings = VioDeviceParameterSet.ParseVirtualBindings(config.Parameters);
        var vio = new VioDevice(config.Id, deviceName, config.DriverId, driver, Vars);
        foreach (var binding in bindings)
        {
            if (binding.IsOutput)
            {
                vio.RegisterVirtualOutput(binding.Alias);
            }
            else
            {
                vio.RegisterVirtualInput(binding.Alias);
            }
        }

        return vio;
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
    /// Writes a digital output on a <see cref="GpioDevice"/> or <see cref="VioDevice"/> by logical alias.
    /// </summary>
    public bool TryWriteDigitalOutput(string deviceId, string alias, bool value, out string? error)
    {
        error = null;
        if (!_devices.TryGetValue(deviceId, out var dev))
        {
            error = "device_not_found";
            return false;
        }

        switch (dev)
        {
            case GpioDevice gpio:
                if (!gpio.WriteOutput(alias, value))
                {
                    error = "write_failed";
                    return false;
                }

                return true;
            case VioDevice vio:
                if (!vio.WriteOutput(alias, value))
                {
                    error = "write_failed";
                    return false;
                }

                return true;
            default:
                error = "device_not_gpio_or_vio";
                return false;
        }
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
