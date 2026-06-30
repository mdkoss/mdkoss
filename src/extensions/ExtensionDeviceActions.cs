using System.Text.Json;

namespace MDKOSS.Core;

/// <summary>Device action handlers for extension device types.</summary>
internal static class ExtensionDeviceActions
{
    internal static DeviceActionResult ExecuteSerial(
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
            "read" => HandleSerialRead(serial),
            "status" => DeviceActionResult.Ok(new { isOpen = serial.IsOpen, bytesToRead = serial.BytesToRead }),
            _ => DeviceActionResult.Fail("unknown_action")
        };
    }

    internal static DeviceActionResult ExecuteTcp(
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
            "read" => HandleTcpRead(tcp),
            "status" => DeviceActionResult.Ok(new { isConnected = tcp.IsConnected, bytesToRead = tcp.BytesToRead }),
            _ => DeviceActionResult.Fail("unknown_action")
        };
    }

    private static DeviceActionResult HandleSerialRead(SerialDevice serial)
    {
        var (err, data) = serial.ReadAll();
        if (err == SerialErrorCode.Ok && data != null)
        {
            return DeviceActionResult.Ok(new { data });
        }

        return DeviceActionResult.Fail("read_failed");
    }

    private static DeviceActionResult HandleTcpRead(TcpDevice tcp)
    {
        var (err, data) = tcp.ReadAll();
        if (err == TcpErrorCode.Ok && data != null)
        {
            return DeviceActionResult.Ok(new { data });
        }

        return DeviceActionResult.Fail("read_failed");
    }
}
