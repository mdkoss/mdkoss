namespace MDKOSS.Core;

/// <summary>Serial device runtime API for monitoring modules.</summary>
public static class SerialDeviceApi
{
    /// <summary>Gets serial device status for monitoring.</summary>
    public static object? GetStatus(MdkRuntime runtime, string deviceId)
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

    public static SerialErrorCode OpenPort(MdkRuntime runtime, string deviceId, SerialPortConfig config)
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

    public static SerialErrorCode ClosePort(MdkRuntime runtime, string deviceId)
    {
        if (!runtime.TryGetDevice(deviceId, out var dev) || dev is not SerialDevice serial)
        {
            return SerialErrorCode.PortNotFound;
        }

        return serial.Close();
    }

    public static SerialErrorCode SetConfig(MdkRuntime runtime, string deviceId, SerialPortConfig config)
    {
        if (!runtime.TryGetDevice(deviceId, out var dev) || dev is not SerialDevice serial)
        {
            return SerialErrorCode.PortNotFound;
        }

        return serial.SetParameters(config);
    }

    public static SerialErrorCode WriteText(MdkRuntime runtime, string deviceId, string data)
    {
        if (!runtime.TryGetDevice(deviceId, out var dev) || dev is not SerialDevice serial)
        {
            return SerialErrorCode.PortNotFound;
        }

        return serial.Write(data);
    }

    public static SerialErrorCode WriteBinary(MdkRuntime runtime, string deviceId, byte[] data)
    {
        if (!runtime.TryGetDevice(deviceId, out var dev) || dev is not SerialDevice serial)
        {
            return SerialErrorCode.PortNotFound;
        }

        return serial.WriteBinary(data);
    }

    public static (SerialErrorCode error, string? data) ReadAll(MdkRuntime runtime, string deviceId)
    {
        if (!runtime.TryGetDevice(deviceId, out var dev) || dev is not SerialDevice serial)
        {
            return (SerialErrorCode.PortNotFound, null);
        }

        return serial.ReadAll();
    }

    public static SerialErrorCode DiscardBuffers(MdkRuntime runtime, string deviceId)
    {
        if (!runtime.TryGetDevice(deviceId, out var dev) || dev is not SerialDevice serial)
        {
            return SerialErrorCode.PortNotFound;
        }

        return serial.DiscardBuffers();
    }
}
