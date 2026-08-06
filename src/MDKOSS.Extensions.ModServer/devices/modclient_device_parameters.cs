using System.Globalization;

namespace MDKOSS.Extensions.ModServer;

/// <summary>Parsed parameters for <see cref="ModClientDevice"/> (config type <c>devmodclient</c>).</summary>
public sealed class ModClientDeviceParameters
{
    public string Host { get; init; } = "127.0.0.1";

    public int Port { get; init; } = 502;

    public byte UnitId { get; init; } = 1;

    public int ConnectTimeoutMs { get; init; } = 3000;

    public int ReadTimeoutMs { get; init; } = 3000;

    public int WriteTimeoutMs { get; init; } = 3000;

    /// <summary>When true, connect in <see cref="ModClientDevice.Start"/>.</summary>
    public bool AutoConnect { get; init; } = true;

    public static Dictionary<string, string> DefaultParameters() => new(StringComparer.OrdinalIgnoreCase)
    {
        ["host"] = "127.0.0.1",
        ["port"] = "1502",
        ["unitId"] = "1",
        ["connectTimeoutMs"] = "3000",
        ["readTimeoutMs"] = "3000",
        ["writeTimeoutMs"] = "3000",
        ["autoConnect"] = "true",
    };

    public static ModClientDeviceParameters ParseConfig(IReadOnlyDictionary<string, string>? parameters)
    {
        parameters ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        return new ModClientDeviceParameters
        {
            Host = ReadString(parameters, "host", "127.0.0.1"),
            Port = ClampPort(ReadInt(parameters, "port", 502)),
            UnitId = (byte)Math.Clamp(ReadInt(parameters, "unitId", 1), 0, 255),
            ConnectTimeoutMs = Math.Max(100, ReadInt(parameters, "connectTimeoutMs", 3000)),
            ReadTimeoutMs = Math.Max(100, ReadInt(parameters, "readTimeoutMs", 3000)),
            WriteTimeoutMs = Math.Max(100, ReadInt(parameters, "writeTimeoutMs", 3000)),
            AutoConnect = ReadBool(parameters, "autoConnect", true),
        };
    }

    private static int ClampPort(int port) => Math.Clamp(port, 1, 65535);

    private static string ReadString(IReadOnlyDictionary<string, string> parameters, string key, string fallback)
    {
        return parameters.TryGetValue(key, out var raw) && !string.IsNullOrWhiteSpace(raw)
            ? raw.Trim()
            : fallback;
    }

    private static int ReadInt(IReadOnlyDictionary<string, string> parameters, string key, int fallback)
    {
        if (!parameters.TryGetValue(key, out var raw) || string.IsNullOrWhiteSpace(raw))
        {
            return fallback;
        }

        return int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : fallback;
    }

    private static bool ReadBool(IReadOnlyDictionary<string, string> parameters, string key, bool fallback)
    {
        if (!parameters.TryGetValue(key, out var raw) || string.IsNullOrWhiteSpace(raw))
        {
            return fallback;
        }

        return bool.TryParse(raw, out var value) ? value : fallback;
    }
}
