using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using MDKOSS.Core;
using MDKOSS.Core.Flow;

namespace MDKOSS.Config.Wpf.Debug.Flow;

public partial class FlowEditorWindow : Window
{
    private const double NodeWidth = 140;
    private const double NodeHeight = 56;

    private readonly ConfigWorkspace _workspace;
    private readonly Action? _onApplied;
    private readonly FlowEditorVm _vm = new();
    private readonly Dictionary<string, Border> _nodeVisuals = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<Path> _edgePaths = [];

    private string? _preferredTaskName;
    private FlowNodeVm? _dragNode;
    private Point _dragOffset;
    private string? _pendingFromId;
    private string? _pendingPort;

    public FlowEditorWindow(ConfigWorkspace workspace, string? preferredTaskName = null, Action? onApplied = null)
    {
        InitializeComponent();
        _workspace = workspace;
        _preferredTaskName = preferredTaskName;
        _onApplied = onApplied;
        DataContext = _vm;
        Loaded += (_, _) =>
        {
            ReloadTaskList();
            RebuildNodeVisuals();
            UpdateEdgeGeometries();
        };
    }

    private void ReloadTaskList()
    {
        TaskCombo.Items.Clear();
        foreach (var t in _workspace.Setting.Tasks.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase))
        {
            TaskCombo.Items.Add(new ComboBoxItem
            {
                Content = $"{t.Name}  [{t.Type}]",
                Tag = t.Name,
            });
        }

        ComboBoxItem? prefer = null;
        if (!string.IsNullOrWhiteSpace(_preferredTaskName))
        {
            prefer = TaskCombo.Items.Cast<ComboBoxItem>()
                .FirstOrDefault(i => string.Equals(i.Tag as string, _preferredTaskName, StringComparison.OrdinalIgnoreCase));
        }

