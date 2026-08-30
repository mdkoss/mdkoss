using System.Windows;
using MDKOSS.UI.WPF.Infrastructure;
using MDKOSS.UI.WPF.Services;
using Prism.Commands;
using Prism.Ioc;
using Prism.Mvvm;
using Prism.Navigation.Regions;

namespace MDKOSS.UI.WPF.ViewModels.Tools;

public abstract class LiveToolViewModel : BindableBase, INavigationAware, IDisposable
{
    private string _status = "";

    protected LiveToolViewModel(IRuntimeUiService runtime)
    {
        Runtime = runtime;
        Runtime.SnapshotChanged += OnChanged;
        GoToolCommand = new DelegateCommand<string>(id =>
            ContainerLocator.Container.Resolve<IToolNavigator>().NavigateByPage(id));
    }

    protected IRuntimeUiService Runtime { get; }

    public DelegateCommand<string> GoToolCommand { get; }

    protected string? PreferredDeviceId { get; private set; }

    public string Status
    {
        get => _status;
        protected set => SetProperty(ref _status, value);
    }

    public virtual void OnNavigatedTo(NavigationContext navigationContext)
    {
        PreferredDeviceId = navigationContext.Parameters.GetValue<string>("deviceId");
        Reload();
    }

    public virtual bool IsNavigationTarget(NavigationContext navigationContext) => true;

    public virtual void OnNavigatedFrom(NavigationContext navigationContext)
    {
    }

    protected abstract void Reload();

    protected void Toast(string message, bool ok = true) =>
        Status = (ok ? "" : "失败：") + message;

    private void OnChanged(object? sender, EventArgs e) =>
        Application.Current?.Dispatcher.Invoke(Reload);

    public virtual void Dispose() => Runtime.SnapshotChanged -= OnChanged;
}
