using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MDKOSS.Sample.Modbus.Machine;

/// <summary>
/// Operator-panel config loaded from <c>plc_panels.json</c>
/// (exported from the legacy <c>PLC_PANELS</c> object).
/// </summary>
public sealed class PlcPanelConfig
{
    public int Version { get; init; } = 1;
    public int RefreshMs { get; init; } = 100;
    public double EncoderScale { get; init; } = 0.05 / 1000.0;
    public string Source { get; init; } = "";
    public PlcMainDisplay MainDisplay { get; init; } = new();
    public IReadOnlyList<PlcCommandButton> Commands { get; init; } = [];
    public IReadOnlyList<PlcPanel> Panels { get; init; } = [];
}

public sealed class PlcMainDisplay
{
    public string? PositionPointId { get; init; }
    public string PositionUnit { get; init; } = "m";
    public string? SpeedPointId { get; init; }
    public string SpeedUnit { get; init; } = "m/s";
}

public sealed class PlcCommandButton
{
    public string Id { get; init; } = "";
    public string Label { get; init; } = "";
    public string PointId { get; init; } = "";
    public string Kind { get; init; } = "pulse";
    public int Value { get; init; } = 1;
    public string CssClass { get; init; } = "";
}

public sealed class PlcPanel
{
    public string Id { get; init; } = "";
    public string Title { get; init; } = "";
    public IReadOnlyList<PlcPanelField> Fields { get; init; } = [];
}

public sealed class PlcPanelField
{
    public string Id { get; init; } = "";
    public string PointId { get; init; } = "";
    public string Label { get; init; } = "";
    public string Type { get; init; } = "short";
    public int Addr { get; init; }
    public int? Bit { get; init; }
    public string? Unit { get; init; }
    public bool? Writable { get; init; }
    public string? OffLabel { get; init; }
    public string? OnLabel { get; init; }
}

public static class PlcPanelExport
{
    public const string JsonFileName = PlcConfigFiles.PanelsJson;
    public const string JsFileName = PlcConfigFiles.PanelsJs;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static PlcPanelConfig LoadOrGenerate(string? settingPath, string? baseDirectory, PlcRegisterCatalog catalog)
    {
        var loaded = TryLoad(settingPath, baseDirectory, catalog);
        if (loaded is not null && loaded.Panels.Count > 0)
        {
            return loaded;
        }

        return FromCatalog(catalog);
    }

    public static PlcPanelConfig? TryLoad(string? settingPath, string? baseDirectory, PlcRegisterCatalog? catalog)
    {
        foreach (var dir in PlcRegisterCatalog.EnumerateSearchDirs(settingPath, baseDirectory))
        {
            var json = Path.Combine(dir, JsonFileName);
            if (!File.Exists(json))
            {
                continue;
            }

            try
            {
                var cfg = JsonSerializer.Deserialize<PlcPanelConfig>(File.ReadAllText(json), JsonOpts);
                if (cfg is not null && cfg.Panels.Count > 0)
                {
                    return BindToCatalog(cfg, catalog, json);
                }
            }
            catch
            {
                // Fall through to the next search dir.
            }
        }

        return null;
    }

    public static PlcPanelConfig ParsePlcConfigJs(string js, PlcRegisterCatalog? catalog, string? sourcePath = null)
    {
        var json = PlcRegisterJsConverter.ToJsonObject(js);
        using var doc = JsonDocument.Parse(json, new JsonDocumentOptions
        {
            AllowTrailingCommas = true,
            CommentHandling = JsonCommentHandling.Skip,
        });
        var panels = new List<PlcPanel>();
        foreach (var prop in doc.RootElement.EnumerateObject())
        {
            if (prop.Value.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var title = ReadString(prop.Value, "title");
            if (string.IsNullOrWhiteSpace(title))
            {
                title = prop.Name;
            }

            var fields = new List<PlcPanelField>();
            if (prop.Value.TryGetProperty("fields", out var fieldsEl) && fieldsEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var el in fieldsEl.EnumerateArray())
                {
                    var id = ReadString(el, "id");
                    if (string.IsNullOrWhiteSpace(id))
                    {
                        continue;
                    }

                    fields.Add(new PlcPanelField
                    {
                        Id = id,
                        PointId = ReadString(el, "pointId"),
                        Label = FirstNonEmpty(ReadString(el, "label"), id),
                        Type = FirstNonEmpty(ReadString(el, "type"), "short"),
                        Addr = ReadInt(el, "addr"),
                        Bit = ReadIntOrNull(el, "bit"),
                        Unit = EmptyToNull(ReadString(el, "unit")),
                        Writable = ReadBoolOrNull(el, "writable"),
                        OffLabel = EmptyToNull(ReadString(el, "offLabel")),
                        OnLabel = EmptyToNull(ReadString(el, "onLabel")),
                    });
                }
            }

            if (fields.Count == 0)
            {
                continue;
            }

            panels.Add(new PlcPanel
            {
                Id = prop.Name,
                Title = title,
                Fields = fields,
            });
        }

        var generated = catalog is null ? null : FromCatalog(catalog);
        return BindToCatalog(new PlcPanelConfig
        {
            Version = 1,
            RefreshMs = 100,
            EncoderScale = 0.05 / 1000.0,
            Source = sourcePath ?? JsFileName,
            MainDisplay = generated?.MainDisplay ?? new PlcMainDisplay(),
            Commands = generated?.Commands ?? [],
            Panels = panels,
        }, catalog, sourcePath ?? JsFileName);
    }

