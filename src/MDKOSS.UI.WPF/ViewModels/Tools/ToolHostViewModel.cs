using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Threading;
using MDKOSS.UI.WPF.Infrastructure;
using MDKOSS.UI.WPF.Models;
using Prism.Commands;
using Prism.Mvvm;
using Prism.Navigation.Regions;

namespace MDKOSS.UI.WPF.ViewModels.Tools;

public sealed class ToolHostViewModel : BindableBase, INavigationAware
{
    private readonly IRegionManager _regions;
    private string _groupId = ToolCatalog.Monitor.Id;
    private string _groupLabel = ToolCatalog.Monitor.Label;
    private string _modeHint = ToolCatalog.Monitor.ModeHint;
    private string _pageId = ToolCatalog.Monitor.Pages[0].Id;
    private string? _deviceId;

    public ToolHostViewModel(IRegionManager regions, IToolNavigator navigator)
    {
        _regions = regions;
        GoHomeCommand = new DelegateCommand(navigator.GoHome);
        SelectGroupCommand = new DelegateCommand<string>(id => NavigateGroup(id, null));
        SelectPageCommand = new DelegateCommand<string>(id => NavigateGroup(_groupId, id));
    }

    public string GroupId
    {
        get => _groupId;
        private set => SetProperty(ref _groupId, value);
    }

    public string GroupLabel
    {
        get => _groupLabel;
        private set => SetProperty(ref _groupLabel, value);
    }

    public string ModeHint
    {
        get => _modeHint;
        private set => SetProperty(ref _modeHint, value);
    }

    public bool IsMonitor => string.Equals(GroupId, "monitor", StringComparison.OrdinalIgnoreCase);
    public bool IsDebug => string.Equals(GroupId, "debug", StringComparison.OrdinalIgnoreCase);
    public bool IsMan => string.Equals(GroupId, "man", StringComparison.OrdinalIgnoreCase);

    public ObservableCollection<ToolNavItem> Pages { get; } = [];

    public DelegateCommand GoHomeCommand { get; }
    public DelegateCommand<string> SelectGroupCommand { get; }
    public DelegateCommand<string> SelectPageCommand { get; }

    public void OnNavigatedTo(NavigationContext navigationContext)
    {
        var groupId = navigationContext.Parameters.GetValue<string>("group") ?? ToolCatalog.Monitor.Id;
        var pageId = navigationContext.Parameters.GetValue<string>("page");
        _deviceId = navigationContext.Parameters.GetValue<string>("deviceId");
        NavigateGroup(groupId, pageId);
    }

    public bool IsNavigationTarget(NavigationContext navigationContext) => true;

    public void OnNavigatedFrom(NavigationContext navigationContext)
    {
    }

    private void NavigateGroup(string groupId, string? pageId)
    {
        var group = ToolCatalog.ResolveGroup(groupId);
        var page = ToolCatalog.ResolvePage(group, pageId);
        GroupId = group.Id;
        GroupLabel = group.Label;
        ModeHint = group.ModeHint;
        RaisePropertyChanged(nameof(IsMonitor));
        RaisePropertyChanged(nameof(IsDebug));
        RaisePropertyChanged(nameof(IsMan));
        _pageId = page.Id;

        Pages.Clear();
        foreach (var def in group.Pages)
        {
            Pages.Add(new ToolNavItem
            {
                Id = def.Id,
                Label = def.Label,
                IsActive = string.Equals(def.Id, page.Id, StringComparison.OrdinalIgnoreCase),
            });
        }

        var p = new NavigationParameters
        {
            { "group", group.Id },
            { "page", page.Id },
        };
        if (!string.IsNullOrWhiteSpace(_deviceId))
        {
            p.Add("deviceId", _deviceId);
        }
        Application.Current?.Dispatcher.BeginInvoke(
            () => _regions.RequestNavigate(RegionNames.ToolContent, page.Id, p),
            DispatcherPriority.Loaded);
    }
}
