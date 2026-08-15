using MDKOSS.Core;
using MDKOSS.Core.Drivers;
using MDKOSS.Extensions;

namespace MDKOSS.Sample.SampleExt;

/// <summary>
/// Sample 扩展示例包：自定义设备 <c>samplebeacon</c>、MotionTask <c>samplemotion</c>、
/// API <c>/api/sampleext</c>（含运行截图与钉钉发布）、页面 <c>demo_sample_ext.html</c>。
/// </summary>
public sealed class SampleExtExtension : IMdkExtension
{
    public string Id => "sample-ext";

    public string DisplayName => "Sample Extension Demo";

    public void Register(IExtensionRegistration registration)
    {
        ArgumentNullException.ThrowIfNull(registration);

        registration.Device("samplebeacon", (cfg, name, vars, _) =>
            new SampleBeaconDevice(cfg.Id, name, cfg.Parameters, vars));

        registration.Action(
            device => device is SampleBeaconDevice,
            (device, action, parameters) =>
                SampleBeaconActions.Execute((SampleBeaconDevice)device, action, parameters));

        registration.Task("samplemotion", CreateMotionDemo);
        registration.Task("samplemotiondemo", CreateMotionDemo);

        registration.MonitoringModule(runtime => new SampleExtApiModule(runtime));
        registration.StaticPage("/demo_sample_ext.html", () => SampleExtViewPages.DemoHtml);
    }

    private static MTaskBase? CreateMotionDemo(TaskBootstrapContext ctx, MdkSetting.TaskConfig config, string taskTypeKey)
    {
        if (!TryResolveDriver(ctx, config, out var driver) || driver is null)
        {
            return null;
        }

        var taskName = string.IsNullOrWhiteSpace(config.Name) ? taskTypeKey : config.Name;
        return new SampleMotionDemoTask(
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
