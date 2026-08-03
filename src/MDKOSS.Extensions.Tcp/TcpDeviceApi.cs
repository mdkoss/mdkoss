namespace MDKOSS.Core;

/// <summary>TCP device runtime API for monitoring modules.</summary>
public static class TcpDeviceApi
{
    /// <summary>Gets TCP device status for monitoring.</summary>
    public static object? GetStatus(MdkRuntime runtime, string deviceId)
    {
        if (!runtime.TryGetDevice(deviceId, out var dev) || dev is not TcpDevice tcp)
        {
            return null;
        }

        return new
        {
            isConnected = tcp.IsConnected,
            host = tcp.Config.Host,
            port = tcp.Config.Port,
            bytesToRead = tcp.BytesToRead
        };
    }

    public static TcpErrorCode OpenConnection(MdkRuntime runtime, string deviceId, TcpPortConfig config)
    {
        if (!runtime.TryGetDevice(deviceId, out var dev) || dev is not TcpDevice tcp)
        {
            return TcpErrorCode.ConnectionRefused;
        }

        var originalConfig = tcp.Config;
        tcp.SetParameters(config);
        var result = tcp.Connect();

        if (result != TcpErrorCode.Ok)
        {
            tcp.SetParameters(originalConfig);
        }

        return result;
    }

    public static TcpErrorCode CloseConnection(MdkRuntime runtime, string deviceId)
    {
        if (!runtime.TryGetDevice(deviceId, out var dev) || dev is not TcpDevice tcp)
        {
            return TcpErrorCode.NotConnected;
        }

        return tcp.Disconnect();
    }

    public static TcpErrorCode SetConfig(MdkRuntime runtime, string deviceId, TcpPortConfig config)
    {
        if (!runtime.TryGetDevice(deviceId, out var dev) || dev is not TcpDevice tcp)
        {
            return TcpErrorCode.NotConnected;
        }

        return tcp.SetParameters(config);
    }

    public static TcpErrorCode WriteText(MdkRuntime runtime, string deviceId, string data)
    {
        if (!runtime.TryGetDevice(deviceId, out var dev) || dev is not TcpDevice tcp)
        {
            return TcpErrorCode.NotConnected;
        }

        return tcp.Write(data);
    }

    public static TcpErrorCode WriteBinary(MdkRuntime runtime, string deviceId, byte[] data)
    {
        if (!runtime.TryGetDevice(deviceId, out var dev) || dev is not TcpDevice tcp)
        {
            return TcpErrorCode.NotConnected;
        }

        return tcp.WriteBinary(data);
    }

    public static (TcpErrorCode error, string? data) ReadAll(MdkRuntime runtime, string deviceId)
    {
        if (!runtime.TryGetDevice(deviceId, out var dev) || dev is not TcpDevice tcp)
        {
            return (TcpErrorCode.NotConnected, null);
        }

        return tcp.ReadAll();
    }

    public static TcpErrorCode DiscardBuffers(MdkRuntime runtime, string deviceId)
    {
        if (!runtime.TryGetDevice(deviceId, out var dev) || dev is not TcpDevice tcp)
        {
            return TcpErrorCode.NotConnected;
        }

        return tcp.DiscardBuffers();
    }
}
