using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MDKOSS.Sample.Modbus.Machine;

/// <summary>
/// Named Modbus holding point (reg / regi / regf / bit / di / do) loaded from
/// <c>plc_registers.json</c>. Legacy <c>plc_registers.js</c> is converted, not loaded.
/// </summary>
public sealed class PlcRegisterPoint
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string Label { get; init; } = "";
    public int Address { get; init; }
    public string AddressHex { get; init; } = "0";
    public string Type { get; init; } = "reg";
    public int? Bit { get; init; }
    public string Group { get; init; } = "";
    public string Description { get; init; } = "";
    public string PlcAddress { get; init; } = "";
    public bool Writable { get; init; }
    public int WordCount { get; init; } = 1;
    public bool IsContinuation { get; init; }
}

public sealed class PlcRegisterCatalog
{
    public int Version { get; init; } = 1;
    public string Source { get; init; } = "";
    public IReadOnlyList<PlcRegisterPoint> Points { get; init; } = [];

    public IReadOnlyList<string> Groups =>
        Points
            .Select(p => p.Group)
            .Where(g => g.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToList();

    public PlcRegisterPoint? Find(string? id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return null;
        }

        return Points.FirstOrDefault(p =>
            string.Equals(p.Id, id.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    public static PlcRegisterCatalog Empty { get; } = new();

    public static PlcRegisterCatalog Load(string? settingPath, string? baseDirectory)
    {
        foreach (var dir in EnumerateSearchDirs(settingPath, baseDirectory))
        {
            var json = Path.Combine(dir, PlcConfigFiles.RegistersJson);
            if (File.Exists(json))
            {
                return LoadFromJson(File.ReadAllText(json), json);
            }
        }

        return Empty;
    }

    public static PlcRegisterCatalog LoadFromJs(string js, string? sourcePath = null)
    {
        var json = PlcRegisterJsConverter.ToJsonArray(js);
        using var doc = JsonDocument.Parse(json, JsonReadOpts);
        return FromJsArray(doc.RootElement, sourcePath ?? "plc_registers.js");
    }

    public static PlcRegisterCatalog LoadFromJson(string json, string? sourcePath = null)
    {
        using var doc = JsonDocument.Parse(json, JsonReadOpts);
        var root = doc.RootElement;
        if (root.ValueKind == JsonValueKind.Array)
        {
            return FromJsArray(root, sourcePath ?? "plc_registers.json");
        }

        if (root.TryGetProperty("points", out var points) && points.ValueKind == JsonValueKind.Array)
        {
            var list = new List<PlcRegisterPoint>();
            foreach (var el in points.EnumerateArray())
            {
                var point = JsonSerializer.Deserialize<PlcRegisterPoint>(el.GetRawText(), JsonOpts);
                if (point is not null && !string.IsNullOrWhiteSpace(point.Id))
                {
                    list.Add(point);
                }
            }

            return new PlcRegisterCatalog
            {
                Version = root.TryGetProperty("version", out var ver) && ver.TryGetInt32(out var v) ? v : 1,
                Source = root.TryGetProperty("source", out var src) ? src.GetString() ?? "" : sourcePath ?? "",
                Points = list,
            };
        }

        return Empty;
    }

    public string ToJson()
    {
        return JsonSerializer.Serialize(new
        {
            version = Version <= 0 ? 1 : Version,
            source = Source,
            groups = Groups,
            points = Points,
        }, JsonWriteOpts);
    }

    public IReadOnlyList<PlcRegisterPoint> PrimaryWidgets()
        => Points.Where(p => !p.IsContinuation && p.Bit is null && p.Name.Length > 0).ToList();

    public IReadOnlyList<PlcRegisterPoint> DisplayPoints()
        => Points.Where(p => !p.IsContinuation).ToList();

    public PlcPanelConfig ToPanels() => PlcPanelExport.FromCatalog(this);

    private static PlcRegisterCatalog FromJsArray(JsonElement array, string source)
    {
        var points = new List<PlcRegisterPoint>();
        var usedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string previousType = "";
        int previousAddress = int.MinValue;

        foreach (var el in array.EnumerateArray())
        {
            var addressHex = ReadString(el, "address");
            var address = ParseHex(addressHex);
            var name = ReadString(el, "name").Trim();
            var typeRaw = ReadString(el, "type");
            var type = NormalizeType(typeRaw);
            var desc = ReadString(el, "description");
            var plc = ReadString(el, "plcAddress");
            var access = ReadString(el, "access");
            var isContinuation = name.Length == 0
                && type == "reg"
                && previousAddress >= 0
                && address == previousAddress + 1
                && previousType is "regi" or "regf";

            var group = AssignGroup(address);
            var writable = IsWritable(type, access);
            var wordCount = type is "regi" or "regf" ? 2 : 1;
            var id = UniqueId(usedIds, name.Length > 0 ? name : $"hr_{address:X}", address, bit: null);

            if (!isContinuation)
            {
                points.Add(new PlcRegisterPoint
                {
                    Id = id,
                    Name = name,
                    Label = name.Length > 0 ? name : $"HR {addressHex}",
                    Address = address,
                    AddressHex = addressHex.Length > 0 ? addressHex : address.ToString("X", CultureInfo.InvariantCulture),
                    Type = type,
                    Group = group,
                    Description = desc,
                    PlcAddress = plc.Length > 0 ? plc : access,
                    Writable = writable,
                    WordCount = wordCount,
                    IsContinuation = false,
                });
            }
            else
            {
                points.Add(new PlcRegisterPoint
                {
                    Id = UniqueId(usedIds, $"hr_{address:X}", address, null),
                    Name = "",
                    Label = $"HR {addressHex} (续)",
                    Address = address,
                    AddressHex = addressHex,
                    Type = "reg",
                    Group = group,
                    Description = "32 位低字",
                    Writable = false,
                    WordCount = 1,
                    IsContinuation = true,
                });
            }

            if (el.TryGetProperty("bits", out var bitsEl) && bitsEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var bitEl in bitsEl.EnumerateArray())
                {
                    var bitNo = ParseBit(ReadString(bitEl, "bit"));
                    if (bitNo is null)
                    {
                        continue;
                    }

                    var bitName = PreferBitLabel(
                        ReadString(bitEl, "name"),
                        ReadString(bitEl, "description"),
                        ReadString(bitEl, "access"));
                    if (string.IsNullOrWhiteSpace(bitName))
                    {
                        continue;
                    }

                    var bitId = UniqueId(usedIds, $"{id}_b{bitNo.Value}", address, bitNo);
                    points.Add(new PlcRegisterPoint
                    {
                        Id = bitId,
                        Name = name,
                        Label = bitName.Trim(),
                        Address = address,
                        AddressHex = addressHex,
                        Type = "bit",
                        Bit = bitNo,
                        Group = group,
                        Description = ReadString(bitEl, "description"),
                        PlcAddress = ReadString(bitEl, "plcAddress"),
                        Writable = true,
                        WordCount = 1,
                    });
                }
            }

            previousType = type;
            previousAddress = address;
        }

        return new PlcRegisterCatalog
        {
            Version = 1,
            Source = source,
            Points = points,
        };
    }

    internal static IEnumerable<string> EnumerateSearchDirs(string? settingPath, string? baseDirectory)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(settingPath))
        {
            var dir = Path.GetDirectoryName(Path.GetFullPath(settingPath.Trim()));
            if (!string.IsNullOrEmpty(dir) && seen.Add(dir))
            {
                yield return dir;
            }
        }

        if (!string.IsNullOrWhiteSpace(baseDirectory))
        {
            var configs = Path.Combine(Path.GetFullPath(baseDirectory), "configs");
            if (seen.Add(configs))
            {
                yield return configs;
            }
        }
    }

