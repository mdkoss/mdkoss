using System.Net;
using System.Text.Json;
using MDKOSS.Core.Drivers;
using MDKOSS.Core.Monitor;

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
            else if (string.Equals(deviceType, "serialdev", StringComparison.OrdinalIgnoreCase))
            {
                device = BuildSerialDevice(config, deviceName);
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

    private SerialDevice BuildSerialDevice(MdkSetting.DeviceConfig config, string deviceName)
    {
        var serialConfig = SerialDeviceParameterSet.ParseConfig(config.Parameters);
        return new SerialDevice(config.Id, deviceName, serialConfig, Vars);
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

    /// <summary>Gets serial device status for monitoring.</summary>
    public object? GetSerialStatus(string deviceId)
    {
        if (!_devices.TryGetValue(deviceId, out var dev) || dev is not SerialDevice serial)
        {
            return null;
        }

        return new
        {
            isOpen = serial.IsOpen,
            portName = serial.Config.PortName,
            baudRate = serial.Config.BaudRate,
            dataBits = serial.Config.DataBits,
            parity = serial.Config.Parity.ToString(),
            stopBits = serial.Config.StopBits.ToString(),
            bytesToRead = serial.BytesToRead
        };
    }

    /// <summary>Opens a serial port.</summary>
    public SerialErrorCode OpenSerialPort(string deviceId, SerialPortConfig config)
    {
        if (!_devices.TryGetValue(deviceId, out var dev) || dev is not SerialDevice serial)
        {
            return SerialErrorCode.PortNotFound;
        }

        // Temporarily update config and open
        var originalConfig = serial.Config;
        serial.SetParameters(config);
        var result = serial.Open();

        // Revert to stored config if open failed
        if (result != SerialErrorCode.Ok)
        {
            serial.SetParameters(originalConfig);
        }

        return result;
    }

    /// <summary>Closes a serial port.</summary>
    public SerialErrorCode CloseSerialPort(string deviceId)
    {
        if (!_devices.TryGetValue(deviceId, out var dev) || dev is not SerialDevice serial)
        {
            return SerialErrorCode.PortNotFound;
        }

        return serial.Close();
    }

    /// <summary>Updates serial port configuration.</summary>
    public SerialErrorCode SetSerialConfig(string deviceId, SerialPortConfig config)
    {
        if (!_devices.TryGetValue(deviceId, out var dev) || dev is not SerialDevice serial)
        {
            return SerialErrorCode.PortNotFound;
        }

        return serial.SetParameters(config);
    }

    /// <summary>Writes text data to serial port.</summary>
    public SerialErrorCode WriteSerialText(string deviceId, string data)
    {
        if (!_devices.TryGetValue(deviceId, out var dev) || dev is not SerialDevice serial)
        {
            return SerialErrorCode.PortNotFound;
        }

        return serial.Write(data);
    }

    /// <summary>Writes binary data to serial port.</summary>
    public SerialErrorCode WriteSerialBinary(string deviceId, byte[] data)
    {
        if (!_devices.TryGetValue(deviceId, out var dev) || dev is not SerialDevice serial)
        {
            return SerialErrorCode.PortNotFound;
        }

        return serial.WriteBinary(data);
    }

    /// <summary>Reads all available data from serial port.</summary>
    public (SerialErrorCode error, string? data) ReadSerialAll(string deviceId)
    {
        if (!_devices.TryGetValue(deviceId, out var dev) || dev is not SerialDevice serial)
        {
            return (SerialErrorCode.PortNotFound, null);
        }

        return serial.ReadAll();
    }

    /// <summary>Discards serial port buffers.</summary>
    public SerialErrorCode DiscardSerialBuffers(string deviceId)
    {
        if (!_devices.TryGetValue(deviceId, out var dev) || dev is not SerialDevice serial)
        {
            return SerialErrorCode.PortNotFound;
        }

        return serial.DiscardBuffers();
    }

    /// <summary>Executes a device action via unified API.</summary>
    public DeviceActionResult ExecuteDeviceAction(string deviceId, string action, Dictionary<string, JsonElement>? parameters)
    {
        if (!_devices.TryGetValue(deviceId, out var dev))
        {
            return DeviceActionResult.Fail("device_not_found");
        }

        try
        {
            return dev switch
            {
                SerialDevice serial => ExecuteSerialAction(serial, action, parameters),
                GpioDevice gpio => ExecuteGpioAction(gpio, action, parameters),
                VioDevice vio => ExecuteVioAction(vio, action, parameters),
                AxisDevice axis => ExecuteAxisAction(axis, action, parameters),
                PlatformDevice platform => ExecutePlatformAction(platform, action, parameters),
                _ => DeviceActionResult.Fail("unsupported_device_type")
            };
        }
        catch (Exception ex)
        {
            AppLog.Error(ex, $"Device action failed: {deviceId}.{action}");
            return DeviceActionResult.Fail("exception: " + ex.Message);
        }
    }

    private static DeviceActionResult ExecuteSerialAction(SerialDevice serial, string action, Dictionary<string, JsonElement>? parameters)
    {
        return action.ToLowerInvariant() switch
        {
            "open" => serial.Open() == SerialErrorCode.Ok ? DeviceActionResult.Ok() : DeviceActionResult.Fail("open_failed"),
            "close" => serial.Close() == SerialErrorCode.Ok ? DeviceActionResult.Ok() : DeviceActionResult.Fail("close_failed"),
            "write" when parameters != null && parameters.TryGetValue("data", out var data) =>
                serial.Write(data.GetString() ?? "") == SerialErrorCode.Ok ? DeviceActionResult.Ok() : DeviceActionResult.Fail("write_failed"),
            "read" => HandleSerialRead(serial),
            "status" => DeviceActionResult.Ok(new { isOpen = serial.IsOpen, bytesToRead = serial.BytesToRead }),
            _ => DeviceActionResult.Fail("unknown_action")
        };
    }

    private static DeviceActionResult HandleSerialRead(SerialDevice serial)
    {
        var (err, data) = serial.ReadAll();
        if (err == SerialErrorCode.Ok && data != null)
        {
            return DeviceActionResult.Ok(new { data });
        }
        return DeviceActionResult.Fail("read_failed");
    }

    private static DeviceActionResult ExecuteGpioAction(GpioDevice gpio, string action, Dictionary<string, JsonElement>? parameters)
    {
        if (action.Equals("write", StringComparison.OrdinalIgnoreCase) && parameters != null)
        {
            if (!parameters.TryGetValue("alias", out var aliasElem) || !parameters.TryGetValue("value", out var valueElem))
            {
                return DeviceActionResult.Fail("missing_parameters");
            }
            var alias = aliasElem.GetString();
            var value = valueElem.GetBoolean();
            return gpio.WriteOutput(alias ?? "", value)
                ? DeviceActionResult.Ok()
                : DeviceActionResult.Fail("write_failed");
        }

        if (action.Equals("read", StringComparison.OrdinalIgnoreCase) && parameters != null)
        {
            if (!parameters.TryGetValue("alias", out var aliasElem))
            {
                return DeviceActionResult.Fail("missing_alias");
            }
            var alias = aliasElem.GetString();
            var value = gpio.ReadInput(alias ?? "");
            return DeviceActionResult.Ok(new { value });
        }

        return DeviceActionResult.Fail("unknown_action");
    }

    private static DeviceActionResult ExecuteVioAction(VioDevice vio, string action, Dictionary<string, JsonElement>? parameters)
    {
        if (action.Equals("write", StringComparison.OrdinalIgnoreCase) && parameters != null)
        {
            if (!parameters.TryGetValue("alias", out var aliasElem) || !parameters.TryGetValue("value", out var valueElem))
            {
                return DeviceActionResult.Fail("missing_parameters");
            }
            var alias = aliasElem.GetString();
            var value = valueElem.GetBoolean();
            return vio.WriteOutput(alias ?? "", value)
                ? DeviceActionResult.Ok()
                : DeviceActionResult.Fail("write_failed");
        }

        if (action.Equals("read", StringComparison.OrdinalIgnoreCase) && parameters != null)
        {
            if (!parameters.TryGetValue("alias", out var aliasElem))
            {
                return DeviceActionResult.Fail("missing_alias");
            }
            var alias = aliasElem.GetString();
            var value = vio.ReadInput(alias ?? "");
            return DeviceActionResult.Ok(new { value });
        }

        return DeviceActionResult.Fail("unknown_action");
    }

    private static DeviceActionResult ExecuteAxisAction(AxisDevice axis, string action, Dictionary<string, JsonElement>? parameters)
    {
        if (action.Equals("move", StringComparison.OrdinalIgnoreCase) && parameters != null && parameters.TryGetValue("position", out var posElem))
        {
            var position = posElem.GetDouble();
            return axis.MoveTo(position)
                ? DeviceActionResult.Ok()
                : DeviceActionResult.Fail("move_failed");
        }

        if (action.Equals("enable", StringComparison.OrdinalIgnoreCase))
        {
            return axis.SetMotionEnabled(true)
                ? DeviceActionResult.Ok()
                : DeviceActionResult.Fail("enable_failed");
        }

        if (action.Equals("disable", StringComparison.OrdinalIgnoreCase))
        {
            return axis.SetMotionEnabled(false)
                ? DeviceActionResult.Ok()
                : DeviceActionResult.Fail("disable_failed");
        }

        return DeviceActionResult.Fail("unknown_action");
    }

    private static DeviceActionResult ExecutePlatformAction(PlatformDevice platform, string action, Dictionary<string, JsonElement>? parameters)
    {
        if (action.Equals("enable", StringComparison.OrdinalIgnoreCase))
        {
            return platform.SetMotion(true)
                ? DeviceActionResult.Ok()
                : DeviceActionResult.Fail("enable_failed");
        }

        if (action.Equals("disable", StringComparison.OrdinalIgnoreCase))
        {
            return platform.SetMotion(false)
                ? DeviceActionResult.Ok()
                : DeviceActionResult.Fail("disable_failed");
        }

        return DeviceActionResult.Fail("unknown_action");
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

    /// <summary>Exposes task state for WinForms and monitoring tools.</summary>
    public IReadOnlyList<TaskSnapshot> GetTaskSnapshots()
    {
        return _tasks.Values
            .OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
            .Select(t => new TaskSnapshot(t.Name, t.GetType().Name, t.IntervalMs, t.State.ToString()))
            .ToList();
    }
}

public sealed record DriverSnapshot(string Type, bool IsConnected);

public sealed record RuntimeSnapshot(
    string ProjectName,
    bool IsRunning,
    IReadOnlyDictionary<string, DriverSnapshot> Drivers,
    IReadOnlyDictionary<string, DeviceSnapshot> Devices,
    IReadOnlyDictionary<string, object?> Vars);

public sealed record TaskSnapshot(string Name, string Type, int IntervalMs, string State);

/// <summary>Result of a device action execution.</summary>
public sealed class DeviceActionResult
{
    public bool Success { get; private init; }
    public string? Error { get; private init; }
    public object? Data { get; private init; }

    private DeviceActionResult(bool success, string? error, object? data)
    {
        Success = success;
        Error = error;
        Data = data;
    }

    public static DeviceActionResult Ok(object? data = null) => new(true, null, data);
    public static DeviceActionResult Fail(string error) => new(false, error, null);
}
