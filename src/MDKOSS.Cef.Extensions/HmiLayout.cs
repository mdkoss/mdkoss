using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MDKOSS.Cef.Extensions;

/// <summary>Main-HMI 组态画布（绝对坐标控件列表）。</summary>
public sealed class HmiLayout
{
    public int Version { get; set; } = 1;

    public string Title { get; set; } = "主界面监控";

    public int CanvasWidth { get; set; } = 1180;

    public int CanvasHeight { get; set; } = 520;

    public List<HmiWidgetInstance> Widgets { get; set; } = [];

    public static HmiLayout CreateDefault()
    {
        return new HmiLayout
        {
            Version = 1,
            Title = "主界面监控",
            CanvasWidth = 1180,
            CanvasHeight = 520,
            Widgets =
            [
                W("w-title", "label", 24, 20, 280, 36, new()
                {
                    ["text"] = "设备监控",
                    ["align"] = "left",
                    ["fontSize"] = 22,
                }),
                W("w-state", "value", 24, 72, 220, 72, new()
                {
                    ["var"] = "task.operation.state",
                    ["label"] = "操作状态",
                    ["unit"] = "",
                }),
                W("w-recipe", "value", 260, 72, 220, 72, new()
                {
                    ["var"] = "recipe.activeName",
                    ["label"] = "当前配方",
                    ["unit"] = "",
                }),
                W("w-speed", "value", 496, 72, 200, 72, new()
                {
                    ["var"] = "process.speed",
                    ["label"] = "工艺速度",
                    ["unit"] = "x",
                }),
                W("w-lamp", "lamp", 720, 72, 120, 72, new()
                {
                    ["var"] = "task.operation.lamp",
                    ["label"] = "塔灯",
                }),
                W("w-progress", "progress", 24, 164, 816, 56, new()
                {
                    ["var"] = "order.current.progress",
                    ["label"] = "工单进度",
                    ["min"] = 0,
                    ["max"] = 100,
                }),
                W("w-io", "status", 24, 240, 200, 44, new()
                {
                    ["var"] = "task.cycle.io.offline",
                    ["label"] = "驱动 IO",
                    ["okWhen"] = "zero",
                }),
                W("w-dev", "status", 240, 240, 200, 44, new()
                {
                    ["var"] = "task.cycle.dev.fault",
                    ["label"] = "设备故障",
                    ["okWhen"] = "zero",
                }),
                W("w-vision", "status", 456, 240, 200, 44, new()
                {
                    ["var"] = "vision.ok",
                    ["label"] = "视觉",
                    ["okWhen"] = "truthy",
                }),
                W("w-start", "button", 24, 308, 120, 44, new()
                {
                    ["text"] = "启动",
                    ["method"] = "POST",
                    ["url"] = "/api/task/start",
                    ["style"] = "start",
                }),
                W("w-stop", "button", 156, 308, 120, 44, new()
                {
                    ["text"] = "停止",
                    ["method"] = "POST",
                    ["url"] = "/api/task/stop",
                    ["style"] = "stop",
                }),
                W("w-reset", "button", 288, 308, 120, 44, new()
                {
                    ["text"] = "复位",
                    ["method"] = "POST",
                    ["url"] = "/api/task/reset",
                    ["style"] = "reset",
                }),
            ],
        };
    }

    private static HmiWidgetInstance W(
        string id,
        string type,
        double x,
        double y,
        double w,
        double h,
        Dictionary<string, object?> props)
        => new()
        {
            Id = id,
            Type = type,
            X = x,
            Y = y,
            W = w,
            H = h,
            Props = props,
        };
}

/// <summary>画布上的一个控件实例。</summary>
public sealed class HmiWidgetInstance
{
    public string Id { get; set; } = "";

    public string Type { get; set; } = "";

    public double X { get; set; }

    public double Y { get; set; }

    public double W { get; set; } = 120;

    public double H { get; set; } = 40;

    [JsonConverter(typeof(HmiPropsConverter))]
    public Dictionary<string, object?> Props { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>控件类型目录项（编辑器属性表）。</summary>
public sealed class HmiWidgetDescriptor
{
    public required string Type { get; init; }

    public required string DisplayName { get; init; }

    public required string Category { get; init; }

    public int DefaultW { get; init; } = 160;

    public int DefaultH { get; init; } = 48;

    public IReadOnlyList<HmiWidgetProp> Props { get; init; } = [];

    /// <summary>Browser URL for the widget script (filled by the registry).</summary>
    public string? Script { get; init; }

    /// <summary>Browser URL for optional widget CSS (filled by the registry).</summary>
    public string? Css { get; init; }

    /// <summary>Package id that registered this type (e.g. <c>hmi-builtin</c>).</summary>
    public string? Package { get; init; }
}

public sealed class HmiWidgetProp
{
    public required string Key { get; init; }

    public required string Label { get; init; }

    /// <summary><c>text</c> / <c>var</c> / <c>number</c> / <c>select</c>.</summary>
    public required string Kind { get; init; }

    public string? Default { get; init; }

    public IReadOnlyList<string>? Options { get; init; }
}

public static class HmiProps
{
    public static string GetString(IReadOnlyDictionary<string, object?>? props, string key, string fallback = "")
    {
        if (!TryGetRaw(props, key, out var raw) || raw is null)
        {
            return fallback;
        }

        if (raw is JsonElement je)
        {
            return je.ValueKind switch
            {
                JsonValueKind.String => je.GetString() ?? fallback,
                JsonValueKind.Number => je.ToString(),
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                JsonValueKind.Null or JsonValueKind.Undefined => fallback,
                _ => je.ToString(),
            };
        }

        return Convert.ToString(raw, CultureInfo.InvariantCulture) ?? fallback;
    }

    public static double GetNumber(IReadOnlyDictionary<string, object?>? props, string key, double fallback = 0)
    {
        var text = GetString(props, key, "");
        return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var n) ? n : fallback;
    }

    private static bool TryGetRaw(IReadOnlyDictionary<string, object?>? props, string key, out object? raw)
    {
        raw = null;
        if (props is null)
        {
            return false;
        }

        if (props.TryGetValue(key, out raw))
        {
            return true;
        }

        foreach (var (k, v) in props)
        {
            if (string.Equals(k, key, StringComparison.OrdinalIgnoreCase))
            {
                raw = v;
                return true;
            }
        }

        return false;
    }
}

/// <summary>Keeps prop bags case-insensitive after JSON round-trip.</summary>
internal sealed class HmiPropsConverter : JsonConverter<Dictionary<string, object?>>
{
    public override Dictionary<string, object?> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var result = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        using var doc = JsonDocument.ParseValue(ref reader);
        if (doc.RootElement.ValueKind != JsonValueKind.Object)
        {
            return result;
        }

        foreach (var prop in doc.RootElement.EnumerateObject())
        {
            result[prop.Name] = prop.Value.Clone();
        }

        return result;
    }

    public override void Write(Utf8JsonWriter writer, Dictionary<string, object?> value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        foreach (var (key, raw) in value)
        {
            writer.WritePropertyName(key);
            if (raw is JsonElement je)
            {
                je.WriteTo(writer);
            }
            else
            {
                JsonSerializer.Serialize(writer, raw, options);
            }
        }

        writer.WriteEndObject();
    }
}
