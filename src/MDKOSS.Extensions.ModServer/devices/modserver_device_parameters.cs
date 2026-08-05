using System.Globalization;

namespace MDKOSS.Extensions.ModServer;

/// <summary>Parsed parameters for <see cref="ModServerDevice"/> (config type <c>devmodserver</c>).</summary>
public sealed class ModServerDeviceParameters
{
    public string BindAddress { get; init; } = "0.0.0.0";

    public int Port { get; init; } = 502;

    public byte UnitId { get; init; } = 1;

    /// <summary>When true, start listening in <see cref="ModServerDevice.Start"/>.</summary>
    public bool AutoStart { get; init; } = true;

    public static ModServerDeviceParameters ParseConfig(IReadOnlyDictionary<string, string>? parameters)
    {
        parameters ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        return new ModServerDeviceParameters
        {
            BindAddress = ReadString(parameters, "bindAddress", "0.0.0.0"),
            Port = ClampPort(ReadInt(parameters, "port", 502)),
            UnitId = (byte)Math.Clamp(ReadInt(parameters, "unitId", 1), 0, 255),
            AutoStart = ReadBool(parameters, "autoStart", true),
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
