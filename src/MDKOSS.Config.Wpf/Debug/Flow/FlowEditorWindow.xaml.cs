using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using MDKOSS.Core;
using MDKOSS.Core.Flow;

namespace MDKOSS.Config.Wpf.Debug.Flow;

/// <summary>
/// Workflow-style editor: vertical centered sequence, auto-wired connectors
/// (C# Workflow Foundation Sequence-like).
/// </summary>
public partial class FlowEditorWindow : Window
{
    private readonly ConfigWorkspace _workspace;
    private readonly Action? _onApplied;
    private readonly FlowEditorVm _vm = new();
    private readonly Dictionary<string, Border> _nodeVisuals = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<UIElement> _edgeVisuals = [];

    private string? _preferredTaskName;
    private bool _suppressTaskBind;

    public FlowEditorWindow(ConfigWorkspace workspace, string? preferredTaskName = null, Action? onApplied = null)
    {
        InitializeComponent();
        _workspace = workspace;
        _preferredTaskName = preferredTaskName;
        _onApplied = onApplied;
        DataContext = _vm;
        PreviewKeyDown += Window_PreviewKeyDown;
        Loaded += (_, _) =>
        {
            ReloadTaskList();
            RefreshCanvas();
        };
        SizeChanged += (_, _) =>
        {
            if (IsLoaded)
            {
                _vm.RelayoutAndAutoWire();
                RefreshCanvas();
            }
        };
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Delete && _vm.Selected is not null
            && !(Keyboard.FocusedElement is TextBox or DataGridCell))
        {
            DeleteNode_Click(sender, new RoutedEventArgs());
            e.Handled = true;
            return;
        }

