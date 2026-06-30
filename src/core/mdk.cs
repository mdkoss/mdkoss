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
    private readonly MTaskScheduler _scheduler = new();
    private MonitoringServer? _monitoringServer;

    public MdkSetting Setting { get; }

    public MVarStore Vars { get; } = new();

    /// <summary>Recipe presets backed by <see cref="MdkSetting.Recipes"/>.</summary>
    public MdkRecipeManager RecipeManager { get; }

    /// <summary>SQLite persistence for orders, recipes, and teach points.</summary>
    public MdkDataStore DataStore { get; }

    public bool IsRunning { get; private set; }

    public string MonitoringPrefix => _monitoringServer?.Prefix ?? DefaultMonitoringPrefix;

    public MdkRuntime(MdkSetting setting)
    {
        Setting = setting;
        DataStore = new MdkDataStore(ResolveDatabasePath(setting));
        RecipeManager = new MdkRecipeManager(setting, Vars);
    }

    private static string ResolveDatabasePath(MdkSetting setting) =>
        string.IsNullOrWhiteSpace(setting.DatabasePath)
            ? MdkSetting.DefaultDatabasePath
            : setting.DatabasePath.Trim();

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
            else if (DeviceExtensionRegistry.TryCreate(deviceType, config, deviceName, Vars, _drivers, out var extensionDevice))
            {
                device = extensionDevice!;
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
            device.Dispose();
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

    /// <summary>Exposes recipe state for WinForms and monitoring tools.</summary>
    public RecipeSnapshot GetRecipeSnapshot() => RecipeManager.GetSnapshot();

    /// <summary>Applies a recipe by id at runtime.</summary>
    public bool TryApplyRecipe(string recipeId, out string? error) =>
        RecipeManager.TryApplyRecipe(recipeId, out error);

    /// <summary>Persists the current setting (including recipes) to disk and SQLite.</summary>
    public void SaveSetting(string settingPath)
    {
        DataStore.PersistRecipesFromSetting(Setting);
        Setting.Save(settingPath);
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
