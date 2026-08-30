using MDKOSS.Core;

namespace MDKOSS.Tools.Calib.Calib;

/// <summary>
/// Calibration items are tasks in the setting:
/// <c>parameters.calib=true</c>, or <c>type</c> starting with <c>calib</c>.
/// Flow items use <c>type=flow</c> (editable). Code items inherit <see cref="MDKOSS.Tasks.MotionTask"/>.
/// </summary>
public static class CalibCatalog
{
    public static readonly HashSet<string> HiddenParamKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "calib",
        "flowJson",
        "flowFile",
        "loop",
        "autoStart",
        "displayName",
        "group",
    };

    public static bool IsCalibTask(MdkSetting.TaskConfig? task)
    {
        if (task is null || string.IsNullOrWhiteSpace(task.Name))
        {
            return false;
        }

        if (task.Parameters.TryGetValue("calib", out var flag) && IsTruthy(flag))
        {
            return true;
        }

        var type = (task.Type ?? "").Trim();
        return type.StartsWith("calib", StringComparison.OrdinalIgnoreCase);
    }

    public static IReadOnlyList<MdkSetting.TaskConfig> List(MdkSetting setting)
    {
        ArgumentNullException.ThrowIfNull(setting);
        return setting.Tasks
            .Where(IsCalibTask)
            .OrderBy(t => DisplayGroup(t), StringComparer.OrdinalIgnoreCase)
            .ThenBy(t => DisplayName(t), StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static bool IsFlowKind(string? type)
    {
        var key = (type ?? "").Trim();
        return string.Equals(key, "flow", StringComparison.OrdinalIgnoreCase)
               || string.Equals(key, "script", StringComparison.OrdinalIgnoreCase);
    }

    public static string DisplayName(MdkSetting.TaskConfig task)
    {
        if (task.Parameters.TryGetValue("displayName", out var name) && !string.IsNullOrWhiteSpace(name))
        {
            return name.Trim();
        }

        return string.IsNullOrWhiteSpace(task.Name) ? task.Type : task.Name;
    }

    public static string DisplayGroup(MdkSetting.TaskConfig task)
    {
        if (task.Parameters.TryGetValue("group", out var group) && !string.IsNullOrWhiteSpace(group))
        {
            return group.Trim();
        }

        return IsFlowKind(task.Type) ? "Flow" : "Motion";
    }

    public static string KindLabel(MdkSetting.TaskConfig task) =>
        IsFlowKind(task.Type) ? "FlowTask" : "MotionTask";

    public static bool IsTruthy(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        return raw.Trim() is "1" or "true" or "True" or "yes" or "on";
    }
}
