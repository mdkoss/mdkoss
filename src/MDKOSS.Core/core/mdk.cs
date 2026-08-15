using System.Net;
using System.Text.Json;
using MDKOSS.Core.Data;
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
    private readonly object _graphLock = new();
    private readonly MTaskScheduler _scheduler = new();
    private MonitoringServer? _monitoringServer;

    public MdkSetting Setting { get; }

    /// <summary>Path of the JSON setting file last loaded / to persist via <see cref="SaveSetting()"/>.</summary>
    public string? SettingPath { get; set; }

    public MVarStore Vars { get; } = new();

    /// <summary>Recipe presets backed by <see cref="MdkSetting.Recipes"/>.</summary>
    public MdkRecipeManager RecipeManager { get; }

    /// <summary>Alarm catalog + active alarm state.</summary>
    public MdkAlarmManager AlarmManager { get; }

    /// <summary>SQLite persistence for orders, recipes, and teach points.</summary>
    public MdkDataStore DataStore { get; }

    public bool IsRunning { get; private set; }

    public string MonitoringPrefix => _monitoringServer?.Prefix ?? DefaultMonitoringPrefix;

    public MdkRuntime(MdkSetting setting, string? settingPath = null)
    {
        Setting = setting;
        SettingPath = string.IsNullOrWhiteSpace(settingPath) ? null : Path.GetFullPath(settingPath.Trim());
        DataStore = new MdkDataStore(ResolveDatabasePath(setting));
        RecipeManager = new MdkRecipeManager(setting, Vars);
        AlarmManager = new MdkAlarmManager(setting, Vars);
    }

    private static string ResolveDatabasePath(MdkSetting setting)
    {
        var raw = string.IsNullOrWhiteSpace(setting.DatabasePath)
            ? MdkSetting.DefaultDatabasePath
            : setting.DatabasePath.Trim();
        if (Path.IsPathRooted(raw))
        {
            return Path.GetFullPath(raw);
        }

        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, raw));
    }

    public static MdkRuntime CreateFromFile(string settingPath)
    {
        var setting = MdkSetting.Load(settingPath);
        return new MdkRuntime(setting, settingPath);
    }

    /// <summary>
    /// One-time bootstrap for all runtime components.
    /// </summary>
    public void Initialize()
    {
        AppLog.Configure();
        AppLog.Info("MdkRuntime initializing.");

        BootstrapDatabase();
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
            try
            {
                device.Start();
            }
            catch (Exception ex)
            {
                AppLog.Error(ex, $"Failed to start device '{device.Id}'.");
            }
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

    // Load SQLite data and sync into runtime setting / vars.
    private void BootstrapDatabase()
    {
        AppLog.Info($"SQLite database: {DataStore.DatabasePath}");
        DataStore.SyncRecipesWithSetting(Setting);

        var orders = DataStore.ListOrders();
        if (orders.Count > 0)
        {
            Vars.Set(MdkDataStore.OrderListVarKey, DataStore.SerializeOrdersForVar());
        }
    }

    // Seed initial runtime vars from config, then overlay the active recipe.
    private void BootstrapVars()
    {
        foreach (var kv in Setting.Vars)
        {
            Vars.Set(kv.Key, kv.Value);
        }

        RecipeManager.BootstrapActiveRecipe();
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
            () => _tasks.Values.ToList(),
            flowHost: new RuntimeFlowHost(this),
            alarmManager: AlarmManager);

        return RuntimeTaskFactory.Create(taskType, ctx, config);
    }

    /// <summary>Adapts <see cref="MdkRuntime"/> for flow task host ops (MotionTask helpers).</summary>
    private sealed class RuntimeFlowHost(MdkRuntime runtime) : Flow.IFlowRuntimeHost
    {
        public bool TryWriteDigitalOutput(string deviceId, string alias, bool value, out string? error) =>
            runtime.TryWriteDigitalOutput(deviceId, alias, value, out error);

        public DeviceActionResult ExecuteDeviceAction(
            string deviceId,
            string action,
            Dictionary<string, System.Text.Json.JsonElement>? parameters) =>
            runtime.ExecuteDeviceAction(deviceId, action, parameters);

        public bool TryAxisMoveTo(string axisDeviceId, double position, out string? error)
        {
            error = null;
            if (!runtime._devices.TryGetValue(axisDeviceId, out var raw) || raw is not AxisDevice axis)
            {
                error = "axis_not_found";
                return false;
            }

            if (!axis.MoveTo(position))
            {
                error = "axis_move_failed";
                return false;
            }

            return true;
        }

        public bool TryAxisSetMotionEnabled(string axisDeviceId, bool enabled, out string? error)
        {
            error = null;
            if (!runtime._devices.TryGetValue(axisDeviceId, out var raw) || raw is not AxisDevice axis)
            {
                error = "axis_not_found";
                return false;
            }

            if (!axis.SetMotionEnabled(enabled))
            {
                error = "axis_enable_failed";
                return false;
            }

            return true;
        }

        public bool TryAxisJog(string axisDeviceId, double direction, double velocity, out string? error)
        {
            error = null;
            if (!runtime._devices.TryGetValue(axisDeviceId, out var raw) || raw is not AxisDevice axis)
            {
                error = "axis_not_found";
                return false;
            }

            if (!axis.Jog(direction, velocity))
            {
                error = "axis_jog_failed";
                return false;
            }

            return true;
        }

        public bool TryAxisStopMotion(string axisDeviceId, out string? error)
        {
            error = null;
            if (!runtime._devices.TryGetValue(axisDeviceId, out var raw) || raw is not AxisDevice axis)
            {
                error = "axis_not_found";
                return false;
            }

            if (!axis.StopMotion())
            {
                error = "axis_stop_failed";
                return false;
            }

            return true;
        }

        public bool TryPlatformSetMotion(string platformDeviceId, bool enabled, out string? error)
        {
            error = null;
            if (!runtime._devices.TryGetValue(platformDeviceId, out var raw) || raw is not PlatformDevice platform)
            {
                error = "platform_not_found";
                return false;
            }

            if (!platform.SetMotion(enabled))
            {
                error = "platform_set_motion_failed";
                return false;
            }

            return true;
        }

        public bool TryPlatformAxisMoveTo(
            string platformDeviceId,
            string axisLetter,
            double position,
            out string? error)
        {
            error = null;
            if (!runtime._devices.TryGetValue(platformDeviceId, out var raw) || raw is not PlatformDevice platform)
            {
                error = "platform_not_found";
                return false;
            }

            var entry = platform.Axes.FirstOrDefault(a =>
                string.Equals(a.AxisLetter, axisLetter, StringComparison.OrdinalIgnoreCase));
            if (entry is null)
            {
                error = "platform_axis_not_found";
                return false;
            }

            if (!entry.Axis.MoveTo(position))
            {
                error = "platform_axis_move_failed";
                return false;
            }

            return true;
        }

        public bool TryPlatformAxisJog(
            string platformDeviceId,
            string axisLetter,
            double direction,
            double velocity,
            out string? error)
        {
            error = null;
            if (!runtime._devices.TryGetValue(platformDeviceId, out var raw) || raw is not PlatformDevice platform)
            {
                error = "platform_not_found";
                return false;
            }

            var entry = platform.Axes.FirstOrDefault(a =>
                string.Equals(a.AxisLetter, axisLetter, StringComparison.OrdinalIgnoreCase));
            if (entry is null)
            {
                error = "platform_axis_not_found";
                return false;
            }

            if (!entry.Axis.Jog(direction, velocity))
            {
                error = "platform_axis_jog_failed";
                return false;
            }

            return true;
        }

        public bool TryPlatformAxisStopMotion(
            string platformDeviceId,
            string axisLetter,
            out string? error)
        {
            error = null;
            if (!runtime._devices.TryGetValue(platformDeviceId, out var raw) || raw is not PlatformDevice platform)
            {
                error = "platform_not_found";
                return false;
            }

            var entry = platform.Axes.FirstOrDefault(a =>
                string.Equals(a.AxisLetter, axisLetter, StringComparison.OrdinalIgnoreCase));
            if (entry is null)
            {
                error = "platform_axis_not_found";
                return false;
            }

            if (!entry.Axis.StopMotion())
            {
                error = "platform_axis_stop_failed";
                return false;
            }

            return true;
        }

        public bool TryGpioWriteOutput(string gpioDeviceId, string alias, bool value, out string? error) =>
            runtime.TryWriteDigitalOutput(gpioDeviceId, alias, value, out error);

        public bool TryGpioReadInput(string gpioDeviceId, string alias, out bool value, out string? error)
        {
            value = false;
            error = null;
            if (!runtime.TryResolveGpioOrVioDevice(gpioDeviceId, out var raw, out error))
            {
                return false;
            }

            switch (raw)
            {
                case GpioDevice gpio:
                    value = gpio.ReadInput(alias);
                    return true;
                case VioDevice vio:
                    value = vio.ReadInput(alias);
                    return true;
                default:
                    error = "device_not_gpio_or_vio";
                    return false;
            }
        }

        public bool TryGetDeviceSnapshot(
            string deviceId,
            out string? deviceType,
            out string? state,
            out bool driverConnected,
            out string? error)
        {
            deviceType = null;
            state = null;
            driverConnected = false;
            error = null;
            if (!runtime._devices.TryGetValue(deviceId, out var device))
            {
                error = "device_not_found";
                return false;
            }

            var snap = device.GetSnapshot();
            deviceType = snap.Type;
            state = snap.State;
            driverConnected = snap.DriverConnected;
            return true;
        }

        public bool TryEnsureDriverConnected(string deviceId, out string? error)
        {
            error = null;
            if (!runtime._devices.TryGetValue(deviceId, out var device))
            {
                error = "device_not_found";
                return false;
            }

            if (device.LinkedDriver.IsConnected)
            {
                return true;
            }

            error = $"driver_not_connected:{device.LinkedDriver.Name}";
            return false;
        }
    }

    // Instantiate and initialize all enabled devices (general + axes + platforms).
    private void BootstrapDevices()
    {
        Setting.NormalizeSections();
        foreach (var config in Setting.AllDeviceConfigs.Where(d => d.Enabled))
        {
            try
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
                else if (DeviceExtensionRegistry.TryCreate(deviceType, config, deviceName, Vars, _drivers, out var extensionDevice))
                {
                    device = extensionDevice!;
                }
                else if (string.Equals(deviceType, "visiondev", StringComparison.OrdinalIgnoreCase)
                         || string.Equals(deviceType, "vision", StringComparison.OrdinalIgnoreCase))
                {
                    device = BuildVisionDevice(config, deviceName);
                }
                else if (!_drivers.TryGetValue(config.DriverId, out var driver))
                {
                    continue;
                }
                else
                {
                    device = deviceType switch
                    {
                        _ when AxisDeviceParameterSet.IsAxisFamilyType(deviceType) =>
                            new AxisDevice(config.Id, deviceName, driver, Vars, deviceType),
                        "cameradev" => new CameraDevDevice(config.Id, deviceName, driver, Vars),
                        _ => throw new MdkException(MdkErrorCode.UnsupportedDeviceType, $"Unsupported device type: {config.Type}")
                    };
                }

                device.Initialize();
                _devices[config.Id] = device;
            }
            catch (Exception ex)
            {
                AppLog.Error(ex, $"Failed to create device '{config.Id}' ({config.Type}). Skipping.");
            }
        }
    }

    private VisionDevice BuildVisionDevice(MdkSetting.DeviceConfig config, string deviceName)
    {
        var parameters = VisionDeviceParameters.Parse(config.Parameters);
        // Prefer camera from vision config when device param blank.
        if (string.IsNullOrWhiteSpace(parameters.CameraDeviceId)
            && !string.IsNullOrWhiteSpace(parameters.VisionId))
        {
            var vision = Setting.Visions.FirstOrDefault(v =>
                string.Equals(v.Id, parameters.VisionId, StringComparison.OrdinalIgnoreCase));
            if (vision is not null && !string.IsNullOrWhiteSpace(vision.CameraDeviceId))
            {
                parameters = new VisionDeviceParameters
                {
                    VisionId = parameters.VisionId,
                    CameraDeviceId = vision.CameraDeviceId,
                    ImagePath = parameters.ImagePath,
                    ResultPrefix = parameters.ResultPrefix,
                    DebugImagePath = parameters.DebugImagePath,
                    GenerateTestImageWhenMissing = parameters.GenerateTestImageWhenMissing,
                };
            }
        }

        return new VisionDevice(
            config.Id,
            deviceName,
            parameters,
            Vars,
            visionId => Setting.Visions.FirstOrDefault(v =>
                string.Equals(v.Id, visionId, StringComparison.OrdinalIgnoreCase)),
            deviceId => _devices.TryGetValue(deviceId, out var d) ? d : null);
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
        short ordinal = 0;
        foreach (var letter in letters)
        {
            // Prefer composing from an existing Axis device (axis.X = Axis device id).
            var binding = PlatformDeviceParameterSet.TryGetAxisBinding(config.Parameters, letter);
            if (!string.IsNullOrWhiteSpace(binding)
                && _devices.TryGetValue(binding, out var existing)
                && existing is AxisDevice existingAxis)
            {
                var axisCfg = Setting.Axes.FirstOrDefault(a =>
                    string.Equals(a.Id, binding, StringComparison.OrdinalIgnoreCase));
                var driverId = !string.IsNullOrWhiteSpace(axisCfg?.DriverId)
                    ? axisCfg!.DriverId.Trim()
                    : FindDriverId(existingAxis.LinkedDriver) ?? defaultDriverId;
                if (string.IsNullOrWhiteSpace(driverId))
                {
                    return null;
                }

                var fallbackIndex = axisCfg is null
                    ? ordinal
                    : AxisDeviceParameterSet.ParseAxisIndex(axisCfg.Parameters, ordinal);
                var axisIndex = PlatformDeviceParameterSet.ResolveAxisIndex(
                    config.Parameters, letter, fallbackIndex);
                axisRefs.Add(new PlatformAxisRef(letter, driverId, existingAxis, axisIndex));
                ordinal++;
                continue;
            }

            var resolvedDriverId = PlatformDeviceParameterSet.ResolveAxisDriverId(
                config.Parameters,
                letter,
                defaultDriverId,
                id => Setting.Axes
                    .FirstOrDefault(a => string.Equals(a.Id, id, StringComparison.OrdinalIgnoreCase))
                    ?.DriverId);
            if (!_drivers.TryGetValue(resolvedDriverId, out var axisDriver))
            {
                return null;
            }

            var axisIndexFromDriver = PlatformDeviceParameterSet.ResolveAxisIndex(
                config.Parameters, letter, ordinal);
            var axisId = $"{config.Id}.{letter}";
            var axisName = $"{deviceName} {letter}";
            var axisDevice = new AxisDevice(axisId, axisName, axisDriver, Vars);
            axisRefs.Add(new PlatformAxisRef(letter, resolvedDriverId, axisDevice, axisIndexFromDriver));
            ordinal++;
        }

        return new PlatformDevice(config.Id, deviceName, kind, axisRefs, Vars);
    }

    private string? FindDriverId(IDriver driver)
    {
        foreach (var kv in _drivers)
        {
            if (ReferenceEquals(kv.Value, driver))
            {
                return kv.Key;
            }
        }

        return null;
    }

    private GpioDevice BuildGpioDevice(MdkSetting.DeviceConfig config, string deviceName)
    {
        // Prefer one shared GpioDevice: attach every enabled non-vio driver card.
        // Point values (in.*/out.*) distinguish the card via driverId:address.
        var defaultDriverId = string.IsNullOrWhiteSpace(config.DriverId) ? null : config.DriverId.Trim();
        var driverMap = ResolveGpioDriverMap(config);

        var bindings = GpioDeviceParameterSet.ParseBindings(config.Parameters, defaultDriverId);
        foreach (var b in bindings)
        {
            if (!driverMap.ContainsKey(b.DriverId))
            {
                throw new MdkException(
                    MdkErrorCode.GpioDriverScopeInvalid,
                    $"GPIO point '{b.Alias}' uses driver '{b.DriverId}' which is not attached to this GpioDevice " +
                    "(missing, disabled, vio-typed, or outside optional driverIds).");
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

    /// <summary>
    /// Builds the driver map for a GPIO device: all enabled non-vio drivers by default,
    /// or the optional <c>driverIds</c> subset (still excluding vio-typed drivers).
    /// </summary>
    private Dictionary<string, IDriver> ResolveGpioDriverMap(MdkSetting.DeviceConfig config)
    {
        var vioDriverIds = new HashSet<string>(
            Setting.Drivers
                .Where(d => GpioDeviceParameterSet.IsVioDriverType(d.Type))
                .Select(d => d.Id),
            StringComparer.OrdinalIgnoreCase);

        var scope = GpioDeviceParameterSet.ParseDriverScopeIds(config.Parameters);
        var filtered = new Dictionary<string, IDriver>(StringComparer.OrdinalIgnoreCase);

        if (scope is null)
        {
            foreach (var kv in _drivers)
            {
                if (!vioDriverIds.Contains(kv.Key))
                {
                    filtered[kv.Key] = kv.Value;
                }
            }
        }
        else
        {
            foreach (var id in scope)
            {
                if (vioDriverIds.Contains(id))
                {
                    continue;
                }

                if (_drivers.TryGetValue(id, out var d))
                {
                    filtered[id] = d;
                }
            }
        }

        if (filtered.Count == 0)
        {
            throw new MdkException(
                MdkErrorCode.GpioDriverScopeInvalid,
                "GPIO device has no attachable drivers (need at least one enabled non-vio driver).");
        }

        return filtered;
    }

    private VioDevice BuildVioDevice(MdkSetting.DeviceConfig config, string deviceName, IDriver driver)
    {
        var bindings = VioDeviceParameterSet.ParseVirtualBindings(config.Parameters);
        var vio = new VioDevice(config.Id, deviceName, config.DriverId, driver, Vars);
        foreach (var binding in bindings)
        {
            if (binding.IsBidirectional)
            {
                vio.RegisterVirtualPoint(binding.Alias);
            }
            else if (binding.IsOutput)
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

    /// <summary>Looks up a registered device by id.</summary>
    public bool TryGetDevice(string deviceId, out MDeviceBase device)
    {
        return _devices.TryGetValue(deviceId, out device!);
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
            try
            {
                device.Dispose();
            }
            catch (Exception ex)
            {
                AppLog.Error(ex, $"Failed to dispose device '{device.Id}'.");
            }
        }

        _drivers.Clear();
        _devices.Clear();
        _tasks.Clear();

        DataStore.PersistRecipesFromSetting(Setting);
        DataStore.Dispose();

        AppLog.Shutdown();
    }


    /// <summary>
    /// Writes a digital output on a <see cref="GpioDevice"/> or <see cref="VioDevice"/> by logical alias.
    /// Empty <paramref name="deviceId"/> resolves to the first registered <see cref="GpioDevice"/>.
    /// </summary>
    public bool TryWriteDigitalOutput(string deviceId, string alias, bool value, out string? error)
    {
        error = null;
        if (!TryResolveGpioOrVioDevice(deviceId, out var dev, out error))
        {
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
    /// Resolves a GPIO/VIO device. Blank id picks the primary (first) <see cref="GpioDevice"/> so tasks
    /// can operate aliases without naming the device when only one shared GPIO is configured.
    /// </summary>
    internal bool TryResolveGpioOrVioDevice(string? deviceId, out MDeviceBase device, out string? error)
    {
        device = null!;
        error = null;
        if (!string.IsNullOrWhiteSpace(deviceId))
        {
            if (!_devices.TryGetValue(deviceId.Trim(), out device!))
            {
                error = "device_not_found";
                return false;
            }

            return true;
        }

        var gpio = _devices.Values.OfType<GpioDevice>().FirstOrDefault();
        if (gpio is null)
        {
            error = "gpio_device_not_found";
            return false;
        }

        device = gpio;
        return true;
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
            if (DeviceActionRegistry.TryExecute(dev, action, parameters, out var extensionResult))
            {
                return extensionResult;
            }

            return dev switch
            {
                GpioDevice gpio => ExecuteGpioAction(gpio, action, parameters),
                VioDevice vio => ExecuteVioAction(vio, action, parameters),
                AxisDevice axis => ExecuteAxisAction(axis, action, parameters),
                PlatformDevice platform => ExecutePlatformAction(platform, action, parameters),
                VisionDevice vision => VisionDeviceActions.Execute(vision, action, parameters),
                CameraDevDevice camera => ExecuteCameraDevAction(camera, action, parameters),
                _ => DeviceActionResult.Fail("unsupported_device_type")
            };
        }
        catch (Exception ex)
        {
            AppLog.Error(ex, $"Device action failed: {deviceId}.{action}");
            return DeviceActionResult.Fail("exception: " + ex.Message);
        }
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

        if (action.Equals("stop", StringComparison.OrdinalIgnoreCase))
        {
            return axis.StopMotion()
                ? DeviceActionResult.Ok()
                : DeviceActionResult.Fail("stop_failed");
        }

        if (action.Equals("jog", StringComparison.OrdinalIgnoreCase) && parameters != null)
        {
            var direction = parameters.TryGetValue("direction", out var dirElem)
                ? dirElem.GetDouble()
                : 0;
            var velocity = parameters.TryGetValue("velocity", out var velElem)
                ? velElem.GetDouble()
                : 1.0;
            return axis.Jog(direction, velocity)
                ? DeviceActionResult.Ok()
                : DeviceActionResult.Fail("jog_failed");
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

        if (action.Equals("moveAxis", StringComparison.OrdinalIgnoreCase)
            && parameters != null
            && parameters.TryGetValue("axis", out var axisElem)
            && parameters.TryGetValue("position", out var posElem))
        {
            var letter = axisElem.GetString() ?? "";
            var position = posElem.GetDouble();
            var entry = platform.Axes.FirstOrDefault(a =>
                string.Equals(a.AxisLetter, letter, StringComparison.OrdinalIgnoreCase));
            if (entry is null)
            {
                return DeviceActionResult.Fail("platform_axis_not_found");
            }

            return entry.Axis.MoveTo(position)
                ? DeviceActionResult.Ok()
                : DeviceActionResult.Fail("move_failed");
        }

        return DeviceActionResult.Fail("unknown_action");
    }

    private static DeviceActionResult ExecuteCameraDevAction(
        CameraDevDevice camera,
        string action,
        Dictionary<string, JsonElement>? parameters)
    {
        if (action.Equals("trigger", StringComparison.OrdinalIgnoreCase)
            || action.Equals("capture", StringComparison.OrdinalIgnoreCase))
        {
            var recipe = "default";
            if (parameters is not null
                && parameters.TryGetValue("recipe", out var recipeEl)
                && recipeEl.ValueKind == JsonValueKind.String)
            {
                recipe = recipeEl.GetString() ?? "default";
            }

            return camera.TriggerCapture(recipe)
                ? DeviceActionResult.Ok(new { recipe })
                : DeviceActionResult.Fail("capture_failed");
        }

        if (action.Equals("status", StringComparison.OrdinalIgnoreCase))
        {
            return DeviceActionResult.Ok(camera.GetSnapshot());
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

    /// <summary>Exposes recipe state for WinForms and monitoring tools.</summary>
    public RecipeSnapshot GetRecipeSnapshot() => RecipeManager.GetSnapshot();

    /// <summary>Applies a recipe by id at runtime.</summary>
    public bool TryApplyRecipe(string recipeId, out string? error)
    {
        if (!RecipeManager.TryApplyRecipe(recipeId, out error))
        {
            return false;
        }

        DataStore.PersistRecipesFromSetting(Setting);
        return true;
    }

    /// <summary>Persists the current setting to <see cref="SettingPath"/> (JSON + recipe SQLite + config tables).</summary>
    public void SaveSetting()
    {
        if (string.IsNullOrWhiteSpace(SettingPath))
        {
            throw new InvalidOperationException("setting_path_unset");
        }

        SaveSetting(SettingPath);
    }

    /// <summary>Persists the current setting (including recipes) to disk and SQLite config tables.</summary>
    public void SaveSetting(string settingPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(settingPath);
        DataStore.PersistRecipesFromSetting(Setting);
        Setting.Save(settingPath);
        SettingPath = Path.GetFullPath(settingPath);
        try
        {
            using var configStore = new MdkConfigStore(DataStore.DatabasePath);
            configStore.ExportSetting(Setting, SettingPath);
        }
        catch (Exception ex)
        {
            AppLog.Info($"Config SQLite export skipped: {ex.Message}");
        }
    }

    /// <summary>Refreshes <see cref="MdkDataStore.OrderListVarKey"/> from SQLite.</summary>
    public void RefreshOrderListVar() =>
        Vars.Set(MdkDataStore.OrderListVarKey, DataStore.SerializeOrdersForVar());

    /// <summary>Upserts an order and refreshes the runtime order list var.</summary>
    public bool TryUpsertOrder(ProductionOrderRecord order, out string? error)
    {
        if (!DataStore.TryUpsertOrder(order, out error))
        {
            return false;
        }

        RefreshOrderListVar();
        return true;
    }

    /// <summary>Deletes an order and refreshes the runtime order list var.</summary>
    public bool TryDeleteOrder(string orderId, out string? error)
    {
        if (!DataStore.TryDeleteOrder(orderId, out error))
        {
            return false;
        }

        RefreshOrderListVar();
        return true;
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
