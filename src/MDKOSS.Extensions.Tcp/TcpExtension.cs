using System.Text.Json;
using MDKOSS.Core;
using MDKOSS.Core.Monitor;
using MDKOSS.Extensions;

namespace MDKOSS.Extensions.Tcp;

/// <summary>TCP device extension package (config type <c>tcpdev</c>).</summary>
public sealed class TcpExtension : IMdkExtension
{
    public string Id => "tcp";

    public string DisplayName => "TCP device";

    public void Register(IExtensionRegistration registration)
    {
        ArgumentNullException.ThrowIfNull(registration);

        registration.Device("tcpdev", (cfg, name, vars, _) =>
        {
            var tcpConfig = TcpDeviceParameterSet.ParseConfig(cfg.Parameters);
            return new TcpDevice(cfg.Id, name, tcpConfig, vars);
        });

        registration.Action(
            device => device is TcpDevice,
            (device, action, parameters) =>
                TcpDeviceActions.Execute((TcpDevice)device, action, parameters));

        registration.MonitoringModule(runtime => new TcpApiModule(runtime));
    }
}

/// <summary>Call once before creating <see cref="MdkRuntime"/>.</summary>
public static class TcpExtensionBootstrap
{
    public static void Register() => MdkExtensionHost.Register(new TcpExtension());
}

/// <summary>Unified action handlers for <see cref="TcpDevice"/>.</summary>
internal static class TcpDeviceActions
{
    internal static DeviceActionResult Execute(
        TcpDevice tcp,
        string action,
        Dictionary<string, JsonElement>? parameters)
    {
        return action.ToLowerInvariant() switch
        {
            "connect" => tcp.Connect() == TcpErrorCode.Ok ? DeviceActionResult.Ok() : DeviceActionResult.Fail("connect_failed"),
            "disconnect" => tcp.Disconnect() == TcpErrorCode.Ok ? DeviceActionResult.Ok() : DeviceActionResult.Fail("disconnect_failed"),
            "write" when parameters != null && parameters.TryGetValue("data", out var data) =>
                tcp.Write(data.GetString() ?? "") == TcpErrorCode.Ok ? DeviceActionResult.Ok() : DeviceActionResult.Fail("write_failed"),
            "read" => HandleRead(tcp),
            "status" => DeviceActionResult.Ok(new { isConnected = tcp.IsConnected, bytesToRead = tcp.BytesToRead }),
            _ => DeviceActionResult.Fail("unknown_action")
        };
    }

    private static DeviceActionResult HandleRead(TcpDevice tcp)
    {
        var (err, data) = tcp.ReadAll();
        return err == TcpErrorCode.Ok && data != null
            ? DeviceActionResult.Ok(new { data })
            : DeviceActionResult.Fail("read_failed");
    }
}
