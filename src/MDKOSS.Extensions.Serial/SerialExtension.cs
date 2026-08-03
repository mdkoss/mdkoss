using System.Text.Json;
using MDKOSS.Core;
using MDKOSS.Core.Monitor;
using MDKOSS.Extensions;

namespace MDKOSS.Extensions.Serial;

/// <summary>Serial port extension package (config type <c>serialdev</c>).</summary>
public sealed class SerialExtension : IMdkExtension
{
    public string Id => "serial";

    public string DisplayName => "Serial device (RS-232C)";

    public void Register(IExtensionRegistration registration)
    {
        ArgumentNullException.ThrowIfNull(registration);

        registration.Device("serialdev", (cfg, name, vars, _) =>
        {
            var serialConfig = SerialDeviceParameterSet.ParseConfig(cfg.Parameters);
            return new SerialDevice(cfg.Id, name, serialConfig, vars);
        });

        registration.Action(
            device => device is SerialDevice,
            (device, action, parameters) =>
                SerialDeviceActions.Execute((SerialDevice)device, action, parameters));

        registration.MonitoringModule(runtime => new SerialApiModule(runtime));
    }
}

/// <summary>Call once before creating <see cref="MdkRuntime"/>.</summary>
public static class SerialExtensionBootstrap
{
    public static void Register() => MdkExtensionHost.Register(new SerialExtension());
}

/// <summary>Unified action handlers for <see cref="SerialDevice"/>.</summary>
internal static class SerialDeviceActions
{
    internal static DeviceActionResult Execute(
        SerialDevice serial,
        string action,
        Dictionary<string, JsonElement>? parameters)
    {
        return action.ToLowerInvariant() switch
        {
            "open" => serial.Open() == SerialErrorCode.Ok ? DeviceActionResult.Ok() : DeviceActionResult.Fail("open_failed"),
            "close" => serial.Close() == SerialErrorCode.Ok ? DeviceActionResult.Ok() : DeviceActionResult.Fail("close_failed"),
            "write" when parameters != null && parameters.TryGetValue("data", out var data) =>
                serial.Write(data.GetString() ?? "") == SerialErrorCode.Ok ? DeviceActionResult.Ok() : DeviceActionResult.Fail("write_failed"),
            "read" => HandleRead(serial),
            "status" => DeviceActionResult.Ok(new { isOpen = serial.IsOpen, bytesToRead = serial.BytesToRead }),
            _ => DeviceActionResult.Fail("unknown_action")
        };
    }

    private static DeviceActionResult HandleRead(SerialDevice serial)
    {
        var (err, data) = serial.ReadAll();
        return err == SerialErrorCode.Ok && data != null
            ? DeviceActionResult.Ok(new { data })
            : DeviceActionResult.Fail("read_failed");
    }
}
