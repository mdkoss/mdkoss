using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using MDKOSS.Cef.Extensions;

namespace MDKOSS.Config.Wpf.Hmi;

public partial class HmiEditorWindow : Window
{
    private readonly ConfigWorkspace _workspace;
    private readonly Action? _onApplied;
    private readonly HmiLayout _layout;
    private string? _selectedId;
    private Point _dragOffset;
    private bool _dragging;

    public HmiEditorWindow(ConfigWorkspace workspace, Action? onApplied = null)
    {
        InitializeComponent();
        _workspace = workspace;
        _onApplied = onApplied;
        _layout = HmiLayoutStore.Clone(workspace.Hmi);
        TitleBox.Text = _layout.Title;
        WidthBox.Text = _layout.CanvasWidth.ToString(CultureInfo.InvariantCulture);
        HeightBox.Text = _layout.CanvasHeight.ToString(CultureInfo.InvariantCulture);
        BuildPalette();
        Redraw();
    }

    private void BuildPalette()
    {
        PalettePanel.Children.Clear();
        foreach (var desc in HmiWidgetCatalog.All)
        {
            var btn = new Button
            {
                Content = $"{desc.DisplayName} · {desc.Type}",
                Tag = desc.Type,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Margin = new Thickness(0, 0, 0, 6),
            };
            btn.Click += (_, _) => AddWidget(desc.Type);
            PalettePanel.Children.Add(btn);
        }
    }

    private void AddWidget(string type)
    {
        var widget = HmiWidgetCatalog.CreateInstance(type, 24 + (_layout.Widgets.Count % 6) * 16, 24 + (_layout.Widgets.Count % 6) * 16);
        _layout.Widgets.Add(widget);
        _selectedId = widget.Id;
        Redraw();
    }

    private void Redraw()
    {
        ApplyCanvasMeta();
        Board.Width = _layout.CanvasWidth;
        Board.Height = _layout.CanvasHeight;
        Board.Children.Clear();
        foreach (var widget in _layout.Widgets)
        {
            var border = new Border
            {
                Width = Math.Max(24, widget.W),
                Height = Math.Max(20, widget.H),
                Background = string.Equals(widget.Id, _selectedId, StringComparison.OrdinalIgnoreCase)
                    ? new SolidColorBrush(Color.FromRgb(213, 237, 228))
                    : Brushes.White,
                BorderBrush = string.Equals(widget.Id, _selectedId, StringComparison.OrdinalIgnoreCase)
                    ? (Brush)FindResource("AccentBrush")
                    : (Brush)FindResource("BorderBrushKey"),
                BorderThickness = new Thickness(string.Equals(widget.Id, _selectedId, StringComparison.OrdinalIgnoreCase) ? 2 : 1),
                CornerRadius = new CornerRadius(4),
                Tag = widget.Id,
                Cursor = Cursors.SizeAll,
                Child = new TextBlock
                {
                    Text = $"{widget.Type}\n{HmiDraftMapper.Describe(widget)}",
                    Margin = new Thickness(6, 4, 6, 4),
                    TextWrapping = TextWrapping.Wrap,
                    FontSize = 11,
                },
            };
            Canvas.SetLeft(border, widget.X);
            Canvas.SetTop(border, widget.Y);
            border.MouseLeftButtonDown += Widget_MouseLeftButtonDown;
            Board.Children.Add(border);
        }

        RenderProps();
    }

