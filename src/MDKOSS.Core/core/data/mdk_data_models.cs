using System.Text.Json;
using System.Text.Json.Serialization;

namespace MDKOSS.Core.Data;

/// <summary>Production order queue entry (排单).</summary>
public sealed class ProductionOrderRecord
{
    public string Id { get; set; } = string.Empty;
    public string Product { get; set; } = string.Empty;
    public int Qty { get; set; } = 1;
    public string Status { get; set; } = "pending";
    public double Progress { get; set; }
    public string? RecipeId { get; set; }
    public int Priority { get; set; }
    public string? Notes { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }

    /// <summary>Project-defined extra columns (lot, line, customer, …), stored as fields_json.</summary>
    public Dictionary<string, string> Fields { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Captures unknown JSON properties on POST so any custom field is accepted.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }

    /// <summary>Moves <see cref="ExtensionData"/> keys into <see cref="Fields"/> (except reserved names).</summary>
    public void AbsorbExtensionData()
    {
        if (ExtensionData is null || ExtensionData.Count == 0)
        {
            ExtensionData = null;
            return;
        }

        foreach (var (key, el) in ExtensionData)
        {
            if (IsReservedOrderJsonKey(key))
            {
                continue;
            }

            Fields[key] = JsonElementToPlainString(el);
        }

        ExtensionData = null;
    }

    private static bool IsReservedOrderJsonKey(string key) =>
        key.Equals("id", StringComparison.OrdinalIgnoreCase)
        || key.Equals("product", StringComparison.OrdinalIgnoreCase)
        || key.Equals("qty", StringComparison.OrdinalIgnoreCase)
        || key.Equals("quantity", StringComparison.OrdinalIgnoreCase)
        || key.Equals("status", StringComparison.OrdinalIgnoreCase)
        || key.Equals("progress", StringComparison.OrdinalIgnoreCase)
        || key.Equals("recipeId", StringComparison.OrdinalIgnoreCase)
        || key.Equals("recipe_id", StringComparison.OrdinalIgnoreCase)
        || key.Equals("priority", StringComparison.OrdinalIgnoreCase)
        || key.Equals("notes", StringComparison.OrdinalIgnoreCase)
        || key.Equals("createdAtUtc", StringComparison.OrdinalIgnoreCase)
        || key.Equals("created_at", StringComparison.OrdinalIgnoreCase)
        || key.Equals("updatedAtUtc", StringComparison.OrdinalIgnoreCase)
        || key.Equals("updated_at", StringComparison.OrdinalIgnoreCase)
        || key.Equals("updatedAt", StringComparison.OrdinalIgnoreCase)
        || key.Equals("fields", StringComparison.OrdinalIgnoreCase);

    private static string JsonElementToPlainString(JsonElement el) =>
        el.ValueKind switch
        {
            JsonValueKind.String => el.GetString() ?? "",
            JsonValueKind.Null => "",
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            JsonValueKind.Number => el.GetRawText(),
            _ => el.GetRawText(),
        };
}

/// <summary>Persisted recipe (配方).</summary>
public sealed class RecipeRecord
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public Dictionary<string, object?> Vars { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}

/// <summary>Teach point file grouped by platform (点位文件).</summary>
public sealed class TeachPointFileRecord
{
    public string Id { get; set; } = string.Empty;
    public string PlatformId { get; set; } = string.Empty;
    public string Name { get; set; } = "default";
    public string? PlatformKind { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}

/// <summary>Single teach point within a file (示教点).</summary>
public sealed class TeachPointRecord
{
    public string Id { get; set; } = string.Empty;
    public string FileId { get; set; } = string.Empty;
    public string PointId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public Dictionary<string, double> Axes { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public int SortOrder { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}

/// <summary>Platform teach file with all points (export/import shape).</summary>
public sealed class TeachPointFileSnapshot
{
    public string PlatformId { get; set; } = string.Empty;
    public string? Kind { get; set; }
    public string FileName { get; set; } = "default";
    public IReadOnlyList<TeachPointSnapshot> Points { get; set; } = [];
}

public sealed record TeachPointSnapshot(string PointId, string Name, IReadOnlyDictionary<string, double> Axes);

/// <summary>Latest calibration parameters for one task in a project.</summary>
public sealed class CalibParamsRecord
{
    public string ProjectName { get; set; } = string.Empty;
    public string TaskName { get; set; } = string.Empty;
    public Dictionary<string, string> Params { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public DateTime UpdatedAtUtc { get; set; }
}

/// <summary>One calibration run (parameters snapshot + results).</summary>
public sealed class CalibResultRecord
{
    public string Id { get; set; } = string.Empty;
    public string ProjectName { get; set; } = string.Empty;
    public string TaskName { get; set; } = string.Empty;
    public Dictionary<string, string> Params { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string> Results { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public bool Ok { get; set; }
    public string Message { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
}