    internal static string NormalizeType(string type)
    {
        return type.Trim().ToLowerInvariant() switch
        {
            "regi" => "regi",
            "regf" => "regf",
            "bit" => "bit",
            "di" => "di",
            "do" => "do",
            _ => "reg",
        };
    }

    internal static string AssignGroup(int address) => address switch
    {
        <= 3 => "编码器与张力",
        4 => "贴标故障",
        5 => "辊电机状态",
        <= 0x0A => "整机状态",
        <= 0x0E => "称重",
        <= 0x11 => "操作与收卷",
        <= 0x18 => "速度设定",
        <= 0x1E => "贴标位置",
        <= 0x23 => "操作按钮",
        <= 0x27 => "贴标坐标",
        <= 0x2B => "辊模式",
        <= 0x2F => "手动功能",
        <= 0x3B => "机型与差速",
        <= 0x43 => "PID",
        <= 0x4A => "幅宽与脉冲",
        _ => "PLC I/O",
    };

    private static bool IsWritable(string type, string access)
    {
        if (type == "di")
        {
            return false;
        }

        return type is "reg" or "regi" or "regf" or "bit" or "do";
    }

    private static string UniqueId(HashSet<string> used, string preferred, int address, int? bit)
    {
        var stem = SanitizeId(preferred);
        if (stem.Length == 0)
        {
            stem = bit is null ? $"hr_{address:X}" : $"hr_{address:X}_b{bit}";
        }

        var id = stem;
        var n = 2;
        while (!used.Add(id))
        {
            id = bit is null ? $"{stem}_{address:X}_{n}" : $"{stem}_{n}";
            n++;
        }

        return id;
    }