    public static PlcPanelConfig BindToCatalog(PlcPanelConfig config, PlcRegisterCatalog? catalog, string? source = null)
    {
        ArgumentNullException.ThrowIfNull(config);
        var panels = config.Panels.Select(panel => new PlcPanel
        {
            Id = panel.Id,
            Title = panel.Title,
            Fields = panel.Fields.Select(f => BindField(f, catalog)).ToList(),
        }).ToList();

        var commands = config.Commands.Count > 0
            ? config.Commands
            : catalog is null ? [] : FromCatalog(catalog).Commands;

        return new PlcPanelConfig
        {
            Version = config.Version <= 0 ? 1 : config.Version,
            RefreshMs = config.RefreshMs <= 0 ? 100 : config.RefreshMs,
            EncoderScale = config.EncoderScale <= 0 ? 0.05 / 1000.0 : config.EncoderScale,
            Source = string.IsNullOrWhiteSpace(source) ? config.Source : source,
            MainDisplay = config.MainDisplay ?? new PlcMainDisplay(),
            Commands = commands,
            Panels = panels,
        };
    }

    private static PlcPanelField BindField(PlcPanelField field, PlcRegisterCatalog? catalog)
    {
        var point = FindPoint(catalog, field);
        var pointId = FirstNonEmpty(field.PointId, point?.Id ?? "", field.Id);
        return new PlcPanelField
        {
            Id = field.Id,
            PointId = pointId,
            Label = field.Label,
            Type = field.Type,
            Addr = field.Addr,
            Bit = field.Bit,
            Unit = field.Unit,
            Writable = field.Writable ?? true,
            OffLabel = field.OffLabel,
            OnLabel = field.OnLabel,
        };
    }

    /// <summary>
    /// Adds holding points for panel fields that are not in the register catalog
    /// (addresses present in <c>plcconfig.js</c> / <c>plc_panels.json</c> only).
    /// </summary>
    public static PlcRegisterCatalog AugmentCatalog(PlcRegisterCatalog catalog, PlcPanelConfig config)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(config);
        var extra = new List<PlcRegisterPoint>();
        foreach (var field in config.Panels.SelectMany(p => p.Fields))
        {
            if (FindPoint(catalog, field) is not null)
            {
                continue;
            }

            var id = FirstNonEmpty(field.PointId, field.Id);
            if (string.IsNullOrWhiteSpace(id) || catalog.Find(id) is not null)
            {
                continue;
            }

            if (extra.Any(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            var type = field.Type switch
            {
                "int" => "regi",
                "float" => "regf",
                "bit" or "correctionMethod" => "bit",
                _ => "reg",
            };
            extra.Add(new PlcRegisterPoint
            {
                Id = id,
                Name = field.Id,
                Label = field.Label,
                Address = field.Addr,
                AddressHex = field.Addr.ToString("X", CultureInfo.InvariantCulture),
                Type = type,
                Bit = field.Bit ?? (type == "bit" ? 0 : null),
                Writable = field.Writable ?? true,
                WordCount = type is "regi" or "regf" ? 2 : 1,
            });
        }

        if (extra.Count == 0)
        {
            return catalog;
        }

        return new PlcRegisterCatalog
        {
            Version = catalog.Version,
            Source = catalog.Source,
            Points = catalog.Points.Concat(extra).ToList(),
        };
    }

    internal static PlcRegisterPoint? FindPoint(PlcRegisterCatalog? catalog, PlcPanelField field)
    {
        if (catalog is null)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(field.PointId))
        {
            var named = catalog.Find(field.PointId);
            if (named is not null)
            {
                return named;
            }
        }

        var bit = field.Bit;
        if (bit is not null || field.Type is "bit" or "correctionMethod")
        {
            return catalog.Points.FirstOrDefault(p =>
                p.Address == field.Addr && p.Bit == (bit ?? 0) && p.Type == "bit");
        }

        var want = field.Type switch
        {
            "int" => "regi",
            "float" => "regf",
            _ => "",
        };
        if (want.Length > 0)
        {
            var typed = catalog.Points.FirstOrDefault(p =>
                p.Address == field.Addr && p.Bit is null && !p.IsContinuation && p.Type == want);
            if (typed is not null)
            {
                return typed;
            }
        }

        return catalog.Points.FirstOrDefault(p =>
            p.Address == field.Addr && p.Bit is null && !p.IsContinuation);
    }

