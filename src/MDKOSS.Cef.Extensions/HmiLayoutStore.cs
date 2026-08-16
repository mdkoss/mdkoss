using System.Text.Json;
using MDKOSS.Core;

namespace MDKOSS.Cef.Extensions;

/// <summary>Persists <see cref="HmiLayout"/> next to the setting file (or <c>configs/hmi.layout.json</c>).</summary>
public static class HmiLayoutStore
{
    public const string FileName = "hmi.layout.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
    };

    public static string ResolvePath(MdkRuntime runtime)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        return ResolvePath(runtime.SettingPath);
    }

    public static string ResolvePath(string? settingPath)
    {
        if (!string.IsNullOrWhiteSpace(settingPath))
        {
            var dir = Path.GetDirectoryName(Path.GetFullPath(settingPath.Trim()));
            if (!string.IsNullOrEmpty(dir))
            {
                return Path.Combine(dir, FileName);
            }
        }

        return Path.Combine(AppContext.BaseDirectory, "configs", FileName);
    }

    public static HmiLayout LoadOrDefault(MdkRuntime runtime)
        => LoadFromFile(ResolvePath(runtime));

    public static HmiLayout LoadOrDefault(string? settingPath)
        => LoadFromFile(ResolvePath(settingPath));

    public static HmiLayout LoadFromFile(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return HmiLayout.CreateDefault();
        }

        try
        {
            return Parse(File.ReadAllText(path)) ?? HmiLayout.CreateDefault();
        }
        catch
        {
            return HmiLayout.CreateDefault();
        }
    }

    public static HmiLayout Clone(HmiLayout layout)
        => Parse(Serialize(layout)) ?? HmiLayout.CreateDefault();

    public static HmiLayout? Parse(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        var layout = JsonSerializer.Deserialize<HmiLayout>(json, JsonOptions);
        if (layout is null)
        {
            return null;
        }

        Normalize(layout);
        return layout;
    }

    public static string Serialize(HmiLayout layout)
    {
        ArgumentNullException.ThrowIfNull(layout);
        Normalize(layout);
        return JsonSerializer.Serialize(layout, JsonOptions);
    }

    public static void Save(MdkRuntime runtime, HmiLayout layout)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        ArgumentNullException.ThrowIfNull(layout);
        Normalize(layout);
        var path = ResolvePath(runtime);
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        File.WriteAllText(path, Serialize(layout));
    }

    public static void SaveToFile(string path, HmiLayout layout)
    {
        ArgumentNullException.ThrowIfNull(layout);
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("Path cannot be empty.", nameof(path));
        }

        Normalize(layout);
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        File.WriteAllText(path, Serialize(layout));
    }

    public static void Normalize(HmiLayout layout)
    {
        layout.Version = layout.Version <= 0 ? 1 : layout.Version;
        if (string.IsNullOrWhiteSpace(layout.Title))
        {
            layout.Title = "主界面监控";
        }

        layout.CanvasWidth = layout.CanvasWidth <= 0 ? 1180 : layout.CanvasWidth;
        layout.CanvasHeight = layout.CanvasHeight <= 0 ? 520 : layout.CanvasHeight;
        layout.Widgets ??= [];
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var widget in layout.Widgets)
        {
            if (string.IsNullOrWhiteSpace(widget.Id) || !seen.Add(widget.Id))
            {
                widget.Id = "w-" + Guid.NewGuid().ToString("N")[..8];
                seen.Add(widget.Id);
            }

            widget.Type = (widget.Type ?? "").Trim().ToLowerInvariant();
            widget.W = widget.W <= 0 ? 80 : widget.W;
            widget.H = widget.H <= 0 ? 32 : widget.H;
            widget.Props ??= new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        }
    }
}
