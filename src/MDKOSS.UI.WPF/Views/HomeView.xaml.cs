using System.Windows.Controls;
using System.Windows.Input;
using MDKOSS.UI.WPF.Models;
using MDKOSS.UI.WPF.ViewModels;

namespace MDKOSS.UI.WPF.Views;

public partial class HomeView : UserControl
{
    public HomeView()
    {
        InitializeComponent();
    }

    private void OnOrderDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is HomeViewModel vm && sender is DataGrid grid && grid.SelectedItem is OrderRow row)
        {
            vm.OpenOrderCommand.Execute(row);
        }
    }
}
