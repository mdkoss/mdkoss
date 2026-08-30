using System.Globalization;
using MDKOSS.Core;

namespace MDKOSS.UI.WPF.Services;

public static class SnapshotReader
{
    public static IReadOnlyDictionary<string, object?> Vars(RuntimeSnapshot? snap) =>
        snap?.Vars ?? new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

    public static string VarStr(IReadOnlyDictionary<string, object?> vars, string key, string fallback = "—")
    {
        if (!vars.TryGetValue(key, out var raw) || raw is null)
        {
            return fallback;
        }

        var text = raw.ToString();
        return string.IsNullOrWhiteSpace(text) ? fallback : text;
    }

    public static double VarNum(IReadOnlyDictionary<string, object?> vars, string key, double fallback = 0)
    {
        if (!vars.TryGetValue(key, out var raw) || raw is null)
        {
            return fallback;
        }

        return raw switch
        {
            double d => d,
            float f => f,
            int i => i,
            long l => l,
            decimal m => (double)m,
            bool b => b ? 1 : 0,
            string s when double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var n) => n,
            _ => double.TryParse(raw.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var n)
                ? n
                : fallback,
        };
    }

    public static bool VarTruthy(IReadOnlyDictionary<string, object?> vars, string key)
    {
        if (!vars.TryGetValue(key, out var raw) || raw is null)
        {
            return false;
        }

        return raw switch
        {
            bool b => b,
            string s => s.Equals("true", StringComparison.OrdinalIgnoreCase) || s == "1",
            _ => VarNum(vars, key) != 0,
        };
    }

    public static string FormatUtc(DateTime utc)
    {
        if (utc == default)
        {
            return "—";
        }

        return utc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
    }
}
