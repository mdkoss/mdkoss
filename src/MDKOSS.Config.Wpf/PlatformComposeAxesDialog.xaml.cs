using System.Windows;
using System.Windows.Controls;
using MDKOSS.Core;

namespace MDKOSS.Config.Wpf;

/// <summary>Pick existing Axis devices for each platform axis letter.</summary>
public partial class PlatformComposeAxesDialog : Window
{
    private readonly IReadOnlyList<string> _letters;
    private readonly IReadOnlyList<MdkSetting.DeviceConfig> _axes;
    private readonly Dictionary<string, ComboBox> _combos = new(StringComparer.OrdinalIgnoreCase);

    public PlatformComposeAxesDialog(
        string kindToken,
        IReadOnlyList<string> letters,
        IReadOnlyList<MdkSetting.DeviceConfig> axes,
        IReadOnlyDictionary<string, string> currentBindings)
    {
        InitializeComponent();
        _letters = letters;
        _axes = axes;
        HeadlineText.Text = $"组合轴（kind={kindToken}）— 为每个字母选择已有 Axis";

        var axisOptions = axes
            .Select(a =>
            {
                var idx = AxisDeviceParameterSet.ParseAxisIndex(a.Parameters);
                var label = string.IsNullOrWhiteSpace(a.Name)
                    ? $"{a.Id}  (axis={idx}, {a.DriverId})"
                    : $"{a.Id} — {a.Name}  (axis={idx}, {a.DriverId})";
                return (a.Id, label);
            })
            .ToList();

        foreach (var letter in letters)
        {
            var row = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
            var label = new TextBlock
            {
                Text = $"轴 {letter}",
                Width = 56,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = (System.Windows.Media.Brush)FindResource("MutedBrush"),
            };
            DockPanel.SetDock(label, Dock.Left);
            row.Children.Add(label);

            var combo = new ComboBox
            {
                IsEditable = true,
                IsTextSearchEnabled = true,
                MinWidth = 280,
            };
            combo.Items.Add("");
            foreach (var (id, text) in axisOptions)
            {
                combo.Items.Add(new ComboBoxItem { Content = text, Tag = id });
            }

            if (currentBindings.TryGetValue(letter, out var cur) && !string.IsNullOrWhiteSpace(cur))
            {
                var match = combo.Items.OfType<ComboBoxItem>()
                    .FirstOrDefault(i => string.Equals(i.Tag as string, cur, StringComparison.OrdinalIgnoreCase));
                if (match is not null)
                {
                    combo.SelectedItem = match;
                }
                else
                {
                    combo.Text = cur;
                }
            }

            row.Children.Add(combo);
            SlotsPanel.Children.Add(row);
            _combos[letter] = combo;
        }
    }

    /// <summary>Letter → selected Axis device id (empty entries omitted).</summary>
    public Dictionary<string, string> SelectedAxisIds { get; } = new(StringComparer.OrdinalIgnoreCase);

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        SelectedAxisIds.Clear();
        foreach (var letter in _letters)
        {
            if (!_combos.TryGetValue(letter, out var combo))
            {
                continue;
            }

            string? id = null;
            if (combo.SelectedItem is ComboBoxItem { Tag: string tag } && !string.IsNullOrWhiteSpace(tag))
            {
                id = tag.Trim();
            }
            else if (!string.IsNullOrWhiteSpace(combo.Text))
            {
                var text = combo.Text.Trim();
                // Accept raw id typed by user, or match against known axes.
                var known = _axes.FirstOrDefault(a =>
                    string.Equals(a.Id, text, StringComparison.OrdinalIgnoreCase));
                id = known?.Id ?? text;
            }

            if (!string.IsNullOrWhiteSpace(id))
            {
                SelectedAxisIds[letter] = id;
            }
        }

        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
