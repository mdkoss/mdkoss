using System.Globalization;

namespace MDKOSS.Pnp;

/// <summary>Parsed parameters for <see cref="TrayDevice"/>.</summary>
public sealed class TrayDeviceParameters
{
    public int Rows { get; init; } = 4;

    public int Cols { get; init; } = 6;

    public double OriginX { get; init; }

    public double OriginY { get; init; }

    public double PitchX { get; init; } = 20;

    public double PitchY { get; init; } = 20;

    public double PickZ { get; init; } = -10;

    public double SafeZ { get; init; } = 0;

    public int StartIndex { get; init; }

    public string Role { get; init; } = "source";

    public static TrayDeviceParameters ParseConfig(IReadOnlyDictionary<string, string>? parameters)
    {
        parameters ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        return new TrayDeviceParameters
        {
            Rows = ReadInt(parameters, "rows", 4),
            Cols = ReadInt(parameters, "cols", 6),
            OriginX = ReadDouble(parameters, "originX", 0),
            OriginY = ReadDouble(parameters, "originY", 0),
            PitchX = ReadDouble(parameters, "pitchX", 20),
            PitchY = ReadDouble(parameters, "pitchY", 20),
            PickZ = ReadDouble(parameters, "pickZ", -10),
            SafeZ = ReadDouble(parameters, "safeZ", 0),
            StartIndex = ReadInt(parameters, "startIndex", 0),
            Role = ReadString(parameters, "role", "source"),
        };
    }

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

    private static double ReadDouble(IReadOnlyDictionary<string, string> parameters, string key, double fallback)
    {
        if (!parameters.TryGetValue(key, out var raw) || string.IsNullOrWhiteSpace(raw))
        {
            return fallback;
        }

        return double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            ? value
            : fallback;
    }
}
