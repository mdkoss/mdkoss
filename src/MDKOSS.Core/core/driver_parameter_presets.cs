using System.Text.Encodings.Web;
using System.Text.Json;

namespace MDKOSS.Core;

/// <summary>
/// Per-driver-type default <c>parameters</c> as JSON, used by config UI「重置模板」/新建填充。
/// </summary>
public static class DriverParameterPresets
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>Known driver types that have a default JSON template.</summary>
    public static IReadOnlyList<string> KnownTypes { get; } =
        ["sim", "vio", "gts", "dmc"];

    /// <summary>Raw default parameters JSON for <paramref name="type"/> (empty object when unknown).</summary>
    public static string DefaultJson(string? type) =>
        (type ?? "").Trim().ToLowerInvariant() switch
        {
            "sim" => SimJson,
            "vio" => VioJson,
            "gts" => GtsJson,
            "dmc" => DmcJson,
            _ => "{}",
        };

    /// <summary>Parsed default parameters dictionary for <paramref name="type"/>.</summary>
    public static Dictionary<string, string> ForType(string? type)
    {
        var json = DefaultJson(type);
        if (json is "{}" or "")
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        return ParseJsonObject(json);
    }

    /// <summary>Serialize a parameters map to indented JSON (for UI preview / export).</summary>
    public static string ToJson(IReadOnlyDictionary<string, string> parameters) =>
        JsonSerializer.Serialize(
            parameters.OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(kv => kv.Key, kv => kv.Value),
            JsonOptions);

    private static Dictionary<string, string> ParseJsonObject(string json)
    {
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Object)
        {
            return dict;
        }

        foreach (var prop in doc.RootElement.EnumerateObject())
        {
            dict[prop.Name] = prop.Value.ValueKind switch
            {
                JsonValueKind.String => prop.Value.GetString() ?? "",
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                JsonValueKind.Number => prop.Value.ToString(),
                JsonValueKind.Null => "",
                _ => prop.Value.ToString(),
            };
        }

        return dict;
    }

    // ── Default JSON blobs (source of truth for「重置模板」) ──────────────

    public const string SimJson =
        """
        {
          "ip": "127.0.0.1",
          "port": "5000",
          "card": "0",
          "model": "VirtualCard",
          "inBits": "32",
          "outBits": "32",
          "ioBitBase": "0",
          "note": "SIM VirtualCard"
        }
        """;

    public const string VioJson =
        """
        {
          "inBits": "128",
          "outBits": "128",
          "ioBitBase": "0",
          "model": "VirtualCard",
          "note": "VIO 128bit DI/DO"
        }
        """;

    public const string GtsJson =
        """
        {
          "cardNo": "0",
          "channel": "0",
          "openParam": "0",
          "resetOnInit": "false",
          "configPath": "",
          "note": "GTS motion card"
        }
        """;

    public const string DmcJson =
        """
        {
          "card": "0",
          "configPath": "",
          "resetOnInit": "false",
          "sevonActiveLow": "true",
          "note": "DMC motion card"
        }
        """;
}
