namespace MDKOSS.Cef.Extensions;

/// <summary>
/// Compatibility facade over <see cref="HmiWidgetRegistry"/>.
/// Types come from widget packs (builtin folder + extra folders + <see cref="IHmiWidgetPackage"/>).
/// </summary>
public static class HmiWidgetCatalog
{
    public static IReadOnlyList<HmiWidgetDescriptor> All => HmiWidgetRegistry.All;

    public static bool IsKnown(string? type) => HmiWidgetRegistry.IsKnown(type);

    public static HmiWidgetDescriptor? Find(string? type) => HmiWidgetRegistry.Find(type);

    public static HmiWidgetInstance CreateInstance(string type, double x, double y, string? id = null)
    {
        var desc = Find(type) ?? throw new ArgumentException($"Unknown widget type '{type}'.", nameof(type));
        var props = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var prop in desc.Props)
        {
            if (prop.Default is not null)
            {
                props[prop.Key] = CoerceDefault(prop);
            }
        }

        return new HmiWidgetInstance
        {
            Id = string.IsNullOrWhiteSpace(id) ? "w-" + Guid.NewGuid().ToString("N")[..8] : id.Trim(),
            Type = desc.Type,
            X = x,
            Y = y,
            W = desc.DefaultW,
            H = desc.DefaultH,
            Props = props,
        };
    }

    private static object CoerceDefault(HmiWidgetProp prop)
    {
        if (prop.Kind == "number"
            && double.TryParse(prop.Default, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var n))
        {
            return n;
        }

        return prop.Default ?? "";
    }
}
