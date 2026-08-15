using MDKOSS.Core;
using MDKOSS.Core.Drivers;
using MDKOSS.Extensions;

namespace MDKOSS.Sample.Dispenser.Machine;

/// <summary>
/// 三轴点胶机扩展：任务 <c>dispense</c>、API <c>/api/dispense</c>、页面 <c>indexDispenser.html</c>。
/// </summary>
public sealed class DispenserExtension : IMdkExtension
{
    public string Id => "sample-dispenser";

    public string DisplayName => "Sample 3-Axis Dispenser";

    public void Register(IExtensionRegistration registration)
    {
        ArgumentNullException.ThrowIfNull(registration);

        registration.Task("dispense", CreateDispenseCycle);
        registration.Task("dispensecycle", CreateDispenseCycle);
        registration.Task("dispenser", CreateDispenseCycle);

        registration.MonitoringModule(runtime => new DispenserApiModule(runtime));
        registration.StaticPage("/indexDispenser.html", () => DispenserViewPages.IndexHtml);

        DispenseLogStore.Info("bootstrap", "Dispenser tasks / API / pages registered");
    }

    private static MTaskBase? CreateDispenseCycle(TaskBootstrapContext ctx, MdkSetting.TaskConfig config, string taskTypeKey)
    {
        if (!TryResolveDriver(ctx, config, out var driver) || driver is null)
        {
            return null;
        }

        var taskName = string.IsNullOrWhiteSpace(config.Name) ? taskTypeKey : config.Name;
        return new DispenseCycleTask(
            taskName,
            config.IntervalMs,
            driver,
            ctx.Vars,
            ctx.Devices,
            config.Parameters,
            ctx.AlarmManager);
    }

    private static bool TryResolveDriver(TaskBootstrapContext ctx, MdkSetting.TaskConfig config, out IDriver? driver)
    {
        if (!string.IsNullOrWhiteSpace(config.DriverId)
            && ctx.Drivers.TryGetValue(config.DriverId, out var mapped))
        {
            driver = mapped;
            return true;
        }

        driver = ctx.Drivers.Values.FirstOrDefault();
        return driver is not null;
    }
}
