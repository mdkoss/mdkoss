using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Microsoft.Win32;
using MDKOSS.Core;
using MDKOSS.Core.Vision;

namespace MDKOSS.Config.Wpf.Debug.Vision;

public partial class VisionEditorWindow : Window
{
    private readonly ConfigWorkspace _workspace;
    private readonly Action? _onApplied;
    private readonly VisionEditorVm _vm = new();
    private readonly Dictionary<string, Border> _nodeVisuals = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<UIElement> _edgeVisuals = [];

    private string? _preferredVisionId;
    private bool _suppressBind;

    public VisionEditorWindow(ConfigWorkspace workspace, string? preferredVisionId = null, Action? onApplied = null)
    {
        InitializeComponent();
        _workspace = workspace;
        _preferredVisionId = preferredVisionId;
        _onApplied = onApplied;
        DataContext = _vm;
        PreviewKeyDown += Window_PreviewKeyDown;
        Loaded += (_, _) =>
        {
            ReloadVisionList();
            RefreshCanvas();
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

    private void ReloadVisionList()
    {
        _suppressBind = true;
        try
        {
            VisionCombo.Items.Clear();
            foreach (var v in _workspace.Setting.Visions.OrderBy(x => x.Id, StringComparer.OrdinalIgnoreCase))
            {
                VisionCombo.Items.Add(new ComboBoxItem
                {
                    Content = string.IsNullOrWhiteSpace(v.Name) ? v.Id : $"{v.Id} · {v.Name}",
                    Tag = v.Id,
                });
            }

            ComboBoxItem? prefer = null;
            if (!string.IsNullOrWhiteSpace(_preferredVisionId))
            {
                prefer = VisionCombo.Items.Cast<ComboBoxItem>()
                    .FirstOrDefault(i => string.Equals(i.Tag as string, _preferredVisionId, StringComparison.OrdinalIgnoreCase));
            }

            VisionCombo.SelectedItem = prefer ?? (VisionCombo.Items.Count > 0 ? VisionCombo.Items[0] : null);
        }
        finally
        {
            _suppressBind = false;
        }

        BindSelectedVision();
    }

    private MdkSetting.VisionConfig? SelectedVision()
    {
        if (VisionCombo.SelectedItem is ComboBoxItem { Tag: string id })
        {
            return _workspace.Setting.Visions.FirstOrDefault(v =>
                string.Equals(v.Id, id, StringComparison.OrdinalIgnoreCase));
        }

        return null;
    }

    private void VisionCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressBind)
        {
            return;
        }

        BindSelectedVision();
    }

    private void BindSelectedVision()
    {
        var vision = SelectedVision();
        if (vision is null)
        {
            _vm.Load(VisionDocument.CreateEmpty());
            RefreshCanvas();
            return;
        }

        _preferredVisionId = vision.Id;
        VisionDocument doc;
        if (vision.Pipeline is not null && vision.Pipeline.Nodes.Count > 0)
        {
            doc = vision.Pipeline;
        }
        else if (!string.IsNullOrWhiteSpace(vision.PipelineJson)
            && VisionDocument.TryParse(vision.PipelineJson, out var parsed, out _))
        {
            doc = parsed;
        }
        else
        {
            doc = VisionDocument.CreateEmpty();
        }

        _vm.Load(doc, vision.CameraDeviceId);
        RefreshCanvas();
    }

    private void RefreshCanvas()
    {
        ClearCanvas();
        foreach (var node in _vm.Nodes)
        {
            AddNodeVisual(node);
        }

        DrawConnectors();
        SizeCanvasToContent();
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

    private void AddNodeVisual(VisionNodeVm node)
    {
        var isTerminal = VisionNodeKinds.IsTerminal(node.Kind);
        var summary = BuildPropSummary(node);
        var body = new StackPanel();
        body.Children.Add(new TextBlock
        {
            Text = node.Kind.StartsWith("vision.", StringComparison.OrdinalIgnoreCase)
                ? node.Kind["vision.".Length..]
                : node.Kind,
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
            MaxWidth = VisionEditorVm.LayoutNodeWidth - 24,
        });

        var border = new Border
        {
            Width = VisionEditorVm.LayoutNodeWidth,
            MinHeight = VisionEditorVm.LayoutNodeHeight,
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

    private static string BuildPropSummary(VisionNodeVm node)
    {
        if (node.Props.Count == 0)
        {
            return node.Id;
        }

        return string.Join("  ", node.Props
            .Where(p => !string.IsNullOrWhiteSpace(p.Key))
            .Take(3)
            .Select(p => $"{p.Key}={TrimVal(p.Value)}"));
    }

    private static string TrimVal(string? v)
    {
        if (string.IsNullOrEmpty(v))
        {
            return "";
        }

        return v.Length <= 18 ? v : v[..16] + "…";
    }

    private void DrawConnectors()
    {
        foreach (var edge in _vm.Edges)
        {
            if (!_nodeVisuals.TryGetValue(edge.From, out var from)
                || !_nodeVisuals.TryGetValue(edge.To, out var to))
            {
                continue;
            }

            var x1 = Canvas.GetLeft(from) + from.Width / 2;
            var y1 = Canvas.GetTop(from) + from.ActualHeight;
            if (from.ActualHeight <= 0)
            {
                y1 = Canvas.GetTop(from) + VisionEditorVm.LayoutNodeHeight;
            }

            var x2 = Canvas.GetLeft(to) + to.Width / 2;
            var y2 = Canvas.GetTop(to);

            var line = new Line
            {
                X1 = x1,
                Y1 = y1,
                X2 = x2,
                Y2 = y2,
                Stroke = new SolidColorBrush(Color.FromRgb(0x8B, 0x94, 0x9E)),
                StrokeThickness = 1.5,
                IsHitTestVisible = false,
            };
            Panel.SetZIndex(line, 1);
            GraphCanvas.Children.Add(line);
            _edgeVisuals.Add(line);
        }
    }

    private void SizeCanvasToContent()
    {
        var maxY = _vm.Nodes.Count == 0
            ? 400
            : _vm.Nodes.Max(n => n.Y) + VisionEditorVm.LayoutNodeHeight + 80;
        GraphCanvas.Height = Math.Max(600, maxY);
        GraphCanvas.Width = Math.Max(700, VisionEditorVm.LayoutCenterX + VisionEditorVm.LayoutNodeWidth);
    }

    private void SelectNode(VisionNodeVm node)
    {
        _vm.Selected = node;
        RefreshCanvas();
    }

    private void Canvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _vm.Selected = null;
        RefreshCanvas();
    }

    private void AddNode_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string kind })
        {
            return;
        }

