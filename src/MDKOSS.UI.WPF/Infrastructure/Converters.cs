using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace MDKOSS.UI.WPF.Infrastructure;

public sealed class EqualityToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var left = value?.ToString() ?? string.Empty;
        var right = parameter?.ToString() ?? string.Empty;
        return string.Equals(left, right, StringComparison.OrdinalIgnoreCase)
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        Binding.DoNothing;
}

public sealed class LampBrushConverter : IValueConverter
{
    private static readonly SolidColorBrush Red = Brush("#ff6b6b");
    private static readonly SolidColorBrush Yellow = Brush("#ffd166");
    private static readonly SolidColorBrush Green = Brush("#3ee6a0");
    private static readonly SolidColorBrush Off = Brush("#4a6aa3");

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return (value?.ToString() ?? "red").ToLowerInvariant() switch
        {
            "green" => Green,
            "yellow" => Yellow,
            "red" => Red,
            _ => Off,
        };
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        Binding.DoNothing;

    private static SolidColorBrush Brush(string hex)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)!);
        brush.Freeze();
        return brush;
    }
}

public sealed class StatusDotBrushConverter : IValueConverter
{
    private static readonly SolidColorBrush Ok = Frozen("#3ee6a0");
    private static readonly SolidColorBrush Warn = Frozen("#ffd166");
    private static readonly SolidColorBrush Bad = Frozen("#ff6b6b");
    private static readonly SolidColorBrush Idle = Frozen("#c4d2ee");

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return (value?.ToString() ?? "").ToLowerInvariant() switch
        {
            "ok" => Ok,
            "warn" => Warn,
            "bad" => Bad,
            _ => Idle,
        };
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        Binding.DoNothing;

    private static SolidColorBrush Frozen(string hex)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)!);
        brush.Freeze();
        return brush;
    }
}

public sealed class OrderStatusBrushConverter : IValueConverter
{
    private static readonly SolidColorBrush Running = Frozen("#1a4d3a", "#3ee6a0");
    private static readonly SolidColorBrush Pending = Frozen("#3a3420", "#ffd166");
    private static readonly SolidColorBrush Fault = Frozen("#4a2020", "#ff6b6b");
    private static readonly SolidColorBrush Done = Frozen("#1a3058", "#c4d2ee");

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return (value?.ToString() ?? "").ToLowerInvariant() switch
        {
            "running" => Running,
            "pending" => Pending,
            "fault" or "error" => Fault,
            _ => Done,
        };
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        Binding.DoNothing;

    private static SolidColorBrush Frozen(string bg, string _)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(bg)!);
        brush.Freeze();
        return brush;
    }
}