        // prefer existing flow task
        prefer ??= TaskCombo.Items.Cast<ComboBoxItem>()
            .FirstOrDefault(i =>
            {
                var name = i.Tag as string;
                var task = _workspace.Setting.Tasks.FirstOrDefault(t =>
                    string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase));
                return task is not null && string.Equals(task.Type, "flow", StringComparison.OrdinalIgnoreCase);
            });

        TaskCombo.SelectedItem = prefer ?? (TaskCombo.Items.Count > 0 ? TaskCombo.Items[0] : null);
        BindSelectedTask();
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

    private void TaskCombo_SelectionChanged(object sender, SelectionChangedEventArgs e) => BindSelectedTask();

    private void BindSelectedTask()
    {
        var task = SelectedTask();
        if (task is null)
        {
            _vm.Load(FlowDocument.CreateEmpty());
            IntervalBox.Text = "100";
            RebuildNodeVisuals();
            UpdateEdgeGeometries();
            return;
        }

        _preferredTaskName = task.Name;
        IntervalBox.Text = task.IntervalMs.ToString(CultureInfo.InvariantCulture);
        FlowDocument doc;
        if (task.Parameters.TryGetValue("flowJson", out var json) && !string.IsNullOrWhiteSpace(json)
            && FlowDocument.TryParse(json, out var parsed, out _))
        {
            doc = parsed;
        }
        else
        {
            doc = FlowDocument.CreateEmpty();
        }

        _vm.Load(doc);
        // sync main entry
        var start = _vm.Nodes.FirstOrDefault(n => n.Kind == FlowNodeKinds.Start);
        if (start is not null)
        {
            var main = _vm.Functions.FirstOrDefault(f =>
                string.Equals(f.Name, "main", StringComparison.OrdinalIgnoreCase));
            if (main is not null && string.IsNullOrWhiteSpace(main.EntryNodeId))
            {
                main.EntryNodeId = start.Id;
            }
        }

        RebuildNodeVisuals();
        UpdateEdgeGeometries();
        _vm.RefreshPreview();
    }

    private void RebuildNodeVisuals()
    {
        foreach (var p in _edgePaths)
        {
            GraphCanvas.Children.Remove(p);
            if (p.Tag is UIElement label)
            {
                GraphCanvas.Children.Remove(label);
            }
        }

        _edgePaths.Clear();

        foreach (var kv in _nodeVisuals.ToList())
        {
            GraphCanvas.Children.Remove(kv.Value);
        }

        _nodeVisuals.Clear();
        foreach (var node in _vm.Nodes)
        {
            AddNodeVisual(node);
        }
    }

    private void AddNodeVisual(FlowNodeVm node)
    {
        var border = new Border
        {
            Width = NodeWidth,
            Height = NodeHeight,
            Background = Brushes.White,
            BorderBrush = node.BorderBrush,
            BorderThickness = new Thickness(2),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(8, 4, 8, 4),
            Cursor = Cursors.SizeAll,
            Tag = node.Id,
            Child = new StackPanel
            {
                Children =
                {
                    new TextBlock
                    {
                        Text = node.Kind,
                        FontWeight = FontWeights.SemiBold,
                        Foreground = (Brush)FindResource("AccentBrush"),
                    },
                    new TextBlock
                    {
                        Text = node.Id,
                        FontSize = 10,
                        Foreground = (Brush)FindResource("MutedBrush"),
                    },
                },
            },
        };

        Canvas.SetLeft(border, node.X);
        Canvas.SetTop(border, node.Y);
        Panel.SetZIndex(border, 10);

        border.MouseLeftButtonDown += Node_MouseLeftButtonDown;
        GraphCanvas.Children.Add(border);
        _nodeVisuals[node.Id] = border;
    }

    private void Node_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Border { Tag: string id })
        {
            return;
        }

        var node = _vm.Nodes.FirstOrDefault(n => string.Equals(n.Id, id, StringComparison.OrdinalIgnoreCase));
        if (node is null)
        {
            return;
        }

        // completing a link?
        if (_pendingFromId is not null && _pendingPort is not null
            && !string.Equals(_pendingFromId, id, StringComparison.OrdinalIgnoreCase))
        {
            _vm.Connect(_pendingFromId, _pendingPort, id);
            ClearPendingLink();
            UpdateEdgeGeometries();
            e.Handled = true;
            return;
        }

        SelectNode(node);
        _dragNode = node;
        var pos = e.GetPosition(GraphCanvas);
        _dragOffset = new Point(pos.X - node.X, pos.Y - node.Y);
        borderCapture(sender as Border);
        e.Handled = true;
    }

    private void borderCapture(Border? border)
    {
        border?.CaptureMouse();
    }

    private void SelectNode(FlowNodeVm node)
    {
        foreach (var n in _vm.Nodes)
        {
            n.IsSelected = ReferenceEquals(n, node);
            if (_nodeVisuals.TryGetValue(n.Id, out var b))
            {
                b.BorderBrush = n.BorderBrush;
            }
        }

        _vm.Selected = node;
    }

    private void Canvas_MouseMove(object sender, MouseEventArgs e)
    {
        if (_dragNode is null || e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        var pos = e.GetPosition(GraphCanvas);
        _dragNode.X = Math.Max(0, pos.X - _dragOffset.X);
        _dragNode.Y = Math.Max(0, pos.Y - _dragOffset.Y);
        if (_nodeVisuals.TryGetValue(_dragNode.Id, out var border))
        {
            Canvas.SetLeft(border, _dragNode.X);
            Canvas.SetTop(border, _dragNode.Y);
        }

        UpdateEdgeGeometries();
    }

    private void Canvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_dragNode is not null && _nodeVisuals.TryGetValue(_dragNode.Id, out var border))
        {
            border.ReleaseMouseCapture();
        }

        _dragNode = null;
        _vm.RefreshPreview();
    }

    private void Canvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        // click empty canvas clears selection (unless on node which handles)
        if (e.OriginalSource is Canvas)
        {
            foreach (var n in _vm.Nodes)
            {
                n.IsSelected = false;
                if (_nodeVisuals.TryGetValue(n.Id, out var b))
                {
                    b.BorderBrush = n.BorderBrush;
                }
            }

            _vm.Selected = null;
            ClearPendingLink();
        }
    }

    private void AddNode_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string kind })
        {
            return;
        }

        var at = new Point(120 + _vm.Nodes.Count * 24, 100 + (_vm.Nodes.Count % 8) * 70);
        var node = _vm.AddNode(kind, at);
        if (kind == FlowNodeKinds.Start)
        {
            var main = _vm.Functions.FirstOrDefault(f =>
                string.Equals(f.Name, "main", StringComparison.OrdinalIgnoreCase));
            if (main is not null)
            {
                main.EntryNodeId = node.Id;
            }
        }

        AddNodeVisual(node);
        SelectNode(node);
        UpdateEdgeGeometries();
    }

    private void DeleteNode_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.Selected is null)
        {
            return;
        }

        var id = _vm.Selected.Id;
        if (_nodeVisuals.TryGetValue(id, out var border))
        {
            GraphCanvas.Children.Remove(border);
            _nodeVisuals.Remove(id);
        }

        _vm.RemoveSelected();
        UpdateEdgeGeometries();
    }

    private void PortButton_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.Selected is null || sender is not Button { Tag: string port })
        {
            return;
        }

        _pendingFromId = _vm.Selected.Id;
        _pendingPort = port;
        LinkHint.Text = $"连线中：从 {_pendingFromId}.{port} → 点击目标节点";
    }

    private void ClearPendingLink()
    {
        _pendingFromId = null;
        _pendingPort = null;
        LinkHint.Text = string.Empty;
    }

    private void AddVar_Click(object sender, RoutedEventArgs e)
    {
        _vm.Variables.Add(new FlowVarVm { Name = "v" + (_vm.Variables.Count + 1), Type = "number", Init = "0" });
        _vm.RefreshPreview();
    }

    private void PropGrid_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e) =>
        Dispatcher.BeginInvoke(() => _vm.RefreshPreview());

    private void MetaGrid_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e) =>
        Dispatcher.BeginInvoke(() => _vm.RefreshPreview());

    private void Validate_Click(object sender, RoutedEventArgs e)
    {
        SyncMainEntry();
        _vm.RefreshPreview();
        MessageBox.Show(this, _vm.ValidationText, "校验", MessageBoxButton.OK,
            _vm.ValidationText.StartsWith("校验通过", StringComparison.Ordinal)
                ? MessageBoxImage.Information
                : MessageBoxImage.Warning);
    }

    private void Apply_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            SyncMainEntry();
            var doc = _vm.ToDocument();
            var errors = doc.Validate();
            if (errors.Count > 0)
            {
                _vm.RefreshPreview();
                var go = MessageBox.Show(
                    this,
                    "校验未通过：\n" + string.Join("\n", errors) + "\n\n仍要保存？",
                    "校验警告",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);
                if (go != MessageBoxResult.Yes)
                {
                    return;
                }
            }

            if (!int.TryParse(IntervalBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var interval)
                || interval <= 0)
            {
                throw new InvalidOperationException("IntervalMs 必须为正整数。");
            }

            var task = SelectedTask();
            var json = doc.ToJson();
            if (task is null)
            {
                var name = "task-flow";
                var n = 1;
                while (_workspace.Setting.Tasks.Any(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase)))
                {
                    name = $"task-flow-{n++}";
                }

                task = new MdkSetting.TaskConfig
                {
                    Name = name,
                    Type = "flow",
                    IntervalMs = interval,
                    Parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["flowJson"] = json,
                        ["loop"] = "true",
                    },
                };
                _workspace.Setting.Tasks.Add(task);
            }
            else
            {
                task.Type = "flow";
                task.IntervalMs = interval;
                task.Parameters["flowJson"] = json;
                if (!task.Parameters.ContainsKey("loop"))
                {
                    task.Parameters["loop"] = "true";
                }
            }

            _preferredTaskName = task.Name;
            _onApplied?.Invoke();
            ReloadTaskList();
            MessageBox.Show(this,
                $"已写入任务「{task.Name}」的 parameters.flowJson。\n请在主窗口「文件 → 保存」落盘。",
                "应用成功",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "应用失败", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void SyncMainEntry()
    {
        var start = _vm.Nodes.FirstOrDefault(n =>
            string.Equals(n.Kind, FlowNodeKinds.Start, StringComparison.OrdinalIgnoreCase));
        var main = _vm.Functions.FirstOrDefault(f =>
            string.Equals(f.Name, "main", StringComparison.OrdinalIgnoreCase));
        if (main is null)
        {
            _vm.Functions.Add(new FlowFuncVm { Name = "main", EntryNodeId = start?.Id ?? "" });
        }
        else if (start is not null && string.IsNullOrWhiteSpace(main.EntryNodeId))
        {
            main.EntryNodeId = start.Id;
        }
    }

    private void UpdateEdgeGeometries()
    {
        foreach (var p in _edgePaths)
        {
            GraphCanvas.Children.Remove(p);
            if (p.Tag is UIElement label)
            {
                GraphCanvas.Children.Remove(label);
            }
        }

        _edgePaths.Clear();

        foreach (var edge in _vm.Edges)
        {
            var from = _vm.Nodes.FirstOrDefault(n =>
                string.Equals(n.Id, edge.From, StringComparison.OrdinalIgnoreCase));
            var to = _vm.Nodes.FirstOrDefault(n =>
                string.Equals(n.Id, edge.To, StringComparison.OrdinalIgnoreCase));
            if (from is null || to is null)
            {
                continue;
            }

            var x1 = from.X + NodeWidth;
            var y1 = from.Y + NodeHeight / 2;
            var x2 = to.X;
            var y2 = to.Y + NodeHeight / 2;
            var dx = Math.Max(40, Math.Abs(x2 - x1) * 0.4);
            var fig = new PathFigure { StartPoint = new Point(x1, y1), IsClosed = false };
            fig.Segments.Add(new BezierSegment(
                new Point(x1 + dx, y1),
                new Point(x2 - dx, y2),
                new Point(x2, y2),
                true));
            var geo = new PathGeometry([fig]);
            var path = new Path
            {
                Stroke = new SolidColorBrush(Color.FromRgb(0x65, 0x6D, 0x76)),
                StrokeThickness = 2,
                Data = geo,
                IsHitTestVisible = false,
            };
            Panel.SetZIndex(path, 1);
            GraphCanvas.Children.Insert(0, path);
            _edgePaths.Add(path);

            // port label near midpoint
            var label = new TextBlock
            {
                Text = edge.Port,
                FontSize = 10,
                Foreground = new SolidColorBrush(Color.FromRgb(0x65, 0x6D, 0x76)),
                IsHitTestVisible = false,
            };
            Canvas.SetLeft(label, (x1 + x2) / 2);
            Canvas.SetTop(label, (y1 + y2) / 2 - 10);
            Panel.SetZIndex(label, 2);
            GraphCanvas.Children.Insert(0, label);
            // track for cleanup via same list using a wrapper — remove labels with paths
            // store label as Tag on path for cleanup
            path.Tag = label;
        }
    }
}
