using Prism.Navigation.Regions;

namespace MDKOSS.UI.WPF.Infrastructure;

public interface IToolNavigator
{
    event EventHandler? HomeRequested;

    event EventHandler? ToolRequested;

    void GoHome();

    void Navigate(string groupId, string? pageId = null);

    void NavigateByPage(string pageId);
}

public sealed class ToolNavigator : IToolNavigator
{
    private readonly IRegionManager _regions;

    public ToolNavigator(IRegionManager regions) => _regions = regions;

    public event EventHandler? HomeRequested;

    public event EventHandler? ToolRequested;

    public void GoHome() => HomeRequested?.Invoke(this, EventArgs.Empty);

    public void Navigate(string groupId, string? pageId = null)
    {
        var group = ToolCatalog.ResolveGroup(groupId);
        var page = ToolCatalog.ResolvePage(group, pageId);
        var p = new NavigationParameters
        {
            { "group", group.Id },
            { "page", page.Id },
        };
        ToolRequested?.Invoke(this, EventArgs.Empty);
        _regions.RequestNavigate(RegionNames.Content, ViewNames.ToolHost, p);
    }

    public void NavigateByPage(string pageId)
    {
        if (string.IsNullOrWhiteSpace(pageId))
        {
            return;
        }

        var group = ToolCatalog.Groups.FirstOrDefault(g =>
                        g.Pages.Any(p => string.Equals(p.Id, pageId, StringComparison.OrdinalIgnoreCase)))
                    ?? ToolCatalog.Monitor;
        Navigate(group.Id, pageId);
    }
}
