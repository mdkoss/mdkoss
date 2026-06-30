namespace MDKOSS.Core;

/// <summary>Serial/TCP device runtime API (implemented in MDKOSS.Extensions).</summary>
public static class ExtensionDeviceApi
{
    /// <summary>Gets serial device status for monitoring.</summary>
    public static object? GetSerialStatus(MdkRuntime runtime, string deviceId)
    {
        if (!runtime.TryGetDevice(deviceId, out var dev) || dev is not SerialDevice serial)
        {
            return null;
        }

        return new
        {
            isOpen = serial.IsOpen,
            portName = serial.Config.PortName,
            baudRate = serial.Config.BaudRate,
            dataBits = serial.Config.DataBits,
            parity = serial.Config.Parity.ToString(),
            stopBits = serial.Config.StopBits.ToString(),
            bytesToRead = serial.BytesToRead
        };
    }

    /// <summary>Opens a serial port.</summary>
    public static SerialErrorCode OpenSerialPort(MdkRuntime runtime, string deviceId, SerialPortConfig config)
    {
        if (!runtime.TryGetDevice(deviceId, out var dev) || dev is not SerialDevice serial)
        {
            return SerialErrorCode.PortNotFound;
        }

        var originalConfig = serial.Config;
        serial.SetParameters(config);
        var result = serial.Open();

        if (result != SerialErrorCode.Ok)
        {
            serial.SetParameters(originalConfig);
        }

        return result;
    }

    /// <summary>Closes a serial port.</summary>
    public static SerialErrorCode CloseSerialPort(MdkRuntime runtime, string deviceId)
    {
        if (!runtime.TryGetDevice(deviceId, out var dev) || dev is not SerialDevice serial)
        {
            return SerialErrorCode.PortNotFound;
        }

        return serial.Close();
    }

    /// <summary>Updates serial port configuration.</summary>
    public static SerialErrorCode SetSerialConfig(MdkRuntime runtime, string deviceId, SerialPortConfig config)
    {
        if (!runtime.TryGetDevice(deviceId, out var dev) || dev is not SerialDevice serial)
        {
            return SerialErrorCode.PortNotFound;
        }

        return serial.SetParameters(config);
    }

    /// <summary>Writes text data to serial port.</summary>
    public static SerialErrorCode WriteSerialText(MdkRuntime runtime, string deviceId, string data)
    {
        if (!runtime.TryGetDevice(deviceId, out var dev) || dev is not SerialDevice serial)
        {
            return SerialErrorCode.PortNotFound;
        }

        return serial.Write(data);
    }

    /// <summary>Writes binary data to serial port.</summary>
    public static SerialErrorCode WriteSerialBinary(MdkRuntime runtime, string deviceId, byte[] data)
    {
        if (!runtime.TryGetDevice(deviceId, out var dev) || dev is not SerialDevice serial)
        {
            return SerialErrorCode.PortNotFound;
        }

        return serial.WriteBinary(data);
    }

    /// <summary>Reads all available data from serial port.</summary>
    public static (SerialErrorCode error, string? data) ReadSerialAll(MdkRuntime runtime, string deviceId)
    {
        if (!runtime.TryGetDevice(deviceId, out var dev) || dev is not SerialDevice serial)
        {
            return (SerialErrorCode.PortNotFound, null);
        }

        return serial.ReadAll();
    }

    /// <summary>Discards serial port buffers.</summary>
    public static SerialErrorCode DiscardSerialBuffers(MdkRuntime runtime, string deviceId)
    {
        if (!runtime.TryGetDevice(deviceId, out var dev) || dev is not SerialDevice serial)
        {
            return SerialErrorCode.PortNotFound;
        }

        return serial.DiscardBuffers();
    }

    /// <summary>Gets TCP device status for monitoring.</summary>
    public static object? GetTcpStatus(MdkRuntime runtime, string deviceId)
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

    /// <summary>Opens a TCP connection.</summary>
    public static TcpErrorCode OpenTcpConnection(MdkRuntime runtime, string deviceId, TcpPortConfig config)
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

    /// <summary>Closes a TCP connection.</summary>
    public static TcpErrorCode CloseTcpConnection(MdkRuntime runtime, string deviceId)
    {
        if (!runtime.TryGetDevice(deviceId, out var dev) || dev is not TcpDevice tcp)
        {
            return TcpErrorCode.NotConnected;
        }

        return tcp.Disconnect();
    }

    /// <summary>Updates TCP connection configuration.</summary>
    public static TcpErrorCode SetTcpConfig(MdkRuntime runtime, string deviceId, TcpPortConfig config)
    {
        if (!runtime.TryGetDevice(deviceId, out var dev) || dev is not TcpDevice tcp)
        {
            return TcpErrorCode.NotConnected;
        }

        return tcp.SetParameters(config);
    }

    /// <summary>Writes text data to TCP connection.</summary>
    public static TcpErrorCode WriteTcpText(MdkRuntime runtime, string deviceId, string data)
    {
        if (!runtime.TryGetDevice(deviceId, out var dev) || dev is not TcpDevice tcp)
        {
            return TcpErrorCode.NotConnected;
        }

        return tcp.Write(data);
    }

    /// <summary>Writes binary data to TCP connection.</summary>
    public static TcpErrorCode WriteTcpBinary(MdkRuntime runtime, string deviceId, byte[] data)
    {
        if (!runtime.TryGetDevice(deviceId, out var dev) || dev is not TcpDevice tcp)
        {
            return TcpErrorCode.NotConnected;
        }

        return tcp.WriteBinary(data);
    }

    /// <summary>Reads all available data from TCP connection.</summary>
    public static (TcpErrorCode error, string? data) ReadTcpAll(MdkRuntime runtime, string deviceId)
    {
        if (!runtime.TryGetDevice(deviceId, out var dev) || dev is not TcpDevice tcp)
        {
            return (TcpErrorCode.NotConnected, null);
        }

        return tcp.ReadAll();
    }

    /// <summary>Discards TCP connection buffers.</summary>
    public static TcpErrorCode DiscardTcpBuffers(MdkRuntime runtime, string deviceId)
    {
        if (!runtime.TryGetDevice(deviceId, out var dev) || dev is not TcpDevice tcp)
        {
            return TcpErrorCode.NotConnected;
        }

        return tcp.DiscardBuffers();
    }
}
