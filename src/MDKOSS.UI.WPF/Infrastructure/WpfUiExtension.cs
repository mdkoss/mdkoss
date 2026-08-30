using System.Windows;
using Prism.Ioc;

namespace MDKOSS.UI.WPF.Infrastructure;

/// <summary>
/// Optional WPF pages supplied by a host / sample assembly.
/// Register via <see cref="WpfUiExtensionHost"/> before <see cref="MdkWpfHost.Run"/>.
/// </summary>
public interface IWpfUiExtension
{
    string Id { get; }

    void RegisterUi(IWpfUiRegistration ui);
}

public interface IWpfUiRegistration
{
    void ToolPage<TView, TViewModel>(string pageId, string groupId, string label)
        where TView : FrameworkElement
        where TViewModel : class;
}

public static class WpfUiExtensionHost
{
    private static readonly object Sync = new();
    private static readonly List<IWpfUiExtension> Registered = [];

    public static IReadOnlyList<IWpfUiExtension> Extensions
    {
        get
        {
            lock (Sync)
            {
                return Registered.ToArray();
            }
        }
    }

    public static void Register(IWpfUiExtension extension)
    {
        ArgumentNullException.ThrowIfNull(extension);
        if (string.IsNullOrWhiteSpace(extension.Id))
        {
            throw new ArgumentException("Extension Id cannot be empty.", nameof(extension));
        }

        lock (Sync)
        {
            if (Registered.Any(e => string.Equals(e.Id, extension.Id, StringComparison.OrdinalIgnoreCase)))
            {
                return;
            }

            Registered.Add(extension);
        }
    }

    internal static void Apply(IContainerRegistry registry)
    {
        var ui = new WpfUiRegistration(registry);
        foreach (var ext in Extensions)
        {
            ext.RegisterUi(ui);
        }
    }

    private sealed class WpfUiRegistration : IWpfUiRegistration
    {
        private readonly IContainerRegistry _registry;

        public WpfUiRegistration(IContainerRegistry registry) => _registry = registry;

        public void ToolPage<TView, TViewModel>(string pageId, string groupId, string label)
            where TView : FrameworkElement
            where TViewModel : class
        {
            ToolCatalog.AddPage(groupId, pageId, label);
            _registry.RegisterForNavigation<TView, TViewModel>(pageId);
        }
    }
}
