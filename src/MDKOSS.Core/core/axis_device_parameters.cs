namespace MDKOSS.Core;

/// <summary>Motion axis geometry: linear (mm) or rotary (deg).</summary>
public enum MAxisKind
{
    /// <summary>直线轴（单位通常为 mm）。</summary>
    Linear,
    /// <summary>旋转轴（单位通常为 deg）。</summary>
    Rotary,
}

/// <summary>Axis kind helpers.</summary>
public static class MAxisKindExtensions
{
    public static string ToConfigToken(this MAxisKind kind) => kind switch
    {
        MAxisKind.Rotary => "rotary",
        _ => "linear",
    };
}

/// <summary>
/// Axis device <c>parameters</c> keys. Type may be <c>axis</c> / <c>linear</c> / <c>rotary</c>
/// (aliases mirror platform <c>xy</c>/<c>xyz</c> shorthand); <c>parameters.kind</c> stores the geometry.
/// </summary>
public static class AxisDeviceParameterSet
{
    public const string KeyKind = "kind";
    public const string KeyAxis = "axis";
    public const string KeyModel = "model";
    public const string KeyHomeVel = "homeVel";
    public const string KeyPulsePerUnit = "pulsePerUnit";
    public const string KeyMaxVel = "maxVel";
    public const string KeyAccel = "accel";
    public const string KeyNegLimit = "negLimit";
    public const string KeyPosLimit = "posLimit";
    public const string KeyHomeSensor = "homeSensor";
    public const string KeySoftNeg = "softNeg";
    public const string KeySoftPos = "softPos";
    public const string KeyUnit = "unit";
    public const string KeyContinuous = "continuous";
    public const string KeyNote = "note";

    /// <summary>True for <c>axis</c> or geometry aliases (<c>linear</c> / <c>rotary</c>).</summary>
    public static bool IsAxisFamilyType(string? deviceType) =>
        TryKindFromDeviceType(deviceType, out _)
        || string.Equals(deviceType?.Trim(), "axis", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Maps shorthand device types (<c>linear</c>, <c>rotary</c>, and Chinese aliases) to <see cref="MAxisKind"/>.
    /// <c>axis</c> alone is not a kind alias (legacy; kind comes from parameters).
    /// </summary>
    public static bool TryKindFromDeviceType(string? deviceType, out MAxisKind kind)
    {
        switch ((deviceType ?? "").Trim().ToLowerInvariant())
        {
            case "linear":
            case "lin":
            case "直线":
            case "直线轴":
                kind = MAxisKind.Linear;
                return true;
            case "rotary":
            case "rot":
            case "rotate":
            case "旋转":
            case "旋转轴":
                kind = MAxisKind.Rotary;
                return true;
            default:
                kind = default;
                return false;
        }
    }

    /// <summary>
    /// Resolves kind: device-type alias first, then <c>parameters.kind</c>, else <see cref="MAxisKind.Linear"/>.
    /// </summary>
    public static MAxisKind ParseKindOrDefault(
        IReadOnlyDictionary<string, string>? parameters,
        string? deviceType = null)
    {
        if (TryKindFromDeviceType(deviceType, out var fromType))
        {
            return fromType;
        }

        if (parameters is not null
            && parameters.TryGetValue(KeyKind, out var raw)
            && !string.IsNullOrWhiteSpace(raw)
            && TryParseKindToken(raw.Trim(), out var fromParam))
        {
            return fromParam;
        }

        return MAxisKind.Linear;
    }

    /// <summary>Default parameter template for a new axis (geometry-specific init values).</summary>
    public static Dictionary<string, string> DefaultParameters(
        MAxisKind kind = MAxisKind.Linear,
        short axisNo = 0) =>
        kind switch
        {
            MAxisKind.Rotary => RotaryDefaults(axisNo),
            _ => LinearDefaults(axisNo),
        };

    /// <summary>Default parameters from a type/kind token (<c>linear</c>/<c>rotary</c>/<c>axis</c>).</summary>
    public static Dictionary<string, string> DefaultParameters(string? typeOrKind, short axisNo = 0)
    {
        var kind = TryKindFromDeviceType(typeOrKind, out var k)
            ? k
            : (TryParseKindToken(typeOrKind, out var k2) ? k2 : MAxisKind.Linear);
        return DefaultParameters(kind, axisNo);
    }

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

    public static string GetKindToken(IReadOnlyDictionary<string, string>? parameters, string? deviceType = null) =>
        ParseKindOrDefault(parameters, deviceType).ToConfigToken();

    /// <summary>
    /// Ensures <c>kind</c> matches the device type alias (when type is linear/rotary).
    /// Does not remove other keys.
    /// </summary>
    public static void SyncKindParameter(Dictionary<string, string>? parameters, string? deviceType)
    {
        if (parameters is null)
        {
            return;
        }

        var kind = ParseKindOrDefault(parameters, deviceType);
        parameters[KeyKind] = kind.ToConfigToken();
    }

    private static bool TryParseKindToken(string? token, out MAxisKind kind) =>
        TryKindFromDeviceType(token, out kind);

    private static Dictionary<string, string> LinearDefaults(short axisNo) => new(StringComparer.OrdinalIgnoreCase)
    {
        [KeyKind] = "linear",
        [KeyAxis] = axisNo.ToString(),
        [KeyModel] = "Servo_2L_1O",
        [KeyHomeVel] = "10.00",
        [KeyPulsePerUnit] = "10000",
        [KeyMaxVel] = "150.00",
        [KeyAccel] = "2000.00",
        [KeyNegLimit] = "1",
        [KeyPosLimit] = "1",
        [KeyHomeSensor] = "0",
        [KeySoftNeg] = "0",
        [KeySoftPos] = "300",
        [KeyUnit] = "mm",
        [KeyNote] = "",
    };

    private static Dictionary<string, string> RotaryDefaults(short axisNo) => new(StringComparer.OrdinalIgnoreCase)
    {
        [KeyKind] = "rotary",
        [KeyAxis] = axisNo.ToString(),
        [KeyModel] = "Servo_Rotary",
        [KeyHomeVel] = "5.00",
        [KeyPulsePerUnit] = "1000",
        [KeyMaxVel] = "360.00",
        [KeyAccel] = "1000.00",
        [KeyNegLimit] = "0",
        [KeyPosLimit] = "0",
        [KeyHomeSensor] = "0",
        [KeySoftNeg] = "-180",
        [KeySoftPos] = "180",
        [KeyUnit] = "deg",
        [KeyContinuous] = "false",
        [KeyNote] = "",
    };
}
