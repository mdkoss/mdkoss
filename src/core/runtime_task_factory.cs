using MDKOSS.Core.Drivers;
using MDKOSS.Tasks;

namespace MDKOSS.Core;

/// <summary>Dependencies needed to construct tasks from <see cref="MdkSetting.TaskConfig"/>.</summary>
public sealed class TaskBootstrapContext
{
    public TaskBootstrapContext(
        IReadOnlyDictionary<string, IDriver> drivers,
        IReadOnlyDictionary<string, MDeviceBase> devices,
        MVarStore vars,
        Func<RuntimeSnapshot> getSnapshot,
        Func<IReadOnlyList<MTaskBase>> listTasks)
    {
        Drivers = drivers;
        Devices = devices;
        Vars = vars;
        GetSnapshot = getSnapshot;
        ListTasks = listTasks;
    }

    public IReadOnlyDictionary<string, IDriver> Drivers { get; }

    public IReadOnlyDictionary<string, MDeviceBase> Devices { get; }

    public MVarStore Vars { get; }

    public Func<RuntimeSnapshot> GetSnapshot { get; }

    public Func<IReadOnlyList<MTaskBase>> ListTasks { get; }
}

/// <summary>Registry for task implementations keyed by config <c>type</c> string.</summary>
public static class RuntimeTaskFactory
{
    private delegate MTaskBase? CreateFn(TaskBootstrapContext ctx, MdkSetting.TaskConfig config, string taskTypeKey);

    private static readonly Dictionary<string, CreateFn> Registry = new(StringComparer.OrdinalIgnoreCase);

    static RuntimeTaskFactory()
    {
        RegisterCore("poll", CreatePollDriver);
        RegisterCore("polldriver", CreatePollDriver);
        RegisterCore("operation", CreateOperation);
        RegisterCore("taskoperation", CreateOperation);
        RegisterCore("cycle", CreateCycle);
        RegisterCore("taskcycle", CreateCycle);
    }

    /// <summary>Registers or replaces a factory for the given task type key.</summary>
    public static void Register(string type, Func<TaskBootstrapContext, MdkSetting.TaskConfig, string, MTaskBase?> factory)
    {
        if (string.IsNullOrWhiteSpace(type))
        {
            throw new ArgumentException("Task type cannot be empty.", nameof(type));
        }

        ArgumentNullException.ThrowIfNull(factory);
        Registry[type.Trim()] = (ctx, cfg, key) => factory(ctx, cfg, key);
    }

    /// <summary>Returns whether a factory exists for the type.</summary>
    public static bool IsSupported(string? type)
    {
        return !string.IsNullOrWhiteSpace(type) && Registry.ContainsKey(type.Trim());
    }

    /// <summary>Creates a task instance, or null when optional prerequisites are missing (e.g. driver id).</summary>
    /// <exception cref="NotSupportedException">Unknown task type.</exception>
    public static MTaskBase? Create(string taskTypeKey, TaskBootstrapContext ctx, MdkSetting.TaskConfig config)
    {
        if (!Registry.TryGetValue(taskTypeKey, out var factory))
        {
            throw new NotSupportedException($"Unsupported task type: {config.Type}");
        }

        return factory(ctx, config, taskTypeKey);
    }

    private static void RegisterCore(string type, CreateFn fn) => Registry[type] = fn;

    private static MTaskBase? CreatePollDriver(TaskBootstrapContext ctx, MdkSetting.TaskConfig config, string taskTypeKey)
    {
        if (!ctx.Drivers.TryGetValue(config.DriverId, out var driver))
        {
            return null;
        }

        var taskName = string.IsNullOrWhiteSpace(config.Name) ? taskTypeKey : config.Name;
        return new PollDriverTask(taskName, config.IntervalMs, driver, ctx.Vars);
    }

    private static MTaskBase? CreateOperation(TaskBootstrapContext ctx, MdkSetting.TaskConfig config, string taskTypeKey)
    {
        _ = taskTypeKey;
        var gpio = ResolveTaskGpio(ctx, config.Parameters);
        return new TaskOperationTask(ctx.Vars, gpio, config.IntervalMs);
    }

    private static MTaskBase? CreateCycle(TaskBootstrapContext ctx, MdkSetting.TaskConfig config, string taskTypeKey)
    {
        _ = taskTypeKey;
        return new TaskCycleTask(ctx.Vars, ctx.GetSnapshot, ctx.ListTasks, config.IntervalMs);
    }

    private static GpioDevice? ResolveTaskGpio(TaskBootstrapContext ctx, IReadOnlyDictionary<string, string> parameters)
    {
        if (parameters.TryGetValue("gpioDeviceId", out var deviceId) && !string.IsNullOrWhiteSpace(deviceId))
        {
            if (ctx.Devices.TryGetValue(deviceId, out var mapped) && mapped is GpioDevice gpioDevice)
            {
                return gpioDevice;
            }
        }

        return ctx.Devices.Values.OfType<GpioDevice>().FirstOrDefault();
    }
}
