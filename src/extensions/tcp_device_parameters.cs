namespace MDKOSS.Core;

/// <summary>TCP device configuration parameters from settings.</summary>
public static class TcpDeviceParameterSet
{
    private const string DefaultHost = "127.0.0.1";
    private const int DefaultPort = 5000;
    private const int DefaultConnectTimeout = 5000;
    private const int DefaultReadTimeout = 5000;
    private const int DefaultWriteTimeout = 5000;
    private const bool DefaultNoDelay = false;
    private const bool DefaultKeepAlive = true;

    /// <summary>Parses TCP connection configuration from device parameters.</summary>
    public static TcpPortConfig ParseConfig(Dictionary<string, string> parameters)
    {
        var config = new TcpPortConfig
        {
            Host = GetStringParameter(parameters, "host", DefaultHost),
            Port = GetIntParameter(parameters, "port", DefaultPort),
            ConnectTimeout = GetIntParameter(parameters, "connectTimeout", DefaultConnectTimeout),
            ReadTimeout = GetIntParameter(parameters, "readTimeout", DefaultReadTimeout),
            WriteTimeout = GetIntParameter(parameters, "writeTimeout", DefaultWriteTimeout),
            NoDelay = GetBoolParameter(parameters, "noDelay", DefaultNoDelay),
            KeepAlive = GetBoolParameter(parameters, "keepAlive", DefaultKeepAlive)
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
}
