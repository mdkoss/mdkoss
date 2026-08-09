using System.Text.RegularExpressions;

namespace MDKOSS.Core;

/// <summary>
/// One virtual IO point declared in <see cref="MdkSetting.DeviceConfig.Parameters"/>.
/// When <see cref="IsBidirectional"/> is true, the point has no in/out distinction (key = alias, e.g. <c>vio.b1</c>).
/// </summary>
public readonly record struct VioPointBinding(string Alias, bool IsOutput, bool IsBidirectional = false);

/// <summary>
/// Parses virtual IO (<c>vio</c> device) bindings.
/// Preferred keys: <c>vio.b1</c>…<c>vio.bN</c>（不区分 in/out）.
/// Legacy keys: <c>in.*</c> / <c>out.*</c> with empty or <c>virtual</c> values.
/// </summary>
public static partial class VioDeviceParameterSet
{
    /// <summary>Default bit count used by config「重置模板」.</summary>
    public const int DefaultBitCount = 128;

    /// <summary>Parameter key / alias prefix for undirected bits.</summary>
    public const string BitKeyPrefix = "vio.b";

    /// <summary>
    /// Builds undirected <c>vio.b1</c>…<c>vio.bN</c> virtual bindings for template reset.
    /// </summary>
    public static Dictionary<string, string> DefaultParameters(int bitCount = DefaultBitCount)
    {
        var n = Math.Clamp(bitCount, 1, 512);
        var dict = new Dictionary<string, string>(n, StringComparer.OrdinalIgnoreCase);
        for (var i = 1; i <= n; i++)
        {
            var key = $"{BitKeyPrefix}{i}";
            dict[key] = $"virtual|{key}";
        }

        return dict;
    }

    /// <summary>Returns true when <paramref name="key"/> is an undirected <c>vio.bN</c> bit key.</summary>
    public static bool IsUndirectedBitKey(string? key) =>
        !string.IsNullOrWhiteSpace(key) && UndirectedBitKeyRegex().IsMatch(key.Trim());

    /// <summary>
    /// Parses undirected <c>vio.bN</c> keys and legacy <c>in.*</c> / <c>out.*</c>.
    /// Value must be empty/whitespace or <c>virtual</c> (optional <c>|label</c>);
    /// physical <c>driverId:address</c> routes are not allowed.
    /// </summary>
    public static IReadOnlyList<VioPointBinding> ParseVirtualBindings(IReadOnlyDictionary<string, string> parameters)
    {
        var list = new List<VioPointBinding>();
        foreach (var kv in parameters)
        {
            var key = kv.Key;
            if (string.Equals(key, "driverIds", StringComparison.OrdinalIgnoreCase)
                || key.StartsWith("desc.", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (IsUndirectedBitKey(key))
            {
                EnsureVirtualValue(key, kv.Value);
                list.Add(new VioPointBinding(key.Trim(), IsOutput: false, IsBidirectional: true));
                continue;
            }

            if (key.StartsWith("in.", StringComparison.OrdinalIgnoreCase))
            {
                var alias = key[3..];
                EnsureVirtualValue(key, kv.Value);
                list.Add(new VioPointBinding(alias, IsOutput: false));
            }
            else if (key.StartsWith("out.", StringComparison.OrdinalIgnoreCase))
            {
                var alias = key[4..];
                EnsureVirtualValue(key, kv.Value);
                list.Add(new VioPointBinding(alias, IsOutput: true));
            }
        }

        return list;
    }

    private static void EnsureVirtualValue(string parameterKey, string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return;
        }

        var value = raw.Trim();
        var pipe = value.IndexOfAny([GpioDeviceParameterSet.LabelSeparator, '｜']);
        if (pipe >= 0)
        {
            value = value[..pipe].Trim();
        }

        if (string.IsNullOrWhiteSpace(value)
            || string.Equals(value, "virtual", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (GpioDeviceParameterSet.TryParsePointRoute(raw, out _, out _))
        {
            throw new MdkException(
                MdkErrorCode.VioBindingInvalid,
                $"VIO parameter '{parameterKey}' must be empty or 'virtual' (optional |label), not a physical route ({raw}).");
        }

        throw new MdkException(
            MdkErrorCode.VioBindingInvalid,
            $"VIO parameter '{parameterKey}' has unsupported value '{raw}' (use empty, 'virtual', or 'virtual|label').");
    }

    [GeneratedRegex(@"^vio\.b\d+$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex UndirectedBitKeyRegex();
}