    private static string SanitizeId(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return "";
        }

        var sb = new StringBuilder(raw.Length);
        foreach (var ch in raw.Trim())
        {
            if (char.IsLetterOrDigit(ch) || ch is '_' or '-' or '.')
            {
                sb.Append(ch);
            }
        }

        return sb.ToString();
    }

    private static int ParseHex(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return 0;
        }

        return int.TryParse(text.Trim(), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var n)
            ? n
            : 0;
    }

    private static int? ParseBit(string text)
    {
        if (int.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var n)
            && n is >= 0 and <= 15)
        {
            return n;
        }

        return null;
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

    private static string PreferBitLabel(params string[] parts)
    {
        foreach (var part in parts)
        {
            if (IsHumanBitLabel(part))
            {
                return part.Trim();
            }
        }

        return FirstNonEmpty(parts);
    }

    private static bool IsHumanBitLabel(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var t = text.Trim();
        if (t is "0" or "1" or "DO" or "DI" or "do" or "di" or "en")
        {
            return false;
        }

        if (t.StartsWith("(V", StringComparison.OrdinalIgnoreCase)
            || t.StartsWith("BIT", StringComparison.OrdinalIgnoreCase)
            || t.StartsWith("I", StringComparison.Ordinal) && t.Contains('.')
            || t.StartsWith("Q", StringComparison.Ordinal) && t.Contains('.'))
        {
            return false;
        }

        return t.Any(static ch => ch > 127) || t.Length > 2;
    }

    private static string FirstNonEmpty(params string[] parts)
        => parts.FirstOrDefault(s => !string.IsNullOrWhiteSpace(s)) ?? "";

    private static readonly JsonDocumentOptions JsonReadOpts = new()
    {
        AllowTrailingCommas = true,
        CommentHandling = JsonCommentHandling.Skip,
    };

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private static readonly JsonSerializerOptions JsonWriteOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };
}

/// <summary>Converts the sample <c>plc_registers.js</c> object-literal array into JSON.</summary>
public static class PlcRegisterJsConverter
{
    public static string ToJsonArray(string js)
    {
        ArgumentNullException.ThrowIfNull(js);
        var start = js.IndexOf('[');
        var end = js.LastIndexOf(']');
        if (start < 0 || end <= start)
        {
            throw new InvalidDataException("plc_registers.js 中未找到数组。");
        }

        return ConvertSlice(js.AsSpan(start, end - start + 1));
    }

    /// <summary>Converts a JS object literal (e.g. <c>PLC_PANELS = { ... }</c>) into JSON.</summary>
    public static string ToJsonObject(string js, string marker = "PLC_PANELS")
    {
        ArgumentNullException.ThrowIfNull(js);
        var idx = js.IndexOf(marker, StringComparison.Ordinal);
        var from = idx < 0 ? 0 : idx;
        var eq = js.IndexOf('=', from);
        var start = js.IndexOf('{', eq >= 0 ? eq : from);
        if (start < 0)
        {
            throw new InvalidDataException("未找到对象字面量。");
        }

        var end = FindMatchingBrace(js, start);
        return ConvertSlice(js.AsSpan(start, end - start + 1));
    }

