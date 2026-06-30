namespace MDKOSS.Core;

/// <summary>Serial device configuration parameters from settings.</summary>
public static class SerialDeviceParameterSet
{
    private const string DefaultPortName = "COM1";
    private const int DefaultBaudRate = 9600;
    private const int DefaultDataBits = 8;
    private const SerialParity DefaultParity = SerialParity.None;
    private const SerialStopBits DefaultStopBits = SerialStopBits.One;
    private const int DefaultReadTimeout = 5000;
    private const int DefaultWriteTimeout = 5000;

    /// <summary>Parses serial port configuration from device parameters.</summary>
    public static SerialPortConfig ParseConfig(Dictionary<string, string> parameters)
    {
        var config = new SerialPortConfig
        {
            PortName = GetStringParameter(parameters, "portName", DefaultPortName),
            BaudRate = GetIntParameter(parameters, "baudRate", DefaultBaudRate),
            DataBits = GetIntParameter(parameters, "dataBits", DefaultDataBits),
            Parity = GetEnumParameter(parameters, "parity", DefaultParity),
            StopBits = GetEnumParameter(parameters, "stopBits", DefaultStopBits),
            ReadTimeout = GetIntParameter(parameters, "readTimeout", DefaultReadTimeout),
            WriteTimeout = GetIntParameter(parameters, "writeTimeout", DefaultWriteTimeout),
            DtrEnable = GetBoolParameter(parameters, "dtrEnable", false),
            RtsEnable = GetBoolParameter(parameters, "rtsEnable", false)
        };

        return config;
    }

    private static string GetStringParameter(Dictionary<string, string> parameters, string key, string defaultValue)
    {
        if (parameters.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
        {
            return value.Trim();
        }
        return defaultValue;
    }

    private static int GetIntParameter(Dictionary<string, string> parameters, string key, int defaultValue)
    {
        if (parameters.TryGetValue(key, out var value) && int.TryParse(value, out var result))
        {
            return result;
        }
        return defaultValue;
    }

    private static bool GetBoolParameter(Dictionary<string, string> parameters, string key, bool defaultValue)
    {
        if (parameters.TryGetValue(key, out var value) && bool.TryParse(value, out var result))
        {
            return result;
        }
        return defaultValue;
    }

    private static T GetEnumParameter<T>(Dictionary<string, string> parameters, string key, T defaultValue)
        where T : struct, Enum
    {
        if (parameters.TryGetValue(key, out var value) && Enum.TryParse<T>(value, true, out var result))
        {
            return result;
        }
        return defaultValue;
    }
}
