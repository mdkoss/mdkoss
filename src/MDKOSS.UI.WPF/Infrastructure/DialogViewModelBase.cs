using Prism.Commands;
using Prism.Dialogs;
using Prism.Mvvm;

namespace MDKOSS.UI.WPF.Infrastructure;

public abstract class DialogViewModelBase : BindableBase, IDialogAware
{
    private string _title = "对话框";

    protected DialogViewModelBase()
    {
        CloseCommand = new DelegateCommand(() => RequestClose.Invoke(ButtonResult.OK));
    }

    public string Title
    {
        get => _title;
        set => SetProperty(ref _title, value);
    }

    public DelegateCommand CloseCommand { get; }

    public DialogCloseListener RequestClose { get; }

    public virtual bool CanCloseDialog() => true;

    public virtual void OnDialogClosed()
    {
    }

    public virtual void OnDialogOpened(IDialogParameters parameters)
    {
    }
}
