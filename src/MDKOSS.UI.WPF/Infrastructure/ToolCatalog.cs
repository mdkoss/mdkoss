namespace MDKOSS.UI.WPF.Infrastructure;

public sealed record ToolPageDef(string Id, string Label);

public sealed record ToolGroupDef(string Id, string Label, string ModeHint, IReadOnlyList<ToolPageDef> Pages);

public static class ToolCatalog
{
    public static readonly ToolGroupDef Monitor = new(
        "monitor",
        "监控",
        "只读态势",
        [
            new("monitor_runtime", "总览"),
            new("monitor_io", "IO"),
            new("monitor_platform", "平台"),
            new("monitor_axis", "轴"),
            new("monitor_camera", "相机"),
            new("monitor_vision", "视觉"),
            new("monitor_task", "任务"),
            new("monitor_alarm", "报警"),
        ]);

    public static readonly ToolGroupDef Debug = new(
        "debug",
        "调试",
        "可写联调",
        [
            new("debug_platform", "平台示教"),
            new("debug_serial", "串口"),
            new("debug_mysql", "MySQL"),
            new("debug_axis", "轴"),
            new("debug_io", "IO 强制"),
            new("debug_camera", "相机"),
            new("debug_vision", "视觉"),
            new("debug_driver", "驱动"),
            new("debug_db", "数据库"),
            new("debug_machine", "整机"),
            new("debug_alarm", "报警"),
        ]);

    public static readonly ToolGroupDef Man = new(
        "man",
        "配置",
        "配置编辑",
        [
            new("man_machine", "整机"),
            new("man_driver", "驱动"),
            new("man_device", "设备"),
            new("man_axis", "轴"),
            new("man_platform", "平台"),
            new("man_gpio", "GPIO"),
            new("man_task", "任务"),
            new("man_vars", "变量"),
            new("man_recipe", "配方"),
            new("man_vision", "视觉"),
            new("man_alarm", "报警"),
        ]);

    public static IReadOnlyList<ToolGroupDef> Groups { get; } = [Monitor, Debug, Man];

    public static ToolGroupDef ResolveGroup(string? groupId)
    {
        return Groups.FirstOrDefault(g =>
                   string.Equals(g.Id, groupId, StringComparison.OrdinalIgnoreCase))
               ?? Monitor;
    }

    public static ToolPageDef ResolvePage(ToolGroupDef group, string? pageId)
    {
        return group.Pages.FirstOrDefault(p =>
                   string.Equals(p.Id, pageId, StringComparison.OrdinalIgnoreCase))
               ?? group.Pages[0];
    }

    public static string DefaultPageId(string groupId) => ResolveGroup(groupId).Pages[0].Id;
}
