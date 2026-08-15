using MDKOSS.Core.Drivers;

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
    /// Unified field separator for GPIO point values when saving JSON.
    /// Canonical form: <c>driverId|address|label</c> (label optional).
    /// </summary>
    public const char LabelSeparator = '|';

    /// <summary>Accepted field separators when reading (ASCII <c>|</c> and fullwidth <c>｜</c>).</summary>
    private static readonly char[] FieldSeparators = [LabelSeparator, '｜'];

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
    /// Preferred value form is <c>driverId|address</c> (optional <c>|label</c>).
    /// Legacy <c>driverId:address|label</c> and short <c>address|label</c> remain accepted when reading.
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

    /// <summary>Parses <c>driverId|address</c> or legacy <c>driverId:address</c> route syntax.</summary>
    public static bool TryParsePointRoute(string? raw, out string driverId, out string address) =>
        TryParsePointValue(raw, defaultDriverId: null, out driverId, out address, out _);

    /// <summary>
    /// Parses IO parameter value. Preferred: <c>drv-m1|di.gpi.bit.1|急停</c>.
    /// Also accepts legacy <c>drv-m1:0|急停</c>, <c>drv-m1|0</c>, <c>0|急停</c> / <c>0</c> (needs defaultDriverId).
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

        var parts = SplitFields(raw.Trim());
        if (parts.Count == 0)
        {
            return false;
        }

        if (parts.Count >= 3)
        {
            // driverId|address|label...
            driverId = parts[0];
            address = parts[1];
            label = string.Join(LabelSeparator, parts.Skip(2)).Trim();
            return !string.IsNullOrWhiteSpace(driverId) && !string.IsNullOrWhiteSpace(address);
        }

        if (parts.Count == 2)
        {
            var first = parts[0];
            var second = parts[1];

            // Legacy: driverId:address|label
            if (TrySplitDriverColonAddress(first, out driverId, out address))
            {
                label = second;
                return true;
            }

            // Preferred without label: driverId|address
            // (second token looks like an address; first is the driver id)
            if (LooksLikeIoAddress(second) && first.Any(static c => char.IsLetter(c)))
            {
                driverId = first;
                address = second;
                return true;
            }

            // Short: address|label
            if (!string.IsNullOrWhiteSpace(defaultDriverId))
            {
                driverId = defaultDriverId.Trim();
                address = first;
                label = second;
                return !string.IsNullOrWhiteSpace(address);
            }

            // driverId|address when address is non-standard (e.g. symbolic)
            if (first.Any(static c => char.IsLetter(c)) && !LooksLikeIoAddress(first))
            {
                driverId = first;
                address = second;
                return !string.IsNullOrWhiteSpace(address);
            }

            return false;
        }

        // Single token: driverId:address (legacy) or bare address.
        var only = parts[0];
        if (TrySplitDriverColonAddress(only, out driverId, out address))
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(defaultDriverId))
        {
            driverId = defaultDriverId.Trim();
            address = only;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Formats a point parameter value as unified <c>driverId|address</c> (optional <c>|label</c>).
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

        var core = $"{drv}{LabelSeparator}{addr}";
        var lab = (label ?? "").Trim();
        return string.IsNullOrWhiteSpace(lab) ? core : $"{core}{LabelSeparator}{lab}";
    }

    /// <summary>Reads label from a point value (<c>|</c> fields) or legacy <c>desc.{alias}</c>.</summary>
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
    /// Rewrites <c>in.*</c>/<c>out.*</c> values to canonical <c>driverId|address|label</c>,
    /// folds legacy <c>desc.{alias}</c> into the label field,
    /// and preserves existing key order for stable JSON diffs.
    /// </summary>
    public static Dictionary<string, string> NormalizeParameters(
        IReadOnlyDictionary<string, string> parameters,
        string? defaultDriverId = null)
    {
        var source = parameters ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var next = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var kv in source)
        {
            var key = kv.Key?.Trim() ?? "";
            if (key.Length == 0)
            {
                continue;
            }

            if (key.StartsWith("desc.", StringComparison.OrdinalIgnoreCase))
            {
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

    private static List<string> SplitFields(string value)
    {
        var parts = new List<string>();
        var start = 0;
        for (var i = 0; i < value.Length; i++)
        {
            if (value[i] is LabelSeparator or '｜')
            {
                parts.Add(value[start..i].Trim());
                start = i + 1;
            }
        }

        parts.Add(value[start..].Trim());
        return parts.Where(static p => p.Length > 0).ToList();
    }

    private static bool TrySplitDriverColonAddress(string value, out string driverId, out string address)
    {
        driverId = string.Empty;
        address = string.Empty;
        var splitIndex = value.IndexOf(':');
        if (splitIndex <= 0 || splitIndex >= value.Length - 1)
        {
            return false;
        }

        driverId = value[..splitIndex].Trim();
        address = value[(splitIndex + 1)..].Trim();
        return !string.IsNullOrWhiteSpace(driverId) && !string.IsNullOrWhiteSpace(address);
    }

    /// <summary>
    /// True when token looks like an IO address
    /// (driver form <c>di.gpi.bit.1</c> / <c>do.gpo.bit.1</c>, or short <c>0</c> / <c>X0</c>).
    /// </summary>
    private static bool LooksLikeIoAddress(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return false;
        }

        if (DriverIoAddress.LooksLike(token))
        {
            return true;
        }

        if (long.TryParse(token, out _))
        {
            return true;
        }

        var letters = 0;
        var digits = 0;
        foreach (var c in token)
        {
            if (char.IsLetter(c))
            {
                letters++;
            }
            else if (char.IsDigit(c))
            {
                digits++;
            }
            else
            {
                return false;
            }
        }

        return letters is >= 1 and <= 3 && digits >= 1;
    }
}
