namespace MDKOSS.Core;

/// <summary>Parses multi-axis platform layout from <see cref="MdkSetting.DeviceConfig.Parameters"/> and device <c>type</c>.</summary>
public static class PlatformDeviceParameterSet
{
    /// <summary>Maps shorthand device types (<c>x</c>, <c>xy</c>, <c>xyz</c>, …) to a fixed <see cref="MPlatformKind"/>.</summary>
    public static bool TryKindFromDeviceType(string deviceTypeLower, out MPlatformKind kind)
    {
        switch (deviceTypeLower)
        {
            case "x":
                kind = MPlatformKind.X;
                return true;
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

    /// <summary>True for <c>platform</c> or axis-count aliases (<c>x</c>, <c>xy</c>, <c>xyz</c>, …).</summary>
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
            $"Unknown platform kind '{raw}'. Use x, xy, xyz, xyzu, xyzuv, or xyzuvw.");
    }

    /// <summary>
    /// Raw per-axis binding from <c>axis.X</c> / <c>axis.Y</c> … (may be a driver id or an Axis device id).
    /// Returns null when unset.
    /// </summary>
    public static string? TryGetAxisBinding(
        IReadOnlyDictionary<string, string> parameters,
        string axisLetter)
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

        return null;
    }

    /// <summary>
    /// Resolves per-axis driver id: <c>axis.X</c>, <c>axis.Y</c>, … may be a driver id or an Axis device id
    /// (resolved via <paramref name="resolveAxisDriverId"/>). Falls back to <paramref name="defaultDriverId"/> when unset.
    /// </summary>
    public static string ResolveAxisDriverId(
        IReadOnlyDictionary<string, string> parameters,
        string axisLetter,
        string defaultDriverId,
        Func<string, string?>? resolveAxisDriverId = null)
    {
        var letter = axisLetter.Trim();
        var binding = TryGetAxisBinding(parameters, letter);
        if (!string.IsNullOrWhiteSpace(binding))
        {
            var fromAxis = resolveAxisDriverId?.Invoke(binding);
            if (!string.IsNullOrWhiteSpace(fromAxis))
            {
                return fromAxis.Trim();
            }

            return binding;
        }

        if (!string.IsNullOrWhiteSpace(defaultDriverId))
        {
            return defaultDriverId.Trim();
        }

        throw new MdkException(
            MdkErrorCode.PlatformConfigurationInvalid,
            $"Platform axis '{letter}' has no driver: set device driverId or parameter axis.{letter} (driver id or Axis device id).");
    }

    /// <summary>
    /// Resolves per-axis card channel: <c>axisIndex.X</c>, <c>axisIndex.Y</c>, …
    /// Falls back to <paramref name="ordinalFallback"/> (letter order index) when unset.
    /// </summary>
    public static short ResolveAxisIndex(
        IReadOnlyDictionary<string, string> parameters,
        string axisLetter,
        short ordinalFallback)
    {
        var letter = axisLetter.Trim();
        foreach (var key in new[] { $"axisIndex.{letter}", $"axisIndex.{letter.ToLowerInvariant()}", $"axisNo.{letter}" })
        {
            if (parameters.TryGetValue(key, out var raw)
                && short.TryParse(raw.Trim(), out var v)
                && v >= 0)
            {
                return v;
            }
        }

        return ordinalFallback;
    }

    /// <summary>
    /// Default parameters for a platform kind: only <c>axis.X</c>… bindings (Axis device ids) + optional note.
    /// Kind/model/axisIndex are omitted — kind comes from device type; axis index comes from the Axis device.
    /// </summary>
    public static Dictionary<string, string> DefaultParameters(
        string kindToken,
        string? defaultDriverId = null,
        IReadOnlyList<string>? preferredAxisIds = null)
    {
        _ = defaultDriverId; // retained for call-site compatibility; platforms bind axes, not drivers.
        var kind = TryParseKindToken(kindToken, out var k) ? k : MPlatformKind.Xyz;
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["note"] = "",
        };

        // Only keep explicit kind when type is the generic "platform" token.
        if (string.Equals(kindToken.Trim(), "platform", StringComparison.OrdinalIgnoreCase))
        {
            dict["kind"] = kind.ToConfigToken();
        }

        short i = 0;
        foreach (var letter in kind.AxisLetters())
        {
            var axisId = preferredAxisIds is not null && i < preferredAxisIds.Count
                ? (preferredAxisIds[i] ?? "").Trim()
                : "";
            dict[$"axis.{letter}"] = axisId;
            i++;
        }

        return dict;
    }

    /// <summary>
    /// Keeps only platform-relevant keys: <c>axis.*</c>, optional <c>kind</c> (when type is <c>platform</c>), and <c>note</c>.
    /// Drops redundant <c>axisIndex.*</c> / <c>model</c> / duplicate kind for type aliases.
    /// </summary>
    public static Dictionary<string, string> NormalizeParameters(
        string deviceType,
        IReadOnlyDictionary<string, string> parameters)
    {
        var typeLower = (deviceType ?? "").Trim().ToLowerInvariant();
        MPlatformKind? fromAlias = TryKindFromDeviceType(typeLower, out var aliasKind) ? aliasKind : null;
        var kind = ParseKindOrDefault(parameters, fromAlias);
        var next = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (string.Equals(typeLower, "platform", StringComparison.OrdinalIgnoreCase))
        {
            next["kind"] = kind.ToConfigToken();
        }

        if (parameters.TryGetValue("note", out var note))
        {
            next["note"] = note ?? "";
        }

        foreach (var letter in kind.AxisLetters())
        {
            var binding = TryGetAxisBinding(parameters, letter) ?? "";
            next[$"axis.{letter}"] = binding;
        }

        return next;
    }

    private static bool TryParseKindToken(string token, out MPlatformKind kind)
    {
        var t = token.ToLowerInvariant();
        return TryKindFromDeviceType(t, out kind);
    }
}