    private void Widget_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Border border || border.Tag is not string id)
        {
            return;
        }

        _selectedId = id;
        var widget = FindWidget(id);
        if (widget is null)
        {
            return;
        }

        _dragging = true;
        var pos = e.GetPosition(Board);
        _dragOffset = new Point(pos.X - widget.X, pos.Y - widget.Y);
        border.CaptureMouse();
        Redraw();
        e.Handled = true;
    }

    private void Board_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is Canvas)
        {
            _selectedId = null;
            Redraw();
        }
    }

    private void Board_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_dragging || _selectedId is null || e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        var widget = FindWidget(_selectedId);
        if (widget is null)
        {
            return;
        }

        var pos = e.GetPosition(Board);
        widget.X = Math.Max(0, Math.Round(pos.X - _dragOffset.X));
        widget.Y = Math.Max(0, Math.Round(pos.Y - _dragOffset.Y));
        foreach (var child in Board.Children.OfType<Border>())
        {
            if (child.Tag as string == widget.Id)
            {
                Canvas.SetLeft(child, widget.X);
                Canvas.SetTop(child, widget.Y);
                break;
            }
        }
    }

    private void Board_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        _dragging = false;
        foreach (var child in Board.Children.OfType<FrameworkElement>())
        {
            if (child.IsMouseCaptured)
            {
                child.ReleaseMouseCapture();
            }
        }
    }

    private void RenderProps()
    {
        PropsPanel.Children.Clear();
        var widget = FindWidget(_selectedId);
        if (widget is null)
        {
            PropsPanel.Children.Add(new TextBlock
            {
                Text = "点选画布中的控件。",
                Foreground = (Brush)FindResource("MutedBrush"),
            });
            return;
        }

        AddField("Id", widget.Id, v =>
        {
            if (!string.IsNullOrWhiteSpace(v)) widget.Id = v.Trim();
        });
        AddField("Type", widget.Type, _ => { }, readOnly: true);
        AddField("X", widget.X.ToString(CultureInfo.InvariantCulture), v => widget.X = ParseNum(v, widget.X));
        AddField("Y", widget.Y.ToString(CultureInfo.InvariantCulture), v => widget.Y = ParseNum(v, widget.Y));
        AddField("W", widget.W.ToString(CultureInfo.InvariantCulture), v => widget.W = ParseNum(v, widget.W));
        AddField("H", widget.H.ToString(CultureInfo.InvariantCulture), v => widget.H = ParseNum(v, widget.H));

        var desc = HmiWidgetCatalog.Find(widget.Type);
        if (desc is null)
        {
            return;
        }

        foreach (var prop in desc.Props)
        {
            var current = HmiProps.GetString(widget.Props, prop.Key, prop.Default ?? "");
            AddField(prop.Label, current, v => widget.Props[prop.Key] = v);
        }
    }

    private void AddField(string label, string value, Action<string> apply, bool readOnly = false)
    {
        PropsPanel.Children.Add(new TextBlock
        {
            Text = label,
            Foreground = (Brush)FindResource("MutedBrush"),
            Margin = new Thickness(0, 0, 0, 2),
        });
        var box = new TextBox
        {
            Text = value,
            Margin = new Thickness(0, 0, 0, 8),
            IsReadOnly = readOnly,
        };
        if (!readOnly)
        {
            box.LostFocus += (_, _) =>
            {
                apply(box.Text);
                Redraw();
            };
        }

        PropsPanel.Children.Add(box);
    }

    private HmiWidgetInstance? FindWidget(string? id) =>
        string.IsNullOrWhiteSpace(id)
            ? null
            : _layout.Widgets.FirstOrDefault(w => string.Equals(w.Id, id, StringComparison.OrdinalIgnoreCase));

    private void ApplyCanvasMeta()
    {
        _layout.Title = string.IsNullOrWhiteSpace(TitleBox.Text) ? "主界面监控" : TitleBox.Text.Trim();
        if (int.TryParse(WidthBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var w) && w > 0)
        {
            _layout.CanvasWidth = w;
        }

        if (int.TryParse(HeightBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var h) && h > 0)
        {
            _layout.CanvasHeight = h;
        }
    }

    private void CanvasMeta_Changed(object sender, TextChangedEventArgs e)
    {
        if (!IsLoaded)
        {
            return;
        }

        ApplyCanvasMeta();
        Board.Width = _layout.CanvasWidth;
        Board.Height = _layout.CanvasHeight;
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedId is null)
        {
            return;
        }

        _layout.Widgets.RemoveAll(w => string.Equals(w.Id, _selectedId, StringComparison.OrdinalIgnoreCase));
        _selectedId = null;
        Redraw();
    }

    private void Reset_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show(this, "恢复默认组态并丢弃当前画布？", "HMI",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
        {
            return;
        }

        var fresh = HmiLayout.CreateDefault();
        _layout.Title = fresh.Title;
        _layout.CanvasWidth = fresh.CanvasWidth;
        _layout.CanvasHeight = fresh.CanvasHeight;
        _layout.Widgets.Clear();
        _layout.Widgets.AddRange(fresh.Widgets);
        TitleBox.Text = _layout.Title;
        WidthBox.Text = _layout.CanvasWidth.ToString(CultureInfo.InvariantCulture);
        HeightBox.Text = _layout.CanvasHeight.ToString(CultureInfo.InvariantCulture);
        _selectedId = null;
        Redraw();
    }

    private void Apply_Click(object sender, RoutedEventArgs e)
    {
        ApplyCanvasMeta();
        HmiLayoutStore.Normalize(_layout);
        _workspace.ReplaceHmi(HmiLayoutStore.Clone(_layout), _selectedId);
        _onApplied?.Invoke();
        HintText.Text = "已写回工作区（文件 → 保存 才会落盘）。";
    }

    private static double ParseNum(string raw, double fallback) =>
        double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var n) ? n : fallback;
}
