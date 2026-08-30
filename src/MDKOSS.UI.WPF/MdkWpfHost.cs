namespace MDKOSS.UI.WPF;

/// <summary>
/// Starts the Prism WPF shell. Call from a host exe after registering
/// <see cref="Extensions.IMdkExtension"/> / <see cref="Infrastructure.IWpfUiExtension"/>.
/// </summary>
public static class MdkWpfHost
{
    /// <summary>
    /// Optional extra <see cref="Extensions.MdkExtensionHost.Register"/> calls,
    /// invoked after plugin discovery and before <c>MdkRuntime</c> is created.
    /// </summary>
    public static Action? ExtraExtensions { get; set; }

    public static void Run(string[]? args = null)
    {
        _ = args;
        var app = new App();
        app.InitializeComponent();
        app.Run();
    }
}
