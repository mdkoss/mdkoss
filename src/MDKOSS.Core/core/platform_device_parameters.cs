namespace MDKOSS.Core;

/// <summary>Parses multi-axis platform layout from <see cref="MdkSetting.DeviceConfig.Parameters"/> and device <c>type</c>.</summary>
public static class PlatformDeviceParameterSet
{
    /// <summary>Maps shorthand device types (<c>xy</c>, <c>xyz</c>, …) to a fixed <see cref="MPlatformKind"/>.</summary>
    public static bool TryKindFromDeviceType(string deviceTypeLower, out MPlatformKind kind)
    {
        switch (deviceTypeLower)
        {
            case "xy":
                kind = MPlatformKind.Xy;
                return true;
            case "xyz":
                kind = MPlatformKind.Xyz;
                return true;
            case "xyzu":
                kind = MPlatformKind.XyzU;
                return true;
            case "xyzuv":
                kind = MPlatformKind.XyzUv;
                return true;
            case "xyzuvw":
                kind = MPlatformKind.XyzUvw;
                return true;
            default:
                kind = default;
                return false;
        }
    }

    /// <summary>True for <c>platform</c> or axis-count aliases (<c>xy</c>, <c>xyz</c>, …).</summary>
    public static bool IsPlatformFamilyType(string deviceTypeLower) =>
        string.Equals(deviceTypeLower, "platform", StringComparison.OrdinalIgnoreCase)
        || TryKindFromDeviceType(deviceTypeLower, out _);

    /// <summary>
    /// Reads <c>kind</c> from parameters (e.g. <c>xy</c>, <c>xyz</c>). When unset, returns <paramref name="defaultKind"/> (typically from type alias).
    /// </summary>
    public static MPlatformKind ParseKindOrDefault(
        IReadOnlyDictionary<string, string> parameters,
        MPlatformKind? defaultKind)
    {
        if (defaultKind.HasValue)
        {
            return defaultKind.Value;
        }

        if (!parameters.TryGetValue("kind", out var raw) || string.IsNullOrWhiteSpace(raw))
        {
            return MPlatformKind.Xyz;
        }

        if (TryParseKindToken(raw.Trim(), out var k))
        {
            return k;
        }

        throw new MdkException(
            MdkErrorCode.PlatformConfigurationInvalid,
            $"Unknown platform kind '{raw}'. Use xy, xyz, xyzu, xyzuv, or xyzuvw.");
    }

    /// <summary>Resolves per-axis driver id: <c>axis.X</c>, <c>axis.Y</c>, …; falls back to <paramref name="defaultDriverId"/> when unset.</summary>
    public static string ResolveAxisDriverId(
        IReadOnlyDictionary<string, string> parameters,
        string axisLetter,
        string defaultDriverId)
    {
        var letter = axisLetter.Trim();
        if (parameters.TryGetValue($"axis.{letter}", out var v) && !string.IsNullOrWhiteSpace(v))
        {
            return v.Trim();
        }

        if (parameters.TryGetValue($"axis.{letter.ToLowerInvariant()}", out v) && !string.IsNullOrWhiteSpace(v))
        {
            return v.Trim();
        }

        if (!string.IsNullOrWhiteSpace(defaultDriverId))
        {
            return defaultDriverId.Trim();
        }

        throw new MdkException(
            MdkErrorCode.PlatformConfigurationInvalid,
            $"Platform axis '{letter}' has no driver: set device driverId or parameter axis.{letter}.");
    }

    private static bool TryParseKindToken(string token, out MPlatformKind kind)
    {
        var t = token.ToLowerInvariant();
        return TryKindFromDeviceType(t, out kind);
    }
}
