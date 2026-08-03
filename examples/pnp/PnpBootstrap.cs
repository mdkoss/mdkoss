using System.Text.Json;
using MDKOSS.Core;
using MDKOSS.Core.Drivers;
using MDKOSS.Core.Monitor;
using MDKOSS.Extensions;

namespace MDKOSS.Pnp;

/// <summary>PNP machine-type extension (tray device, tasks, API, pages).</summary>
public sealed class PnpExtension : IMdkExtension
{
    public string Id => "pnp";

    public string DisplayName => "Pick and Place machine";

    public void Register(IExtensionRegistration registration)
    {
        ArgumentNullException.ThrowIfNull(registration);

        registration.Device("tray", (cfg, name, vars, drivers) =>
        {
            var parameters = TrayDeviceParameters.ParseConfig(cfg.Parameters);
            IDriver? driver = null;
            if (!string.IsNullOrWhiteSpace(cfg.DriverId)
                && drivers.TryGetValue(cfg.DriverId, out var mapped))
            {
                driver = mapped;
            }

            return new TrayDevice(cfg.Id, name, parameters, driver ?? new TrayLogicalDriver(), vars);
        });

        registration.Action(
            device => device is TrayDevice,
            (device, action, parameters) => ExecuteTrayAction((TrayDevice)device, action, parameters));

        registration.Task("pnp", CreatePnpCycle);
        registration.Task("pnpcycle", CreatePnpCycle);
        registration.Task("pnpconveyor", CreatePnpConveyor);
        registration.Task("conveyor", CreatePnpConveyor);

        registration.MonitoringModule(runtime => new PnpApiModule(runtime));
        registration.StaticPage("/indexPnp.html", () => PnpViewPages.IndexHtml);
        registration.StaticPage("/monitorPnp.html", () => PnpViewPages.MonitorHtml);
        PnpLogStore.Info("bootstrap", "PNP machine components registered");
    }

    private static MTaskBase? CreatePnpCycle(TaskBootstrapContext ctx, MdkSetting.TaskConfig config, string taskTypeKey)
    {
        if (!ctx.Drivers.TryGetValue(config.DriverId, out var driver))
        {
            driver = ctx.Drivers.Values.FirstOrDefault();
            if (driver is null)
            {
                return null;
            }
        }

        var taskName = string.IsNullOrWhiteSpace(config.Name) ? taskTypeKey : config.Name;
        return new PnpCycleTask(taskName, config.IntervalMs, driver, ctx.Vars, ctx.Devices, config.Parameters);
    }

    private static MTaskBase? CreatePnpConveyor(TaskBootstrapContext ctx, MdkSetting.TaskConfig config, string taskTypeKey)
    {
        if (!ctx.Drivers.TryGetValue(config.DriverId, out var driver))
        {
            driver = ctx.Drivers.Values.FirstOrDefault();
            if (driver is null)
            {
                return null;
            }
        }

        var taskName = string.IsNullOrWhiteSpace(config.Name) ? taskTypeKey : config.Name;
        return new PnpConveyorTask(taskName, config.IntervalMs, driver, ctx.Vars, ctx.Devices, config.Parameters);
    }

    private static DeviceActionResult ExecuteTrayAction(
        TrayDevice tray,
        string action,
        Dictionary<string, JsonElement>? parameters)
    {
        return action.ToLowerInvariant() switch
        {
            "status" => DeviceActionResult.Ok(new
            {
                tray.Id,
                tray.Rows,
                tray.Cols,
                tray.Capacity,
                currentIndex = tray.CurrentIndex,
                exhausted = tray.IsExhausted,
                role = tray.Parameters.Role
            }),
            "reset" => ResetTray(tray, parameters),
            "advance" => tray.Advance()
                ? DeviceActionResult.Ok(new { currentIndex = tray.CurrentIndex })
                : DeviceActionResult.Fail("exhausted"),
            "change" => ChangeTray(tray),
            _ => DeviceActionResult.Fail("unknown_action")
        };
    }

    private static DeviceActionResult ResetTray(TrayDevice tray, Dictionary<string, JsonElement>? parameters)
    {
        var start = 0;
        if (parameters is not null
            && parameters.TryGetValue("startIndex", out var el)
            && el.TryGetInt32(out var parsed))
        {
            start = parsed;
        }

        tray.Reset(start);
        return DeviceActionResult.Ok(new { currentIndex = tray.CurrentIndex });
    }

    private static DeviceActionResult ChangeTray(TrayDevice tray)
    {
        tray.MarkTrayChanged();
        return DeviceActionResult.Ok(new { currentIndex = tray.CurrentIndex });
    }
}

/// <summary>Compatibility bootstrap; prefer auto-discovery via <see cref="MdkExtensionHost.DiscoverAndRegister"/>.</summary>
public static class PnpBootstrap
{
    public static void Register() => MdkExtensionHost.Register(new PnpExtension());
}
