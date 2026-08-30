using MDKOSS.Core;
using MDKOSS.Core.Drivers;
using MDKOSS.Extensions;

namespace MDKOSS.Tools.Calib.Calib;

/// <summary>Registers code-based calibration MotionTask types.</summary>
public sealed class CalibExtension : IMdkExtension
{
    public string Id => "tools-calib";

    public string DisplayName => "Calibration tool tasks";

    public void Register(IExtensionRegistration registration)
    {
        ArgumentNullException.ThrowIfNull(registration);
        registration.Task("calib.axisoffset", CreateAxisOffset);
        registration.Task("calibaxisoffset", CreateAxisOffset);
        registration.Task("calib.ninepoint", CreateNinePoint);
        registration.Task("calibninepoint", CreateNinePoint);
        registration.Task("calib.platformoffset", CreatePlatformOffset);
        registration.Task("calibplatformoffset", CreatePlatformOffset);
    }

    private static MTaskBase? CreateAxisOffset(TaskBootstrapContext ctx, MdkSetting.TaskConfig config, string taskTypeKey)
        => Create(ctx, config, taskTypeKey, (name, interval, driver, vars, devices, p, alarms) =>
            new AxisOffsetCalibTask(name, interval, driver, vars, devices, p, alarms));

    private static MTaskBase? CreateNinePoint(TaskBootstrapContext ctx, MdkSetting.TaskConfig config, string taskTypeKey)
        => Create(ctx, config, taskTypeKey, (name, interval, driver, vars, devices, p, alarms) =>
            new NinePointCalibTask(name, interval, driver, vars, devices, p, alarms));

    private static MTaskBase? CreatePlatformOffset(TaskBootstrapContext ctx, MdkSetting.TaskConfig config, string taskTypeKey)
        => Create(ctx, config, taskTypeKey, (name, interval, driver, vars, devices, p, alarms) =>
            new PlatformOffsetCalibTask(name, interval, driver, vars, devices, p, alarms));

    private static MTaskBase? Create(
        TaskBootstrapContext ctx,
        MdkSetting.TaskConfig config,
        string taskTypeKey,
        Func<string, int, IDriver, MVarStore, IReadOnlyDictionary<string, MDeviceBase>, IReadOnlyDictionary<string, string>?, MdkAlarmManager?, MTaskBase> factory)
    {
        if (!TryResolveDriver(ctx, config, out var driver) || driver is null)
        {
            return null;
        }

        var taskName = string.IsNullOrWhiteSpace(config.Name) ? taskTypeKey : config.Name;
        return factory(taskName, config.IntervalMs, driver, ctx.Vars, ctx.Devices, config.Parameters, ctx.AlarmManager);
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

public static class CalibExtensionBootstrap
{
    public static void Register() => MdkExtensionHost.Register(new CalibExtension());
}
