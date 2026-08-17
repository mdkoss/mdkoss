using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
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
    private bool _suppressAlgorithm;

    private bool _roiDragging;
    private bool _roiMoving;
    private Point _roiStartHost;
    private VisionRoiRect _roiStartRect;
    private VisionRoiRect _roiDraft;

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
            ReloadAlgorithmList();
            ReloadVisionList();
            RefreshCanvas();
            RefreshRoiOverlay();
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

    private void ReloadAlgorithmList()
    {
        _suppressAlgorithm = true;
        try
        {
            AlgorithmCombo.Items.Clear();
            foreach (var backend in VisionAlgorithmRegistry.List())
            {
                var label = backend.IsAvailable
                    ? backend.DisplayName
                    : $"{backend.DisplayName} (未安装)";
                AlgorithmCombo.Items.Add(new ComboBoxItem
                {
                    Content = label,
                    Tag = backend.Id,
                    IsEnabled = backend.IsAvailable || string.Equals(backend.Id, HalconVisionBackend.BackendId, StringComparison.OrdinalIgnoreCase),
                });
            }

            SelectAlgorithmInCombo(_vm.Algorithm);
        }
        finally
        {
            _suppressAlgorithm = false;
        }
    }

    private void SelectAlgorithmInCombo(string? algorithmId)
    {
        var id = string.IsNullOrWhiteSpace(algorithmId)
            ? VisionAlgorithmRegistry.DefaultId
            : algorithmId.Trim();
        var match = AlgorithmCombo.Items.Cast<ComboBoxItem>()
            .FirstOrDefault(i => string.Equals(i.Tag as string, id, StringComparison.OrdinalIgnoreCase));
        AlgorithmCombo.SelectedItem = match
            ?? AlgorithmCombo.Items.Cast<ComboBoxItem>()
                .FirstOrDefault(i => string.Equals(i.Tag as string, VisionAlgorithmRegistry.DefaultId, StringComparison.OrdinalIgnoreCase))
            ?? (AlgorithmCombo.Items.Count > 0 ? AlgorithmCombo.Items[0] : null);
    }

    private void AlgorithmCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressAlgorithm)
        {
            return;
        }

        if (AlgorithmCombo.SelectedItem is ComboBoxItem { Tag: string id })
        {
            _vm.Algorithm = id;
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
            _suppressAlgorithm = true;
            try { SelectAlgorithmInCombo(_vm.Algorithm); }
            finally { _suppressAlgorithm = false; }
            RefreshCanvas();
            RefreshRoiOverlay();
            return;
        }

        _preferredVisionId = vision.Id;
        var doc = vision.Pipeline is { Nodes.Count: > 0 }
            ? vision.Pipeline
            : vision.EffectivePipeline;
        if (doc.Nodes.Count == 0)
        {
            doc = VisionDocument.CreateEmpty();
        }

        _vm.Load(doc, vision.CameraDeviceId);
        _suppressAlgorithm = true;
        try { SelectAlgorithmInCombo(_vm.Algorithm); }
        finally { _suppressAlgorithm = false; }
        RefreshCanvas();
        RefreshRoiOverlay();
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
        var drawn = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var edge in _vm.Edges)
        {
            var pairKey = $"{edge.From}->{edge.To}";
            if (!drawn.Add(pairKey))
            {
                continue;
            }

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
        ShowSelectedNodeImage(preferOutput: true);
        RefreshRoiOverlay();
    }

    private void Canvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _vm.Selected = null;
        RefreshCanvas();
        RefreshRoiOverlay();
    }

    private void AddNode_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string kind })
        {
            return;
        }

        _vm.InsertNode(kind);
        RefreshCanvas();
        RefreshRoiOverlay();
    }

    private void DeleteNode_Click(object sender, RoutedEventArgs e)
    {
        _vm.RemoveSelected();
        RefreshCanvas();
        RefreshRoiOverlay();
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

    private void LoadPreview_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title = "选择预览 / ROI 底图",
            Filter = "Images|*.png;*.jpg;*.jpeg;*.bmp;*.tif;*.tiff|All|*.*",
        };
        if (dlg.ShowDialog(this) != true)
        {
            return;
        }

        ShowPreviewImage(dlg.FileName);
        _vm.SetStatusText("已载入预览图，可拖拽 ROI：" + dlg.FileName);
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
        var traceDir = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "mdkoss-vision-trace",
            DateTime.Now.ToString("yyyyMMdd-HHmmss"));
        var result = new VisionExecutor().Run(doc, new VisionRunRequest
        {
            InputImagePath = dlg.FileName,
            DebugImagePath = debugPath,
            KeepIntermediates = true,
            TraceDirectory = traceDir,
        });
        _vm.ApplyRunResult(result);
        var lines = new List<string>();
        lines.Add($"algorithm={doc.Algorithm}");
        if (!string.IsNullOrWhiteSpace(result.Error))
        {
            lines.Add("ERROR: " + result.Error);
        }

        lines.Add($"ok={result.Ok} pose.ok={result.Pose.Ok} x={result.Pose.X:F2} y={result.Pose.Y:F2} ang={result.Pose.AngleDeg:F2} score={result.Pose.Score:F4}");
        lines.AddRange(result.Log);
        if (!string.IsNullOrWhiteSpace(result.DebugImagePath) && System.IO.File.Exists(result.DebugImagePath))
        {
            lines.Add($"debugImage={result.DebugImagePath}");
            ShowPreviewImage(result.DebugImagePath);
        }
        else
        {
            ShowPreviewImage(dlg.FileName);
        }

        _vm.SetStatusText(string.Join(Environment.NewLine, lines));
        if (_vm.Selected is not null)
        {
            ShowSelectedNodeImage(preferOutput: true);
        }

        MessageBox.Show(
            this,
            result.Ok ? "试运行完成，效果图已更新。" : ("试运行失败：\n" + (result.Error ?? "unknown")),
            "视觉试运行",
            MessageBoxButton.OK,
            result.Ok ? MessageBoxImage.Information : MessageBoxImage.Warning);
    }

    private void ShowNodeInput_Click(object sender, RoutedEventArgs e) => ShowSelectedNodeImage(preferOutput: false);

    private void ShowNodeOutput_Click(object sender, RoutedEventArgs e) => ShowSelectedNodeImage(preferOutput: true);

    private void ShowSelectedNodeImage(bool preferOutput)
    {
        var trace = _vm.SelectedTrace;
        var path = preferOutput ? trace?.OutputImagePath : trace?.InputImagePath;
        if (string.IsNullOrWhiteSpace(path) || !System.IO.File.Exists(path))
        {
            path = preferOutput ? trace?.InputImagePath : trace?.OutputImagePath;
        }

        if (!string.IsNullOrWhiteSpace(path) && System.IO.File.Exists(path))
        {
            ShowPreviewImage(path);
        }

        RefreshRoiOverlay();
    }

    private void ShowPreviewImage(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !System.IO.File.Exists(path))
        {
            return;
        }

        var bmp = LoadBitmap(path);
        if (bmp is null)
        {
            return;
        }

        ResultImage.Source = bmp;
        PreviewEmptyHint.Visibility = Visibility.Collapsed;
        _vm.SetPreviewImage(path, bmp.PixelWidth, bmp.PixelHeight);
        RefreshRoiOverlay();
    }

    private static BitmapImage? LoadBitmap(string path)
    {
        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
            bmp.UriSource = new Uri(path, UriKind.Absolute);
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        catch
        {
            return null;
        }
    }

    private void PreviewHost_SizeChanged(object sender, SizeChangedEventArgs e) => RefreshRoiOverlay();

    private void RefreshRoiOverlay()
    {
        if (_vm.PreviewImageWidth <= 0 || _vm.PreviewImageHeight <= 0 || ResultImage.Source is null)
        {
            RoiRectVisual.Visibility = Visibility.Collapsed;
            return;
        }

        var rect = _roiDragging ? _roiDraft : _vm.TryGetRoiRect();
        if (rect is null)
        {
            RoiRectVisual.Visibility = Visibility.Collapsed;
            return;
        }

        if (!TryMapImageRectToHost(rect.Value, out var hostRect))
        {
            RoiRectVisual.Visibility = Visibility.Collapsed;
            return;
        }

        Canvas.SetLeft(RoiRectVisual, hostRect.X);
        Canvas.SetTop(RoiRectVisual, hostRect.Y);
        RoiRectVisual.Width = Math.Max(1, hostRect.Width);
        RoiRectVisual.Height = Math.Max(1, hostRect.Height);
        RoiRectVisual.Visibility = Visibility.Visible;
    }

    private bool TryGetImageLayout(out Rect imageInHost, out double scale)
    {
        imageInHost = default;
        scale = 1;
        if (ResultImage.Source is not BitmapSource bmp || bmp.PixelWidth <= 0 || bmp.PixelHeight <= 0)
        {
            return false;
        }

        var hostW = PreviewHost.ActualWidth;
        var hostH = PreviewHost.ActualHeight;
        if (hostW <= 1 || hostH <= 1)
        {
            return false;
        }

        scale = Math.Min(hostW / bmp.PixelWidth, hostH / bmp.PixelHeight);
        var dispW = bmp.PixelWidth * scale;
        var dispH = bmp.PixelHeight * scale;
        imageInHost = new Rect((hostW - dispW) / 2, (hostH - dispH) / 2, dispW, dispH);
        return true;
    }

    private bool TryMapHostToImage(Point hostPt, out Point imagePt)
    {
        imagePt = default;
        if (!TryGetImageLayout(out var layout, out var scale) || scale <= 0)
        {
            return false;
        }

        if (!layout.Contains(hostPt))
        {
            // clamp to image bounds for easier dragging near edges
            hostPt = new Point(
                Math.Clamp(hostPt.X, layout.Left, layout.Right),
                Math.Clamp(hostPt.Y, layout.Top, layout.Bottom));
        }

        var ix = (hostPt.X - layout.X) / scale;
        var iy = (hostPt.Y - layout.Y) / scale;
        // Map into ROI coordinate space (source size stored on VM).
        if (_vm.PreviewImageWidth > 0 && ResultImage.Source is BitmapSource bmp && bmp.PixelWidth > 0
            && bmp.PixelWidth != _vm.PreviewImageWidth)
        {
            ix *= (double)_vm.PreviewImageWidth / bmp.PixelWidth;
            iy *= (double)_vm.PreviewImageHeight / bmp.PixelHeight;
        }

        imagePt = new Point(ix, iy);
        return true;
    }

    private bool TryMapImageRectToHost(VisionRoiRect rect, out Rect hostRect)
    {
        hostRect = default;
        if (!TryGetImageLayout(out var layout, out var scale) || scale <= 0)
        {
            return false;
        }

        double sx = rect.X;
        double sy = rect.Y;
        double sw = rect.W;
        double sh = rect.H;
        if (ResultImage.Source is BitmapSource bmp && bmp.PixelWidth > 0
            && _vm.PreviewImageWidth > 0 && bmp.PixelWidth != _vm.PreviewImageWidth)
        {
            var fx = (double)bmp.PixelWidth / _vm.PreviewImageWidth;
            var fy = (double)bmp.PixelHeight / _vm.PreviewImageHeight;
            sx *= fx;
            sy *= fy;
            sw *= fx;
            sh *= fy;
        }

        hostRect = new Rect(
            layout.X + sx * scale,
            layout.Y + sy * scale,
            Math.Max(1, sw * scale),
            Math.Max(1, sh * scale));
        return true;
    }

    private void Preview_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_vm.PreviewImageWidth <= 0 || ResultImage.Source is null)
        {
            return;
        }

        var hostPt = e.GetPosition(PreviewHost);
        if (!TryMapHostToImage(hostPt, out var imgPt))
        {
            return;
        }

        _roiStartHost = hostPt;
        var existing = _vm.TryGetRoiRect();
        if (existing is { } cur
            && imgPt.X >= cur.X && imgPt.X <= cur.X + cur.W
            && imgPt.Y >= cur.Y && imgPt.Y <= cur.Y + cur.H)
        {
            _roiMoving = true;
            _roiStartRect = cur;
            _roiDraft = cur;
        }
        else
        {
            _roiMoving = false;
            _roiDraft = new VisionRoiRect((int)imgPt.X, (int)imgPt.Y, 1, 1);
        }

        _roiDragging = true;
        PreviewHost.CaptureMouse();
        RefreshRoiOverlay();
        e.Handled = true;
    }

    private void Preview_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_roiDragging)
        {
            return;
        }

        var hostPt = e.GetPosition(PreviewHost);
        if (!TryMapHostToImage(hostPt, out var imgPt) || !TryMapHostToImage(_roiStartHost, out var startImg))
        {
            return;
        }

        if (_roiMoving)
        {
            var dx = (int)Math.Round(imgPt.X - startImg.X);
            var dy = (int)Math.Round(imgPt.Y - startImg.Y);
            _roiDraft = new VisionRoiRect(
                _roiStartRect.X + dx,
                _roiStartRect.Y + dy,
                _roiStartRect.W,
                _roiStartRect.H).ClampToImage(_vm.PreviewImageWidth, _vm.PreviewImageHeight);
        }
        else
        {
            var x0 = (int)Math.Round(startImg.X);
            var y0 = (int)Math.Round(startImg.Y);
            var x1 = (int)Math.Round(imgPt.X);
            var y1 = (int)Math.Round(imgPt.Y);
            _roiDraft = new VisionRoiRect(x0, y0, x1 - x0, y1 - y0)
                .Normalize()
                .ClampToImage(_vm.PreviewImageWidth, _vm.PreviewImageHeight);
        }

        RefreshRoiOverlay();
        e.Handled = true;
    }

    private void Preview_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_roiDragging)
        {
            return;
        }

        _roiDragging = false;
        PreviewHost.ReleaseMouseCapture();
        _vm.ApplyRoiRect(_roiDraft);
        RefreshCanvas();
        RefreshRoiOverlay();
        RoiHintText.Text = $"ROI {_roiDraft.X},{_roiDraft.Y} {_roiDraft.W}x{_roiDraft.H}";
        e.Handled = true;
    }

    private void Preview_MouseLeave(object sender, MouseEventArgs e)
    {
        // keep drag if mouse captured
        if (!_roiDragging || PreviewHost.IsMouseCaptured)
        {
            return;
        }

        _roiDragging = false;
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
            RefreshRoiOverlay();
        });
}