    public static PlcPanelConfig FromCatalog(PlcRegisterCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        var fieldsByGroup = new List<(string Group, PlcPanelField Field)>();
        foreach (var point in catalog.Points)
        {
            if (point.IsContinuation)
            {
                continue;
            }

            var field = ToField(point);
            if (field is null)
            {
                continue;
            }

            fieldsByGroup.Add((point.Group.Length > 0 ? point.Group : "其它", field));
        }

        var panels = new List<PlcPanel>();
        var n = 0;
        foreach (var g in fieldsByGroup.GroupBy(x => x.Group, StringComparer.Ordinal))
        {
            panels.Add(new PlcPanel
            {
                Id = $"panel{n:00}",
                Title = g.Key,
                Fields = g.Select(x => x.Field).ToList(),
            });
            n++;
        }

        return new PlcPanelConfig
        {
            Version = 1,
            RefreshMs = 100,
            EncoderScale = 0.05 / 1000.0,
            Source = catalog.Source,
            MainDisplay = new PlcMainDisplay
            {
                PositionPointId = FindNamed(catalog, "encoder")?.Id,
                SpeedPointId = FindNamed(catalog, "rollerm_speed")?.Id,
            },
            Commands = BuildCommands(catalog),
            Panels = panels,
        };
    }

    public static string ToJson(PlcPanelConfig config)
        => JsonSerializer.Serialize(config, JsonOpts);