        if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.Up)
        {
            MoveUp_Click(sender, new RoutedEventArgs());
            e.Handled = true;
        }
        else if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.Down)
        {
            MoveDown_Click(sender, new RoutedEventArgs());
            e.Handled = true;
        }
    }

    private void ReloadTaskList()
    {
        _suppressTaskBind = true;
        try
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

            prefer ??= TaskCombo.Items.Cast<ComboBoxItem>()
                .FirstOrDefault(i =>
                {
                    var name = i.Tag as string;
                    var task = _workspace.Setting.Tasks.FirstOrDefault(t =>
                        string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase));
                    return task is not null && string.Equals(task.Type, "flow", StringComparison.OrdinalIgnoreCase);
                });

            TaskCombo.SelectedItem = prefer ?? (TaskCombo.Items.Count > 0 ? TaskCombo.Items[0] : null);
        }
        finally
        {
            _suppressTaskBind = false;
        }

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

    private void TaskCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressTaskBind)
        {
            return;
        }

        BindSelectedTask();
    }

    private void BindSelectedTask()
    {
        var task = SelectedTask();
        if (task is null)
        {
            _vm.Load(FlowDocument.CreateEmpty());
            IntervalBox.Text = "100";
            RefreshCanvas();
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
        RefreshCanvas();
    }

    private void RefreshCanvas()
    {
        ClearCanvas();
        CenterLayoutOrigin();
        foreach (var node in _vm.Nodes)
        {
            AddNodeVisual(node);
        }

        DrawAutoConnectors();
        SizeCanvasToContent();
    }

    private void CenterLayoutOrigin()
    {
        // Recenter horizontal axis to current viewport when possible
        var viewW = GraphScroll.ViewportWidth > 0 ? GraphScroll.ViewportWidth : GraphCanvas.Width;
        if (viewW < 400)
        {
            viewW = 800;
        }

        // Layout uses fixed LayoutCenterX; shift all nodes if canvas wider
        // Keep VM constant; canvas is wide enough with center at 400.
    }

    private void ClearCanvas()
    {
        foreach (var e in _edgeVisuals)
        {
            GraphCanvas.Children.Remove(e);
        }

        _edgeVisuals.Clear();
        foreach (var kv in _nodeVisuals.ToList())
        {
            GraphCanvas.Children.Remove(kv.Value);
        }

        _nodeVisuals.Clear();
    }

    private void AddNodeVisual(FlowNodeVm node)
    {
        var isTerminal = string.Equals(node.Kind, FlowNodeKinds.Start, StringComparison.OrdinalIgnoreCase)
                         || string.Equals(node.Kind, FlowNodeKinds.End, StringComparison.OrdinalIgnoreCase);
        var summary = BuildPropSummary(node);

        var body = new StackPanel();
        body.Children.Add(new TextBlock
        {
            Text = node.Kind,
            FontWeight = FontWeights.SemiBold,
            FontSize = 13,
            HorizontalAlignment = HorizontalAlignment.Center,
            Foreground = (Brush)FindResource("AccentBrush"),
        });
        body.Children.Add(new TextBlock
        {
            Text = summary,
            FontSize = 11,
            HorizontalAlignment = HorizontalAlignment.Center,
            Foreground = (Brush)FindResource("MutedBrush"),
            TextTrimming = TextTrimming.CharacterEllipsis,
            TextWrapping = TextWrapping.NoWrap,
            MaxWidth = FlowEditorVm.LayoutNodeWidth - 24,
        });

        var border = new Border
        {
            Width = FlowEditorVm.LayoutNodeWidth,
            MinHeight = FlowEditorVm.LayoutNodeHeight,
            Background = isTerminal
                ? new SolidColorBrush(Color.FromRgb(0xE6, 0xF4, 0xEF))
                : Brushes.White,
            BorderBrush = node.BorderBrush,
            BorderThickness = new Thickness(2),
            CornerRadius = new CornerRadius(isTerminal ? 20 : 8),
            Padding = new Thickness(10, 8, 10, 8),
            Cursor = Cursors.Hand,
            Tag = node.Id,
            Child = body,
            ToolTip = $"{node.Kind}\n{node.Id}",
        };

        Canvas.SetLeft(border, node.X);
        Canvas.SetTop(border, node.Y);
        Panel.SetZIndex(border, 10);
        border.MouseLeftButtonDown += (_, e) =>
        {
            SelectNode(node);
            e.Handled = true;
        };

        GraphCanvas.Children.Add(border);
        _nodeVisuals[node.Id] = border;
    }

    private static string BuildPropSummary(FlowNodeVm node)
    {
        if (node.Props.Count == 0)
        {
            return node.Id;
        }

        var parts = node.Props
            .Where(p => !string.IsNullOrWhiteSpace(p.Key))
            .Take(3)
            .Select(p => $"{p.Key}={TrimVal(p.Value)}");
        return string.Join("  ", parts);
    }

    private static string TrimVal(string? v)
    {
        if (string.IsNullOrEmpty(v))
        {
            return "";
        }

        return v.Length <= 18 ? v : v[..16] + "…";
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

    private void DrawAutoConnectors()
    {
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

            var x1 = from.X + FlowEditorVm.LayoutNodeWidth / 2;
            var y1 = from.Y + FlowEditorVm.LayoutNodeHeight;
            var x2 = to.X + FlowEditorVm.LayoutNodeWidth / 2;
            var y2 = to.Y;

            var port = (edge.Port ?? FlowPorts.Next).Trim().ToLowerInvariant();
            Brush stroke = port switch
            {
                "true" or "body" => new SolidColorBrush(Color.FromRgb(0x0B, 0x6E, 0x4F)),
                "false" or "exit" => new SolidColorBrush(Color.FromRgb(0x9A, 0x67, 0x00)),
                _ => new SolidColorBrush(Color.FromRgb(0x65, 0x6D, 0x76)),
            };

            Path path;
            if (Math.Abs(x1 - x2) < 2)
            {
                // straight vertical
                var geo = new PathGeometry();
                var fig = new PathFigure { StartPoint = new Point(x1, y1), IsClosed = false };
                fig.Segments.Add(new LineSegment(new Point(x2, y2), true));
                geo.Figures.Add(fig);
                path = new Path
                {
                    Stroke = stroke,
                    StrokeThickness = 2,
                    Data = geo,
                    IsHitTestVisible = false,
                };
            }
            else
            {
                // elbow: down → horizontal → down (WF-like)
                var midY = (y1 + y2) / 2;
                var geo = new PathGeometry();
                var fig = new PathFigure { StartPoint = new Point(x1, y1), IsClosed = false };
                fig.Segments.Add(new LineSegment(new Point(x1, midY), true));
                fig.Segments.Add(new LineSegment(new Point(x2, midY), true));
                fig.Segments.Add(new LineSegment(new Point(x2, y2), true));
                geo.Figures.Add(fig);
                path = new Path
                {
                    Stroke = stroke,
                    StrokeThickness = 2,
                    Data = geo,
                    IsHitTestVisible = false,
                };
            }

            // arrow head
            var arrow = CreateArrowHead(x2, y2, stroke);
            Panel.SetZIndex(path, 1);
            Panel.SetZIndex(arrow, 2);
            GraphCanvas.Children.Insert(0, path);
            GraphCanvas.Children.Insert(0, arrow);
            _edgeVisuals.Add(path);
            _edgeVisuals.Add(arrow);

            if (port is not "next")
            {
                var label = new TextBlock
                {
                    Text = port,
                    FontSize = 10,
                    Foreground = stroke,
                    IsHitTestVisible = false,
                };
                Canvas.SetLeft(label, (x1 + x2) / 2 + 4);
                Canvas.SetTop(label, (y1 + y2) / 2 - 12);
                Panel.SetZIndex(label, 3);
                GraphCanvas.Children.Insert(0, label);
                _edgeVisuals.Add(label);
            }
        }
    }

    private static Polygon CreateArrowHead(double x, double y, Brush fill)
    {
        var poly = new Polygon
        {
            Fill = fill,
            Points = [new Point(x, y), new Point(x - 5, y - 10), new Point(x + 5, y - 10)],
            IsHitTestVisible = false,
        };
        return poly;
    }

    private void SizeCanvasToContent()
    {
        if (_vm.Nodes.Count == 0)
        {
            return;
        }

        var maxY = _vm.Nodes.Max(n => n.Y) + FlowEditorVm.LayoutNodeHeight + 80;
        var maxX = Math.Max(800, _vm.Nodes.Max(n => n.X) + FlowEditorVm.LayoutNodeWidth + 80);
        GraphCanvas.Width = Math.Max(maxX, FlowEditorVm.LayoutCenterX * 2);
        GraphCanvas.Height = Math.Max(600, maxY);
    }

    private void Canvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
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
        }
    }

    private void AddNode_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string kind })
        {
            return;
        }

        var node = _vm.InsertNode(kind);
        RefreshCanvas();
        SelectNode(node);
        ScrollToNode(node);
    }

    private void DeleteNode_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.Selected is null)
        {
            return;
        }

        if (string.Equals(_vm.Selected.Kind, FlowNodeKinds.Start, StringComparison.OrdinalIgnoreCase)
            || string.Equals(_vm.Selected.Kind, FlowNodeKinds.End, StringComparison.OrdinalIgnoreCase))
        {
            MessageBox.Show(this, "start / end 为流程端点，不能删除。", "提示",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        _vm.RemoveSelected();
        RefreshCanvas();
    }

    private void MoveUp_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.MoveSelected(-1))
        {
            var sel = _vm.Selected;
            RefreshCanvas();
            if (sel is not null)
            {
                SelectNode(sel);
            }
        }
    }

    private void MoveDown_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.MoveSelected(1))
        {
            var sel = _vm.Selected;
            RefreshCanvas();
            if (sel is not null)
            {
                SelectNode(sel);
            }
        }
    }

    private void Relayout_Click(object sender, RoutedEventArgs e)
    {
        _vm.RelayoutAndAutoWire();
        RefreshCanvas();
        _vm.RefreshPreview();
    }

    private void ScrollToNode(FlowNodeVm node)
    {
        GraphScroll.ScrollToVerticalOffset(Math.Max(0, node.Y - 80));
    }

    private void AddVar_Click(object sender, RoutedEventArgs e)
    {
        _vm.Variables.Add(new FlowVarVm { Name = "v" + (_vm.Variables.Count + 1), Type = "number", Init = "0" });
        _vm.RefreshPreview();
    }

    private void PropGrid_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e) =>
        Dispatcher.BeginInvoke(() =>
        {
            _vm.RefreshPreview();
            RefreshCanvas();
        });

    private void MetaGrid_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e) =>
        Dispatcher.BeginInvoke(() => _vm.RefreshPreview());

    private void Validate_Click(object sender, RoutedEventArgs e)
    {
        _vm.RelayoutAndAutoWire();
        RefreshCanvas();
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
            _vm.RelayoutAndAutoWire();
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
}
