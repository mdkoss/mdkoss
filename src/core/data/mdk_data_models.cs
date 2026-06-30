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
