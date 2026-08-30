using Prism.Mvvm;

namespace MDKOSS.UI.WPF.Models;

public sealed class OrderRow : BindableBase
{
    private bool _isSelected;

    public string Id { get; init; } = "";
    public string Product { get; init; } = "—";
    public int Qty { get; init; }
    public string Status { get; init; } = "pending";
    public string StatusLabel { get; init; } = "等待";
    public double Progress { get; init; }
    public string ProgressText => $"{Progress:0}%";
    public string UpdatedAt { get; init; } = "—";
    public string? RecipeId { get; init; }
    public string? Notes { get; init; }

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }
}

public sealed class StatusChipRow
{
    public string Mode { get; init; } = "";
    public string Label { get; init; } = "";
}

public sealed class DeviceRow
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string Type { get; init; } = "";
    public string State { get; init; } = "";
    public string Driver { get; init; } = "";
    public string Online { get; init; } = "";
}

public sealed class TaskRow
{
    public string Name { get; init; } = "";
    public string Type { get; init; } = "";
    public int IntervalMs { get; init; }
    public string State { get; init; } = "";
}

public sealed class VarRow
{
    public string Key { get; init; } = "";
    public string Value { get; init; } = "";
}

public sealed class AlarmRow
{
    public string Id { get; init; } = "";
    public string Code { get; init; } = "";
    public string Name { get; init; } = "";
    public string Level { get; init; } = "";
    public string Message { get; init; } = "";
    public string TriggerTime { get; init; } = "";
}

public sealed class RecipeRow
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string Description { get; init; } = "";
    public bool IsActive { get; init; }
}

public sealed class KvRow
{
    public string Key { get; init; } = "";
    public string Value { get; init; } = "";
}

public sealed class ToolLinkRow
{
    public string GroupId { get; init; } = "";
    public string PageId { get; init; } = "";
    public string Label { get; init; } = "";
}

public sealed class ToolNavItem : BindableBase
{
    private bool _isActive;

    public string Id { get; init; } = "";
    public string Label { get; init; } = "";

    public bool IsActive
    {
        get => _isActive;
        set => SetProperty(ref _isActive, value);
    }
}
