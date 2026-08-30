using System.Windows;
using Prism.Dialogs;

namespace MDKOSS.UI.WPF.Views.Dialogs;

public partial class NavyDialogWindow : Window, IDialogWindow
{
    public NavyDialogWindow()
    {
        InitializeComponent();
    }

    public IDialogResult? Result { get; set; }
}
