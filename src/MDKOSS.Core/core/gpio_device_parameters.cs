namespace MDKOSS.Core;

/// <summary>One GPIO point binding parsed from device <c>parameters</c> (<c>in.*</c> / <c>out.*</c> keys).</summary>
public readonly record struct GpioPointBinding(
    string Alias,
    string DriverId,
    string Address,
    bool IsOutput,
    string Label = "");

/// <summary>Parses GPIO routing entries from <see cref="MdkSetting.DeviceConfig.Parameters"/>.</summary>
public static class GpioDeviceParameterSet
{
    /// <summary>
    /// Optional <c>driverIds</c> value: comma-separated runtime driver ids.
    /// When unset, the runtime attaches every enabled non-<c>vio</c> driver to the GPIO device.
    /// </summary>
    /// <returns><see langword="null"/> when unset or blank — meaning all non-vio drivers.</returns>
    public static HashSet<string>? ParseDriverScopeIds(IReadOnlyDictionary<string, string> parameters)
    {
        if (!parameters.TryGetValue("driverIds", out var raw) || string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var parts = raw.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 0 ? null : new HashSet<string>(parts, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Driver types that belong to virtual IO and must not be attached to <see cref="GpioDevice"/>.</summary>
    public static bool IsVioDriverType(string? driverType) =>
        string.Equals((driverType ?? "").Trim(), "vio", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Parses <c>in.alias</c> / <c>out.alias</c> keys.
    /// Preferred value form is <c>driverId:address</c> (optional <c>|label</c>) so multi-card IO is unambiguous.
    /// Short <c>address</c> / <c>address|label</c> remains accepted when <paramref name="defaultDriverId"/> is set.
    /// </summary>
    public static IReadOnlyList<GpioPointBinding> ParseBindings(
        IReadOnlyDictionary<string, string> parameters,
        string? defaultDriverId = null)
    {
        var list = new List<GpioPointBinding>();
        foreach (var kv in parameters)
        {
            var key = kv.Key;
            if (key.StartsWith("in.", StringComparison.OrdinalIgnoreCase))
            {
                var alias = key[3..];
                if (TryParsePointValue(kv.Value, defaultDriverId, out var driverId, out var address, out var label))
                {
                    list.Add(new GpioPointBinding(alias, driverId, address, IsOutput: false, label));
                }
            }
            else if (key.StartsWith("out.", StringComparison.OrdinalIgnoreCase))
            {
                var alias = key[4..];
                if (TryParsePointValue(kv.Value, defaultDriverId, out var driverId, out var address, out var label))
                {
                    list.Add(new GpioPointBinding(alias, driverId, address, IsOutput: true, label));
                }
            }
        }

        return list;
    }

    /// <summary>Parses <c>driverId:address</c> route syntax (legacy; label not supported).</summary>
    public static bool TryParsePointRoute(string? raw, out string driverId, out string address) =>
        TryParsePointValue(raw, defaultDriverId: null, out driverId, out address, out _);

    /// <summary>
    /// Parses IO parameter value. Desc may be merged after <c>|</c>:
    /// <c>drv-m1:0|急停</c>, <c>drv-m1:0</c>, legacy <c>0|急停</c> / <c>0</c> (needs defaultDriverId).
    /// </summary>
    public static bool TryParsePointValue(
        string? raw,
        string? defaultDriverId,
        out string driverId,
        out string address,
        out string label)
    {
        driverId = string.Empty;
        address = string.Empty;
        label = string.Empty;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        var value = raw.Trim();
        var pipe = value.IndexOf('|');
        if (pipe >= 0)
        {
            label = value[(pipe + 1)..].Trim();
            value = value[..pipe].Trim();
        }

        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var splitIndex = value.IndexOf(':');
        if (splitIndex > 0 && splitIndex < value.Length - 1)
        {
            driverId = value[..splitIndex].Trim();
            address = value[(splitIndex + 1)..].Trim();
            return !string.IsNullOrWhiteSpace(driverId) && !string.IsNullOrWhiteSpace(address);
        }

        // Short form: address only — bind to device.driverId / defaultDriverId.
        if (!string.IsNullOrWhiteSpace(defaultDriverId))
        {
            driverId = defaultDriverId.Trim();
            address = value;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Formats a point parameter value as <c>driverId:address</c> (optional <c>|label</c>)
    /// so the owning driver card is always visible in key-value parameters.
    /// </summary>
    public static string FormatPointValue(
        string driverId,
        string address,
        string? label = null,
        string? deviceDriverId = null)
    {
        _ = deviceDriverId; // retained for call-site compatibility; short form is no longer written.
        var addr = (address ?? "").Trim();
        var drv = (driverId ?? "").Trim();
        if (string.IsNullOrWhiteSpace(drv))
        {
            throw new ArgumentException("GPIO point driverId cannot be empty.", nameof(driverId));
        }

        if (string.IsNullOrWhiteSpace(addr))
        {
            throw new ArgumentException("GPIO point address cannot be empty.", nameof(address));
        }

        var core = $"{drv}:{addr}";
        var lab = (label ?? "").Trim();
        return string.IsNullOrWhiteSpace(lab) ? core : $"{core}|{lab}";
    }

    /// <summary>Reads label from a point value (<c>|</c> suffix) or legacy <c>desc.{alias}</c>.</summary>
    public static string ReadLabel(
        IReadOnlyDictionary<string, string> parameters,
        string alias,
        string? pointValue = null)
    {
        if (!string.IsNullOrWhiteSpace(pointValue)
            && TryParsePointValue(pointValue, defaultDriverId: "x", out _, out _, out var fromValue)
            && !string.IsNullOrWhiteSpace(fromValue))
        {
            return fromValue;
        }

        if (parameters.TryGetValue($"desc.{alias}", out var legacy) && !string.IsNullOrWhiteSpace(legacy))
        {
            return legacy.Trim();
        }

        return "";
    }

    /// <summary>
    /// Rewrites <c>in.*</c>/<c>out.*</c> values to canonical <c>driverId:address|label</c>,
    /// folds legacy <c>desc.{alias}</c> into the <c>|label</c> suffix, and returns keys in a stable order
    /// (<c>in.*</c> → <c>out.*</c> → other) for readable JSON saves.
    /// Short <c>address|label</c> forms are expanded when <paramref name="defaultDriverId"/> is set.
    /// </summary>
    public static Dictionary<string, string> NormalizeParameters(
        IReadOnlyDictionary<string, string> parameters,
        string? defaultDriverId = null)
    {
        var source = parameters ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var next = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var kv in source.OrderBy(static x => x.Key, ParameterKeyComparer.Instance))
        {
            var key = kv.Key?.Trim() ?? "";
            if (key.Length == 0)
            {
                continue;
            }

            if (key.StartsWith("desc.", StringComparison.OrdinalIgnoreCase))
            {
                // Folded into the matching in./out. value below (or dropped if unused).
                continue;
            }

            if (key.StartsWith("in.", StringComparison.OrdinalIgnoreCase)
                || key.StartsWith("out.", StringComparison.OrdinalIgnoreCase))
            {
                var alias = key.StartsWith("in.", StringComparison.OrdinalIgnoreCase) ? key[3..] : key[4..];
                var label = ReadLabel(source, alias, kv.Value);
                if (TryParsePointValue(kv.Value, defaultDriverId, out var driverId, out var address, out _))
                {
                    next[key] = FormatPointValue(driverId, address, label);
                    continue;
                }
            }

            next[key] = kv.Value ?? "";
        }

        return next;
    }

    private sealed class ParameterKeyComparer : IComparer<string>
    {
        public static ParameterKeyComparer Instance { get; } = new();

        public int Compare(string? x, string? y)
        {
            var gx = Group(x);
            var gy = Group(y);
            var g = gx.CompareTo(gy);
            return g != 0
                ? g
                : string.Compare(x, y, StringComparison.OrdinalIgnoreCase);
        }

        private static int Group(string? key)
        {
            if (string.IsNullOrEmpty(key))
            {
                return 3;
            }

            if (key.StartsWith("in.", StringComparison.OrdinalIgnoreCase))
            {
                return 0;
            }

            if (key.StartsWith("out.", StringComparison.OrdinalIgnoreCase))
            {
                return 1;
            }

            return 2;
        }
    }
}
