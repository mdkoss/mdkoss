using System.Globalization;
using MDKOSS.Cef.Extensions;

namespace MDKOSS.Config.Wpf;

/// <summary>Maps HMI widgets to the shared property-draft parameter table.</summary>
internal static class HmiDraftMapper
{
    public static Dictionary<string, string> ToParamBook(HmiWidgetInstance widget)
    {
        var book = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["x"] = FormatNum(widget.X),
            ["y"] = FormatNum(widget.Y),
            ["w"] = FormatNum(widget.W),
            ["h"] = FormatNum(widget.H),
        };

        var desc = HmiWidgetCatalog.Find(widget.Type);
        if (desc is not null)
        {
            foreach (var prop in desc.Props)
            {
                book[prop.Key] = HmiProps.GetString(widget.Props, prop.Key, prop.Default ?? "");
            }
        }

        foreach (var (key, _) in widget.Props)
        {
            if (IsGeometryKey(key) || book.ContainsKey(key))
            {
                continue;
            }

            book[key] = HmiProps.GetString(widget.Props, key, "");
        }

        return book;
    }

    public static void ApplyParamBook(HmiWidgetInstance widget, IReadOnlyDictionary<string, string> book)
    {
        widget.X = ReadNum(book, "x", widget.X);
        widget.Y = ReadNum(book, "y", widget.Y);
        widget.W = ReadNum(book, "w", widget.W);
        widget.H = ReadNum(book, "h", widget.H);

        var props = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        var desc = HmiWidgetCatalog.Find(widget.Type);
        foreach (var (key, raw) in book)
        {
            if (IsGeometryKey(key))
            {
                continue;
            }

            var kind = desc?.Props.FirstOrDefault(p =>
                string.Equals(p.Key, key, StringComparison.OrdinalIgnoreCase))?.Kind;
            if (kind == "number"
                && double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var n))
            {
                props[key] = n;
            }
            else
            {
                props[key] = raw ?? "";
            }
        }

        widget.Props = props;
    }

    public static string Describe(HmiWidgetInstance widget)
    {
        var label = HmiProps.GetString(widget.Props, "label", "");
        if (!string.IsNullOrWhiteSpace(label))
        {
            return label;
        }

        var text = HmiProps.GetString(widget.Props, "text", "");
        if (!string.IsNullOrWhiteSpace(text))
        {
            return text;
        }

        var varKey = HmiProps.GetString(widget.Props, "var", "");
        return string.IsNullOrWhiteSpace(varKey) ? widget.Type : varKey;
    }

    public static IReadOnlyList<string> ParamKeysFor(string? type)
    {
        var keys = new List<string> { "x", "y", "w", "h" };
        var desc = HmiWidgetCatalog.Find(type);
        if (desc is not null)
        {
            keys.AddRange(desc.Props.Select(p => p.Key));
        }

        return keys;
    }

    private static bool IsGeometryKey(string key) =>
        key.Equals("x", StringComparison.OrdinalIgnoreCase)
        || key.Equals("y", StringComparison.OrdinalIgnoreCase)
        || key.Equals("w", StringComparison.OrdinalIgnoreCase)
        || key.Equals("h", StringComparison.OrdinalIgnoreCase);

    private static string FormatNum(double n) => n.ToString("0.###", CultureInfo.InvariantCulture);

    private static double ReadNum(IReadOnlyDictionary<string, string> book, string key, double fallback)
    {
        if (!book.TryGetValue(key, out var raw)
            || !double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var n))
        {
            return fallback;
        }

        return n;
    }
}