    private static int FindMatchingBrace(string js, int start)
    {
        var depth = 0;
        var i = start;
        while (i < js.Length)
        {
            var c = js[i];
            if (c is '\'' or '"')
            {
                i = SkipJsString(js, i);
                continue;
            }

            if (c == '/' && i + 1 < js.Length)
            {
                if (js[i + 1] == '/')
                {
                    i = js.IndexOf('\n', i);
                    if (i < 0)
                    {
                        break;
                    }

                    i++;
                    continue;
                }

                if (js[i + 1] == '*')
                {
                    var close = js.IndexOf("*/", i + 2, StringComparison.Ordinal);
                    i = close < 0 ? js.Length : close + 2;
                    continue;
                }
            }

            if (c == '{')
            {
                depth++;
            }
            else if (c == '}')
            {
                depth--;
                if (depth == 0)
                {
                    return i;
                }
            }

            i++;
        }

        throw new InvalidDataException("对象字面量括号不匹配。");
    }

    private static int SkipJsString(string js, int i)
    {
        var quote = js[i];
        i++;
        while (i < js.Length)
        {
            if (js[i] == '\\' && i + 1 < js.Length)
            {
                i += 2;
                continue;
            }

            if (js[i] == quote)
            {
                return i + 1;
            }

            i++;
        }

        return i;
    }

    private static string ConvertSlice(ReadOnlySpan<char> src)
    {
        var sb = new StringBuilder(src.Length + 64);
        var i = 0;
        while (i < src.Length)
        {
            var c = src[i];
            if (c == '\'' || c == '"')
            {
                i = AppendJsString(src, i, sb);
                continue;
            }

            if (c == '/' && i + 1 < src.Length)
            {
                if (src[i + 1] == '/')
                {
                    while (i < src.Length && src[i] is not '\n')
                    {
                        i++;
                    }

                    continue;
                }

                if (src[i + 1] == '*')
                {
                    i += 2;
                    while (i + 1 < src.Length && !(src[i] == '*' && src[i + 1] == '/'))
                    {
                        i++;
                    }

                    i = Math.Min(i + 2, src.Length);
                    continue;
                }
            }

            if (char.IsLetter(c) || c == '_' || c > 127)
            {
                var start = i;
                i++;
                while (i < src.Length && (char.IsLetterOrDigit(src[i]) || src[i] is '_' or '-' || src[i] > 127))
                {
                    i++;
                }

                var ident = src[start..i].ToString();
                var k = i;
                while (k < src.Length && char.IsWhiteSpace(src[k]))
                {
                    k++;
                }

                if (k < src.Length && src[k] == ':')
                {
                    sb.Append('"').Append(ident).Append('"');
                }
                else if (ident is "true" or "false" or "null")
                {
                    sb.Append(ident);
                }
                else
                {
                    sb.Append('"').Append(EscapeJson(ident)).Append('"');
                }

                continue;
            }

            sb.Append(c);
            i++;
        }

        return sb.ToString();
    }

    private static int AppendJsString(ReadOnlySpan<char> src, int i, StringBuilder sb)
    {
        var quote = src[i];
        i++;
        sb.Append('"');
        while (i < src.Length)
        {
            var c = src[i];
            if (c == '\\' && i + 1 < src.Length)
            {
                var n = src[i + 1];
                if (n == quote)
                {
                    sb.Append('\\').Append(quote == '"' ? '"' : n);
                    i += 2;
                    continue;
                }

                sb.Append('\\').Append(n);
                i += 2;
                continue;
            }

            if (c == quote)
            {
                sb.Append('"');
                return i + 1;
            }

            if (c == '"')
            {
                sb.Append('\\').Append('"');
                i++;
                continue;
            }

            if (c is '\r' or '\n')
            {
                sb.Append(' ');
                i++;
                continue;
            }

            sb.Append(c);
            i++;
        }

        sb.Append('"');
        return i;
    }

    private static string EscapeJson(string text)
        => text.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal);
}
