using MDKOSS.Core;
using MDKOSS.Core.Drivers;
using MDKOSS.Extensions;

namespace MDKOSS.Sample.DieBonder.Machine;

/// <summary>
/// 半导体贴片机扩展：任务 <c>bond</c> / <c>materialConveyor</c>、
/// API <c>/api/bond</c>、页面 <c>indexDieBonder.html</c> / <c>monitorDieBonder.html</c>。
/// Tray 设备仍由 <c>MDKOSS.Sample.Pnp</c> 提供。
/// </summary>
public sealed class DieBonderExtension : IMdkExtension
{
    public string Id => "sample-diebonder";

    public string DisplayName => "Sample Die Bonder";

    public void Register(IExtensionRegistration registration)
    {
        ArgumentNullException.ThrowIfNull(registration);

        registration.Task("bond", CreateBondCycle);
        registration.Task("bondcycle", CreateBondCycle);
        registration.Task("diebond", CreateBondCycle);

        registration.Task("materialconveyor", CreateMaterialConveyor);
        registration.Task("bondconveyor", CreateMaterialConveyor);

        registration.MonitoringModule(runtime => new DieBonderApiModule(runtime));
        registration.StaticPage("/indexDieBonder.html", () => DieBonderViewPages.IndexHtml);
        registration.StaticPage("/monitorDieBonder.html", () => DieBonderViewPages.MonitorHtml);

        BondLogStore.Info("bootstrap", "Die Bonder tasks / API / pages registered");
    }

    private static MTaskBase? CreateBondCycle(TaskBootstrapContext ctx, MdkSetting.TaskConfig config, string taskTypeKey)
    {
        if (!TryResolveDriver(ctx, config, out var driver) || driver is null)
        {
            return null;
        }

        var taskName = string.IsNullOrWhiteSpace(config.Name) ? taskTypeKey : config.Name;
        return new BondCycleTask(taskName, config.IntervalMs, driver, ctx.Vars, ctx.Devices, config.Parameters, ctx.AlarmManager);
    }

    private static MTaskBase? CreateMaterialConveyor(TaskBootstrapContext ctx, MdkSetting.TaskConfig config, string taskTypeKey)
    {
        if (!TryResolveDriver(ctx, config, out var driver) || driver is null)
        {
            return null;
        }

        var taskName = string.IsNullOrWhiteSpace(config.Name) ? taskTypeKey : config.Name;
        return new MaterialConveyorTask(taskName, config.IntervalMs, driver, ctx.Vars, ctx.Devices, config.Parameters, ctx.AlarmManager);
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
