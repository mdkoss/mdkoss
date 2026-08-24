using System.Text.Json;
using System.Text.Json.Serialization;

namespace MDKOSS.Sample.Modbus.Machine;

public sealed class ModbusHmiWidget
{
    public string Id { get; set; } = "";
    public string PointId { get; set; } = "";
    public string Kind { get; set; } = "reg";
    public string Group { get; set; } = "";
    public double X { get; set; }
    public double Y { get; set; }
    public double W { get; set; } = 240;
    public double H { get; set; } = 84;
}

public sealed class ModbusHmiLayout
{
    public int Version { get; set; } = 1;
    public int RefreshMs { get; set; } = 100;
    public List<ModbusHmiWidget> Widgets { get; set; } = [];
}

public static class ModbusHmiLayoutStore
{
    public const string FileName = "modbus.layout.json";

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static string ResolvePath(string? settingPath, string? baseDirectory)
    {
        if (!string.IsNullOrWhiteSpace(settingPath))
        {
            var dir = Path.GetDirectoryName(Path.GetFullPath(settingPath.Trim()));
            if (!string.IsNullOrEmpty(dir))
            {
                return Path.Combine(dir, FileName);
            }
        }

        var root = string.IsNullOrWhiteSpace(baseDirectory)
            ? AppContext.BaseDirectory
            : baseDirectory;
        return Path.Combine(root, "configs", FileName);
    }

    public static ModbusHmiLayout LoadOrDefault(string path, PlcRegisterCatalog catalog)
    {
        if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
        {
            try
            {
                var layout = JsonSerializer.Deserialize<ModbusHmiLayout>(File.ReadAllText(path), JsonOpts);
                if (layout is not null)
                {
                    Normalize(layout);
                    if (layout.Widgets.Count > 0)
                    {
                        return layout;
                    }
                }
            }
            catch
            {
                // Fall through to default.
            }
        }

        return CreateDefault(catalog);
    }

    public static void Save(string path, ModbusHmiLayout layout)
    {
        ArgumentNullException.ThrowIfNull(layout);
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("Path is required.", nameof(path));
        }

        Normalize(layout);
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        File.WriteAllText(path, JsonSerializer.Serialize(layout, JsonOpts));
    }

    public static ModbusHmiLayout CreateDefault(PlcRegisterCatalog catalog)
    {
        var layout = new ModbusHmiLayout { Version = 1, RefreshMs = 100 };
        var groups = catalog.DisplayPoints()
            .GroupBy(p => p.Group, StringComparer.Ordinal)
            .ToList();
        var y = 16.0;
        foreach (var g in groups)
        {
            var x = 16.0;
            var rowH = 0.0;
            var col = 0;
            foreach (var p in g)
            {
                var (w, h) = DefaultSize(p.Type);
                if (col > 0 && col % 4 == 0)
                {
                    x = 16;
                    y += rowH + 12;
                    rowH = 0;
                    col = 0;
                }

                layout.Widgets.Add(new ModbusHmiWidget
                {
                    Id = "w-" + p.Id,
                    PointId = p.Id,
                    Kind = p.Type,
                    Group = p.Group,
                    X = x,
                    Y = y,
                    W = w,
                    H = h,
                });
                x += w + 12;
                rowH = Math.Max(rowH, h);
                col++;
            }

            y += rowH + 36;
        }

        return layout;
    }

    public static void Normalize(ModbusHmiLayout layout)
    {
        layout.Version = layout.Version <= 0 ? 1 : layout.Version;
        layout.RefreshMs = layout.RefreshMs <= 0 ? 100 : layout.RefreshMs;
        layout.Widgets ??= [];
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var w in layout.Widgets)
        {
            if (string.IsNullOrWhiteSpace(w.Id) || !seen.Add(w.Id))
            {
                w.Id = "w-" + Guid.NewGuid().ToString("N")[..8];
                seen.Add(w.Id);
            }

            w.Kind = string.IsNullOrWhiteSpace(w.Kind) ? "reg" : w.Kind.Trim().ToLowerInvariant();
            w.W = w.W <= 0 ? 240 : w.W;
            w.H = w.H <= 0 ? 72 : w.H;
        }
    }

    private static (double W, double H) DefaultSize(string type) => type switch
    {
        "bit" => (200, 56),
        "regf" => (260, 88),
        "regi" => (260, 88),
        _ => (240, 84),
    };
}
