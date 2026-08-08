using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using MDKOSS.Core;
using MDKOSS.Core.Flow;

namespace MDKOSS.Config.Wpf.Debug;

/// <summary>Dedicated TaskConfig editor opened from the Debug menu.</summary>
public partial class TaskDebugWindow : Window
{
    private readonly ConfigWorkspace _workspace;
    private readonly Action? _onApplied;
    private readonly ObservableCollection<KvPairRow> _paramRows = [];
    private string? _preferredTaskName;
    private string? _editingOriginalName;
    private bool _suppressDirty;
    private bool _dirty;

    public TaskDebugWindow(ConfigWorkspace workspace, string? preferredTaskName = null, Action? onApplied = null)
    {
        InitializeComponent();
        _workspace = workspace;
        _preferredTaskName = preferredTaskName;
        _onApplied = onApplied;
        ParamGrid.ItemsSource = _paramRows;

        foreach (var t in ConfigTypeCatalog.TaskTypes)
        {
            TypeCombo.Items.Add(t);
        }

        Loaded += (_, _) =>
        {
            ReloadDriverOptions();
            ReloadTaskList();
        };
    }

    private void ReloadDriverOptions()
    {
        var current = DriverCombo.Text;
        DriverCombo.Items.Clear();
        DriverCombo.Items.Add(string.Empty);
        foreach (var id in _workspace.Setting.Drivers
                     .Select(d => d.Id)
                     .Where(id => !string.IsNullOrWhiteSpace(id))
                     .OrderBy(id => id, StringComparer.OrdinalIgnoreCase))
        {
            DriverCombo.Items.Add(id);
        }

        DriverCombo.Text = current;
    }

    private void ReloadTaskList(string? selectName = null)
    {
        _suppressDirty = true;
        TaskCombo.Items.Clear();
        var tasks = _workspace.Setting.Tasks
            .OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        foreach (var t in tasks)
        {
            TaskCombo.Items.Add(new ComboBoxItem
            {
                Content = $"{t.Name}  [{t.Type}]",
                Tag = t.Name,
            });
        }

        var prefer = selectName ?? _preferredTaskName;
        ComboBoxItem? match = null;
        if (!string.IsNullOrWhiteSpace(prefer))
        {
            match = TaskCombo.Items.Cast<ComboBoxItem>()
                .FirstOrDefault(i => string.Equals(i.Tag as string, prefer, StringComparison.OrdinalIgnoreCase));
        }

        TaskCombo.SelectedItem = match ?? (TaskCombo.Items.Count > 0 ? TaskCombo.Items[0] : null);
        _suppressDirty = false;
        BindSelected();
    }

    private MdkSetting.TaskConfig? SelectedTask()
    {
        if (TaskCombo.SelectedItem is ComboBoxItem { Tag: string name })
        {
            return _workspace.Setting.Tasks.FirstOrDefault(t =>
                string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase));
        }

