using System.Windows;
using System.Windows.Input;
using MDKOSS.Tools.Calib.ViewModels;

namespace MDKOSS.Tools.Calib.Views;

public partial class MainWindow : Window
{
    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        InputBindings.Add(new KeyBinding(viewModel.OpenCommand, Key.O, ModifierKeys.Control));
        InputBindings.Add(new KeyBinding(viewModel.SaveCommand, Key.S, ModifierKeys.Control));
    }

    private void Exit_Click(object sender, RoutedEventArgs e) => Close();
}