    public static void TryWrite(string dir, PlcPanelConfig config)
    {
        try
        {
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, JsonFileName);
            if (File.Exists(path))
            {
                return;
            }

            File.WriteAllText(path, ToJson(config));
        }
        catch
        {
            // Local export is best-effort.
        }
    }

    private static PlcPanelField? ToField(PlcRegisterPoint point)
    {
        if (string.IsNullOrWhiteSpace(point.Id))
        {
            return null;
        }

        var uiType = MapUiType(point.Type);
        return new PlcPanelField
        {
            Id = point.Id,
            PointId = point.Id,
            Label = HumanLabel(point),
            Type = uiType,
            Addr = point.Address,
            Bit = point.Bit,
            Unit = InferUnit(point),
            Writable = point.Writable && point.Type != "di",
        };
    }

    internal static string MapUiType(string type) => type switch
    {
        "regi" => "int",
        "regf" => "float",
        "bit" or "di" or "do" => "bit",
        _ => "short",
    };

    internal static string HumanLabel(PlcRegisterPoint point)
    {
        if (point.Bit is not null && !string.IsNullOrWhiteSpace(point.Label))
        {
            return Truncate(point.Label.Trim(), 28);
        }

        var desc = (point.Description ?? "").Trim();
        desc = desc
            .Replace("高16位", "", StringComparison.Ordinal)
            .Replace("低16位", "", StringComparison.Ordinal)
            .Trim();
        if (LooksHuman(desc) && !string.Equals(desc, point.Name, StringComparison.OrdinalIgnoreCase))
        {
            var cut = desc.IndexOfAny(['；', ';', '。']);
            if (cut > 4)
            {
                desc = desc[..cut];
            }

            return Truncate(desc.Trim(), 28);
        }

        if (!string.IsNullOrWhiteSpace(point.Label))
        {
            return Truncate(point.Label.Trim(), 28);
        }

        return point.Name;
    }

    private static string? InferUnit(PlcRegisterPoint point)
    {
        var text = string.Concat(point.Description, " ", point.PlcAddress, " ", point.Label);
        if (text.Contains("米/S", StringComparison.OrdinalIgnoreCase)
            || text.Contains("m/s", StringComparison.OrdinalIgnoreCase))
        {
            return "m/s";
        }

        if (text.Contains("公斤", StringComparison.Ordinal) || text.Contains("kg", StringComparison.OrdinalIgnoreCase))
        {
            return "kg";
        }

        if (text.Contains("单位g", StringComparison.OrdinalIgnoreCase) || text.Contains("单位 g", StringComparison.OrdinalIgnoreCase))
        {
            return "g";
        }

        if (text.Contains("毫米", StringComparison.Ordinal) || text.Contains("mm", StringComparison.OrdinalIgnoreCase))
        {
            return "mm";
        }

        if (text.Contains("幅宽", StringComparison.Ordinal)
            || text.Contains("cm", StringComparison.OrdinalIgnoreCase))
        {
            return "cm";
        }

        if (text.Contains("百分比", StringComparison.Ordinal) || text.Contains('%'))
        {
            return "%";
        }

        if (text.Contains("张力", StringComparison.Ordinal) && point.Type == "regf")
        {
            return "N";
        }

        return null;
    }

    private static IReadOnlyList<PlcCommandButton> BuildCommands(PlcRegisterCatalog catalog)
    {
        var list = new List<PlcCommandButton>();
        AddCommand(list, catalog, "cmd_start", "op_start", "启动", "set", 1, "");
        AddCommand(list, catalog, "cmd_stop", "op_start", "停止", "set", 0, "");
        AddCommand(list, catalog, "cmd_reset_enc", "op_enc_reset", "复位", "pulse", 1, "");
        AddCommand(list, catalog, "cmd_testrun", "op_testrun", "空跑", "pulse", 1, "");
        var labeller = catalog.Points.FirstOrDefault(p =>
            p.Type == "bit"
            && (p.Label.Contains("贴标机归零", StringComparison.Ordinal)
                || p.Description.Contains("贴标机归零", StringComparison.Ordinal)));
        if (labeller is not null)
        {
            list.Add(new PlcCommandButton
            {
                Id = "cmd_labeller_zero",
                Label = "贴标机归零",
                PointId = labeller.Id,
                Kind = "pulse",
                Value = 1,
            });
        }

        AddCommand(list, catalog, "cmd_emg", "op_emg", "急停", "pulse", 1, "emergency");
        AddCommand(list, catalog, "cmd_alarm_reset", "op_reset", "报警复位", "pulse", 1, "reset-btn");
        return list;
    }

    private static void AddCommand(
        List<PlcCommandButton> list,
        PlcRegisterCatalog catalog,
        string id,
        string name,
        string label,
        string kind,
        int value,
        string css)
    {
        var point = FindNamed(catalog, name);
        if (point is null)
        {
            return;
        }

        list.Add(new PlcCommandButton
        {
            Id = id,
            Label = label,
            PointId = point.Id,
            Kind = kind,
            Value = value,
            CssClass = css,
        });
    }

    private static PlcRegisterPoint? FindNamed(PlcRegisterCatalog catalog, string name)
        => catalog.Points.FirstOrDefault(p =>
            !p.IsContinuation
            && p.Bit is null
            && string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));

    private static bool LooksHuman(string text) => text.Any(static ch => ch > 127);

    private static string Truncate(string text, int max)
    {
        if (text.Length <= max)
        {
            return text;
        }

        return string.Concat(text.AsSpan(0, max).TrimEnd(), "…");
    }

    private static string ReadString(JsonElement el, string name)
    {
        if (!el.TryGetProperty(name, out var p))
        {
            return "";
        }

        return p.ValueKind switch
        {
            JsonValueKind.String => p.GetString() ?? "",
            JsonValueKind.Number => p.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => "",
        };
    }

    private static int ReadInt(JsonElement el, string name)
    {
        if (!el.TryGetProperty(name, out var p))
        {
            return 0;
        }

        if (p.ValueKind == JsonValueKind.Number && p.TryGetInt32(out var n))
        {
            return n;
        }

        return int.TryParse(p.GetString(), out var parsed) ? parsed : 0;
    }

    private static int? ReadIntOrNull(JsonElement el, string name)
    {
        if (!el.TryGetProperty(name, out var p) || p.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return null;
        }

        if (p.ValueKind == JsonValueKind.Number && p.TryGetInt32(out var n))
        {
            return n;
        }

        return int.TryParse(p.GetString(), out var parsed) ? parsed : null;
    }

    private static bool? ReadBoolOrNull(JsonElement el, string name)
    {
        if (!el.TryGetProperty(name, out var p) || p.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return null;
        }

        return p.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => null,
        };
    }

    private static string FirstNonEmpty(params string[] parts)
        => parts.FirstOrDefault(s => !string.IsNullOrWhiteSpace(s)) ?? "";

    private static string? EmptyToNull(string text)
        => string.IsNullOrWhiteSpace(text) ? null : text;
}
