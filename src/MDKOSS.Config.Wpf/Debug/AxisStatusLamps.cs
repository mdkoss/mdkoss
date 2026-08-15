using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MDKOSS.Core.Drivers;

namespace MDKOSS.Config.Wpf.Debug;

internal static class AxisStatusLamps
{
    private static readonly Brush OffBg = new SolidColorBrush(Color.FromRgb(0xF4, 0xF6, 0xF8));
    private static readonly Brush OnBg = new SolidColorBrush(Color.FromRgb(0xE6, 0xF4, 0xEF));
    private static readonly Brush FaultBg = new SolidColorBrush(Color.FromRgb(0xFD, 0xED, 0xED));
    private static readonly Brush OffFg = new SolidColorBrush(Color.FromRgb(0x65, 0x6D, 0x76));
    private static readonly Brush OnFg = new SolidColorBrush(Color.FromRgb(0x0B, 0x6E, 0x4F));
    private static readonly Brush FaultFg = new SolidColorBrush(Color.FromRgb(0xB4, 0x23, 0x18));
    private static readonly Brush OffBorder = new SolidColorBrush(Color.FromRgb(0xD0, 0xD7, 0xDE));
    private static readonly Brush OnBorder = new SolidColorBrush(Color.FromRgb(0x8F, 0xC9, 0xB5));
    private static readonly Brush FaultBorder = new SolidColorBrush(Color.FromRgb(0xF0, 0xB4, 0xB0));

    public static void Render(Panel panel, AxisStatus? status)
    {
        panel.Children.Clear();
        foreach (var lamp in AxisStatus.Lamps)
        {
            var on = status is { } s && lamp.Read(s);
            var fault = on && lamp.Fault;
            var chip = new Border
            {
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(8, 2, 8, 2),
                Margin = new Thickness(0, 0, 6, 6),
                Background = fault ? FaultBg : on ? OnBg : OffBg,
                BorderBrush = fault ? FaultBorder : on ? OnBorder : OffBorder,
                BorderThickness = new Thickness(1),
                ToolTip = lamp.Title,
                Child = new TextBlock
                {
                    Text = lamp.Code,
                    FontSize = 11,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = fault ? FaultFg : on ? OnFg : OffFg,
                },
            };
            panel.Children.Add(chip);
        }
    }
}
