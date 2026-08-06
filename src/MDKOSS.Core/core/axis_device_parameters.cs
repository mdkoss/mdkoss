namespace MDKOSS.Core;

/// <summary>
/// Axis device <c>parameters</c> keys (Servo-style motion channel), aligned with common card configs
/// such as <c>Servo_2L_1O</c> (2 limit inputs + 1 home/enable style).
/// </summary>
public static class AxisDeviceParameterSet
{
    public const string KeyAxis = "axis";
    public const string KeyModel = "model";
    public const string KeyHomeVel = "homeVel";
    public const string KeyPulsePerUnit = "pulsePerUnit";
    public const string KeyMaxVel = "maxVel";
    public const string KeyAccel = "accel";
    public const string KeyNegLimit = "negLimit";
    public const string KeyPosLimit = "posLimit";
    public const string KeyHomeSensor = "homeSensor";
    public const string KeyNote = "note";

    /// <summary>Default parameter template for a new <c>axis</c> device (matches Servo_2L_1O style numbers).</summary>
    public static Dictionary<string, string> DefaultParameters(short axisNo = 0) => new(StringComparer.OrdinalIgnoreCase)
    {
        [KeyAxis] = axisNo.ToString(),
        [KeyModel] = "Servo_2L_1O",
        [KeyHomeVel] = "10.00",
        [KeyPulsePerUnit] = "10000",
        [KeyMaxVel] = "150.00",
        [KeyAccel] = "2000.00",
        [KeyNegLimit] = "1",
        [KeyPosLimit] = "1",
        [KeyHomeSensor] = "0",
        [KeyNote] = "",
    };

    /// <summary>Reads the motion-card axis channel index (<c>axis</c> / <c>axisNo</c> / …).</summary>
    public static short ParseAxisIndex(IReadOnlyDictionary<string, string>? parameters, short fallback = 0)
    {
        if (parameters is null || parameters.Count == 0)
        {
            return fallback;
        }

        foreach (var key in new[] { KeyAxis, "axisNo", "axisIndex", "axisId" })
        {
            if (!parameters.TryGetValue(key, out var raw) || string.IsNullOrWhiteSpace(raw))
            {
                continue;
            }

            if (short.TryParse(raw.Trim(), out var v) && v >= 0)
            {
                return v;
            }
        }

        return fallback;
    }

    public static string GetModel(IReadOnlyDictionary<string, string>? parameters) =>
        parameters is not null
        && parameters.TryGetValue(KeyModel, out var m)
        && !string.IsNullOrWhiteSpace(m)
            ? m.Trim()
            : "Servo_2L_1O";
}
