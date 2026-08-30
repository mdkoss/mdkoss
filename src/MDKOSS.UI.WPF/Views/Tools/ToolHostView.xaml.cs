using System.Windows;
using System.Windows.Controls;
using MDKOSS.UI.WPF.ViewModels.Tools;

namespace MDKOSS.UI.WPF.Views.Tools;

public partial class ToolHostView : UserControl
{
    public ToolHostView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => Hook();
        Loaded += (_, _) => ApplyTheme();
    }

    private void Hook()
    {
        if (DataContext is ToolHostViewModel vm)
        {
            vm.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName is nameof(ToolHostViewModel.GroupId) or null)
                {
                    ApplyTheme();
                }
            };
        }

        ApplyTheme();
    }

    private void ApplyTheme()
    {
        var group = (DataContext as ToolHostViewModel)?.GroupId ?? "monitor";
        var theme = group switch
        {
            "debug" => "Themes/DebugTheme.xaml",
            "man" => "Themes/ManTheme.xaml",
            _ => "Themes/MonitorTheme.xaml",
        };

        Resources.MergedDictionaries.Clear();
        Resources.MergedDictionaries.Add(Load(theme));
        Resources.MergedDictionaries.Add(Load("Themes/ToolImplicit.xaml"));
        Background = (System.Windows.Media.Brush)FindResource("ToolBgBrush");
    }

    private static ResourceDictionary Load(string relative) =>
        new()
        {
            Source = new Uri($"pack://application:,,,/{relative}", UriKind.Absolute),
        };
}