        return null;
    }

    private void TaskCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_dirty && !_suppressDirty)
        {
            var ask = MessageBox.Show(
                this,
                "当前任务有未应用的修改，切换将丢弃。继续？",
                "未保存修改",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);
            if (ask != MessageBoxResult.Yes)
            {
                _suppressDirty = true;
                // revert selection to previous
                if (_editingOriginalName is not null)
                {
                    var prev = TaskCombo.Items.Cast<ComboBoxItem>()
                        .FirstOrDefault(i => string.Equals(i.Tag as string, _editingOriginalName, StringComparison.OrdinalIgnoreCase));
                    if (prev is not null)
                    {
                        TaskCombo.SelectedItem = prev;
                    }
                }

                _suppressDirty = false;
                return;
            }
        }

        BindSelected();
    }

    private void BindSelected()
    {
        _suppressDirty = true;
        var task = SelectedTask();
        if (task is null)
        {
            _editingOriginalName = null;
            NameBox.Text = TypeCombo.Text = DriverCombo.Text = IntervalBox.Text = string.Empty;
            _paramRows.Clear();
            ParamsJsonBox.Text = string.Empty;
            HelpBox.Text = "请选择一个 Task。";
            SetDirty(false);
            _suppressDirty = false;
            return;
        }

        _editingOriginalName = task.Name;
        _preferredTaskName = task.Name;
        NameBox.Text = task.Name;
        TypeCombo.Text = string.IsNullOrWhiteSpace(task.Type) ? "pollDriver" : task.Type;
        DriverCombo.Text = task.DriverId ?? string.Empty;
        IntervalBox.Text = task.IntervalMs.ToString(CultureInfo.InvariantCulture);
        KvTableHelper.LoadStringDict(_paramRows, task.Parameters);
        ParamsJsonBox.Text = KvTableHelper.ToJson(_paramRows);
        UpdateHelpPanel();
        SetDirty(false);
        _suppressDirty = false;
    }

    private void Field_Changed(object sender, TextChangedEventArgs e)
    {
        if (_suppressDirty)
        {
            return;
        }

        SetDirty(true);
        if (ReferenceEquals(sender, NameBox) || ReferenceEquals(sender, IntervalBox))
        {
            UpdateHelpPanel();
        }
    }

    private void TypeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressDirty)
        {
            return;
        }

        SetDirty(true);
        UpdateHelpPanel();
    }

    private void TypeCombo_LostFocus(object sender, RoutedEventArgs e)
    {
        if (_suppressDirty)
        {
            return;
        }

        SetDirty(true);
        UpdateHelpPanel();
    }

    private void FieldCombo_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressDirty)
        {
            return;
        }

        SetDirty(true);
        UpdateHelpPanel();
    }

    private void FieldCombo_LostFocus(object sender, RoutedEventArgs e)
    {
        if (_suppressDirty)
        {
            return;
        }

        SetDirty(true);
        UpdateHelpPanel();
    }

    private void ParamGrid_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
    {
        if (_suppressDirty)
        {
            return;
        }

        SetDirty(true);
    }

    private void SetDirty(bool dirty)
    {
        _dirty = dirty;
        DirtyBadge.Text = dirty ? "已修改（未应用）" : string.Empty;
    }

    private void AddParam_Click(object sender, RoutedEventArgs e)
    {
        _paramRows.Add(new KvPairRow());
        SetDirty(true);
    }

    private void RemoveParam_Click(object sender, RoutedEventArgs e)
    {
        if (ParamGrid.SelectedItem is KvPairRow row)
        {
            _paramRows.Remove(row);
        }
        else if (_paramRows.Count > 0)
        {
            _paramRows.RemoveAt(_paramRows.Count - 1);
        }

        SetDirty(true);
    }

    private void SyncJson_Click(object sender, RoutedEventArgs e)
    {
        ParamsJsonBox.Text = KvTableHelper.ToJson(_paramRows);
        DebugUi.Log(LogBox, "已从表格同步 JSON 预览");
    }

    private void ParamsJson_LostFocus(object sender, RoutedEventArgs e)
    {
        try
        {
            KvTableHelper.LoadFromJsonObject(_paramRows, ParamsJsonBox.Text);
            SetDirty(true);
        }
        catch
        {
            // ignore while typing
        }
    }

    private void Preset_Click(object sender, RoutedEventArgs e)
    {
        var type = (TypeCombo.Text ?? "pollDriver").Trim();
        var preset = GetParameterPreset(type);
        KvTableHelper.LoadStringDict(_paramRows, preset);
        ParamsJsonBox.Text = KvTableHelper.ToJson(_paramRows);
        SetDirty(true);
        DebugUi.Log(LogBox, $"已填充类型「{type}」参数模板（{preset.Count} 项）");
        UpdateHelpPanel();
    }

    private void Validate_Click(object sender, RoutedEventArgs e)
    {
        var issues = ValidateDraft(out _);
        UpdateHelpPanel(issues);
        DebugUi.Log(LogBox, issues.Count == 0 ? "校验通过" : $"校验发现问题 {issues.Count} 条");
    }

    private void Apply_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var issues = ValidateDraft(out var draft);
            var blocking = issues.Where(i => i.StartsWith("[错误]", StringComparison.Ordinal)).ToList();
            if (blocking.Count > 0)
            {
                UpdateHelpPanel(issues);
                MessageBox.Show(this, string.Join(Environment.NewLine, blocking), "无法应用",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var warnings = issues.Where(i => i.StartsWith("[警告]", StringComparison.Ordinal)).ToList();
            if (warnings.Count > 0)
            {
                var go = MessageBox.Show(
                    this,
                    string.Join(Environment.NewLine, warnings) + "\n\n仍要应用？",
                    "校验警告",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);
                if (go != MessageBoxResult.Yes)
                {
                    return;
                }
            }

            var target = SelectedTask();
            if (target is null)
            {
                // create new if list empty / no selection
                if (_workspace.Setting.Tasks.Any(t =>
                        string.Equals(t.Name, draft.Name, StringComparison.OrdinalIgnoreCase)))
                {
                    throw new InvalidOperationException($"任务 Name 已存在: {draft.Name}");
                }

                _workspace.Setting.Tasks.Add(draft);
                target = draft;
            }
            else
            {
                if (!string.Equals(draft.Name, target.Name, StringComparison.OrdinalIgnoreCase)
                    && _workspace.Setting.Tasks.Any(t =>
                        string.Equals(t.Name, draft.Name, StringComparison.OrdinalIgnoreCase)))
                {
                    throw new InvalidOperationException($"任务 Name 已存在: {draft.Name}");
                }

                target.Name = draft.Name;
                target.Type = draft.Type;
                target.DriverId = draft.DriverId;
                target.IntervalMs = draft.IntervalMs;
                target.Parameters = draft.Parameters;
            }

            _editingOriginalName = target.Name;
            _preferredTaskName = target.Name;
            SetDirty(false);
            _onApplied?.Invoke();
            ReloadTaskList(target.Name);
            DebugUi.Log(LogBox, $"已应用到工作区内存: {target.Name}（需主窗口「保存」才落盘）");
            MessageBox.Show(this,
                $"已更新任务「{target.Name}」。\n请在主窗口执行「文件 → 保存」写入磁盘。",
                "应用成功",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            DebugUi.Log(LogBox, "应用失败: " + ex.Message);
            MessageBox.Show(this, ex.Message, "应用失败", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private List<string> ValidateDraft(out MdkSetting.TaskConfig draft)
    {
        var issues = new List<string>();
        var name = (NameBox.Text ?? string.Empty).Trim();
        var type = string.IsNullOrWhiteSpace(TypeCombo.Text) ? "pollDriver" : TypeCombo.Text.Trim();
        var driverId = (DriverCombo.Text ?? string.Empty).Trim();
        var intervalText = (IntervalBox.Text ?? string.Empty).Trim();

        if (string.IsNullOrWhiteSpace(name))
        {
            issues.Add("[错误] Name 不能为空。");
        }

        if (!int.TryParse(intervalText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var interval) || interval <= 0)
        {
            issues.Add("[错误] IntervalMs 必须为正整数。");
            interval = 100;
        }

        var typeKey = type.ToLowerInvariant();
        if (!RuntimeTaskFactory.IsSupported(type))
        {
            issues.Add($"[警告] 类型「{type}」当前进程未注册（可能依赖 plugins，运行时再加载）。");
        }

        var needsDriver = typeKey is "polldriver" or "poll" or "motion" or "motiontask";
        if (needsDriver)
        {
            if (string.IsNullOrWhiteSpace(driverId))
            {
                issues.Add("[错误] 该类型需要 DriverId。");
            }
            else if (!_workspace.Setting.Drivers.Any(d =>
                         string.Equals(d.Id, driverId, StringComparison.OrdinalIgnoreCase)))
            {
                issues.Add($"[错误] DriverId「{driverId}」不在当前 Drivers 列表中。");
            }
        }
        else if (!string.IsNullOrWhiteSpace(driverId)
                 && !_workspace.Setting.Drivers.Any(d =>
                     string.Equals(d.Id, driverId, StringComparison.OrdinalIgnoreCase)))
        {
            issues.Add($"[警告] DriverId「{driverId}」不在 Drivers 列表中（本类型通常可不填）。");
        }

        var parameters = KvTableHelper.ToStringDict(_paramRows);
        if (typeKey is "operation" or "taskoperation")
        {
            if (parameters.TryGetValue("gpioDeviceId", out var gpioId) && !string.IsNullOrWhiteSpace(gpioId))
            {
                if (!_workspace.Setting.Devices.Any(d =>
                        string.Equals(d.Id, gpioId, StringComparison.OrdinalIgnoreCase)
                        && string.Equals(d.Type, "gpio", StringComparison.OrdinalIgnoreCase)))
                {
                    issues.Add($"[警告] gpioDeviceId「{gpioId}」不是当前 setting 中的 gpio 设备。");
                }
            }
            else if (parameters.TryGetValue("deviceId", out var legacy) && !string.IsNullOrWhiteSpace(legacy))
            {
                issues.Add("[提示] 可用 parameters.gpioDeviceId；空则使用共享 GpioDevice（第一个 gpio）。");
            }
            else if (!_workspace.Setting.Devices.Any(d =>
                         string.Equals(d.Type, "gpio", StringComparison.OrdinalIgnoreCase)))
            {
                issues.Add("[警告] 当前 setting 没有 gpio 设备；operation 灯塔 IO 将不可用。");
            }
        }

        draft = new MdkSetting.TaskConfig
        {
            Name = name,
            Type = type,
            DriverId = driverId,
            IntervalMs = interval,
            Parameters = parameters,
        };
        return issues;
    }

    private void UpdateHelpPanel(IReadOnlyList<string>? issues = null)
    {
        var sb = new StringBuilder();
        var type = string.IsNullOrWhiteSpace(TypeCombo.Text) ? "pollDriver" : TypeCombo.Text.Trim();
        sb.AppendLine(DescribeType(type));
        sb.AppendLine();
        sb.AppendLine("字段");
        sb.AppendLine($"  Name       = {NameBox.Text}");
        sb.AppendLine($"  Type       = {type}");
        sb.AppendLine($"  DriverId   = {DriverCombo.Text}");
        sb.AppendLine($"  IntervalMs = {IntervalBox.Text}");
        sb.AppendLine($"  Params     = {_paramRows.Count} 行");
        sb.AppendLine();

        issues ??= ValidateDraft(out _);
        sb.AppendLine(issues.Count == 0 ? "校验：通过" : "校验：");
        foreach (var issue in issues)
        {
            sb.AppendLine("  " + issue);
        }

        HelpBox.Text = sb.ToString();
    }

    private static string DescribeType(string type) => type.Trim().ToLowerInvariant() switch
    {
        "polldriver" or "poll" =>
            "pollDriver\n  轮询驱动心跳，写入 vars。\n  必需：DriverId、IntervalMs。\n  可选参数：varPrefix。",
        "operation" or "taskoperation" =>
            "operation\n  处理 task.operation.command（start/stop/reset/lamp）。\n  gpioDeviceId 可空（默认共享 GpioDevice）。\n  DriverId 可空。",
        "cycle" or "taskcycle" =>
            "cycle\n  周期汇总运行时快照 / 任务状态。\n  主要：IntervalMs。DriverId 可空。",
        "motion" or "motiontask" =>
            "motion\n  运动任务基类默认实现（心跳 alive）。\n  必需：DriverId。\n  参数键值会写入任务 SetParam。",
        "pnpcycle" =>
            "pnpCycle\n  PNP 扩展周期任务（需 MDKOSS.Pnp 插件）。",
        "pnpconveyor" =>
            "pnpConveyor\n  PNP 传送带任务（需 MDKOSS.Pnp 插件）。",
        "flow" or "script" =>
            "flow\n  节点图流程任务。parameters.flowJson 存流程图。\n  请用菜单「调试 → Flow 流程编辑…」图形化编辑。",
        _ => $"类型「{type}」\n  若为扩展任务，请确认 plugins 已注册对应 RuntimeTaskFactory。",
    };

    private static Dictionary<string, string> GetParameterPreset(string type) =>
        type.Trim().ToLowerInvariant() switch
        {
            "polldriver" or "poll" => new(StringComparer.OrdinalIgnoreCase)
            {
                ["varPrefix"] = "driver",
            },
            "operation" or "taskoperation" => new(StringComparer.OrdinalIgnoreCase),
            "cycle" or "taskcycle" => new(StringComparer.OrdinalIgnoreCase)
            {
                ["note"] = "cycle uses IntervalMs; gpio optional",
            },
            "motion" or "motiontask" => new(StringComparer.OrdinalIgnoreCase)
            {
                ["axisDeviceId"] = "dev-axis",
            },
            "flow" or "script" => new(StringComparer.OrdinalIgnoreCase)
            {
                ["loop"] = "true",
                ["flowJson"] = FlowDocument.CreateEmpty().ToJson(),
            },
            "pnpcycle" => new(StringComparer.OrdinalIgnoreCase)
            {
                ["station"] = "1",
            },
            "pnpconveyor" => new(StringComparer.OrdinalIgnoreCase)
            {
                ["conveyorId"] = "conveyor-1",
            },
            _ => new(StringComparer.OrdinalIgnoreCase)
            {
                ["key"] = "value",
            },
        };
}