        _vm.InsertNode(kind);
        RefreshCanvas();
    }

    private void DeleteNode_Click(object sender, RoutedEventArgs e)
    {
        _vm.RemoveSelected();
        RefreshCanvas();
    }

    private void MoveUp_Click(object sender, RoutedEventArgs e)
    {
        _vm.MoveSelected(-1);
        RefreshCanvas();
    }

    private void MoveDown_Click(object sender, RoutedEventArgs e)
    {
        _vm.MoveSelected(1);
        RefreshCanvas();
    }

    private void Relayout_Click(object sender, RoutedEventArgs e)
    {
        _vm.RelayoutAndAutoWire();
        _vm.RefreshPreview();
        RefreshCanvas();
    }

    private void Validate_Click(object sender, RoutedEventArgs e)
    {
        _vm.RefreshPreview();
        var errors = _vm.Validate();
        MessageBox.Show(
            this,
            errors.Count == 0 ? "校验通过。" : string.Join(Environment.NewLine, errors),
            "视觉流程校验",
            MessageBoxButton.OK,
            errors.Count == 0 ? MessageBoxImage.Information : MessageBoxImage.Warning);
    }

    private void TryRun_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title = "选择试运行输入图像",
            Filter = "Images|*.png;*.jpg;*.jpeg;*.bmp;*.tif;*.tiff|All|*.*",
        };
        if (dlg.ShowDialog(this) != true)
        {
            return;
        }

        var doc = _vm.ToDocument();
        var debugPath = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "mdkoss-vision-debug.png");
        var result = new VisionExecutor().Run(doc, dlg.FileName, debugPath);
        var lines = new List<string>();
        if (!string.IsNullOrWhiteSpace(result.Error))
        {
            lines.Add("ERROR: " + result.Error);
        }

        lines.Add($"ok={result.Ok} pose.ok={result.Pose.Ok} x={result.Pose.X:F2} y={result.Pose.Y:F2} ang={result.Pose.AngleDeg:F2} score={result.Pose.Score:F4}");
        lines.AddRange(result.Log);
        if (!string.IsNullOrWhiteSpace(result.DebugImagePath) && System.IO.File.Exists(result.DebugImagePath))
        {
            lines.Add($"debugImage={result.DebugImagePath}");
        }

        _vm.SetStatusText(string.Join(Environment.NewLine, lines));
        MessageBox.Show(
            this,
            result.Ok ? "试运行完成。" : ("试运行失败：\n" + (result.Error ?? "unknown")),
            "视觉试运行",
            MessageBoxButton.OK,
            result.Ok ? MessageBoxImage.Information : MessageBoxImage.Warning);
    }

    private void Apply_Click(object sender, RoutedEventArgs e)
    {
        var vision = SelectedVision();
        if (vision is null)
        {
            MessageBox.Show(this, "请先选择或新建一个 Vision 配置项。", "应用", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var errors = _vm.Validate();
        if (errors.Count > 0)
        {
            MessageBox.Show(this, string.Join(Environment.NewLine, errors), "校验失败", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        vision.CameraDeviceId = _vm.CameraDeviceId?.Trim() ?? "";
        vision.Pipeline = _vm.ToDocument();
        _workspace.NotifyExternalEdit($"视觉流程已更新 · {vision.Id}");
        _onApplied?.Invoke();
        MessageBox.Show(this, $"已写入工作区：{vision.Id}\n请在主窗口「文件 → 保存」落盘。", "应用", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void PropGrid_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e) =>
        Dispatcher.BeginInvoke(() =>
        {
            _vm.RefreshPreview();
            RefreshCanvas();
        });
}
