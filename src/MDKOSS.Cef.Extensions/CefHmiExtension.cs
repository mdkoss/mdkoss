using MDKOSS.Extensions;

namespace MDKOSS.Cef.Extensions;

/// <summary>
/// Main-HMI 组态扩展：控件目录、布局 API、运行页 <c>index_hmi.html</c>、编辑页 <c>man_hmi.html</c>。
/// </summary>
public sealed class CefHmiExtension : IMdkExtension
{
    public string Id => "cef-hmi";

    public string DisplayName => "CEF 主界面组态";

    public void Register(IExtensionRegistration registration)
    {
        ArgumentNullException.ThrowIfNull(registration);
        HmiWidgetRegistry.EnsureLoaded();
        registration.MonitoringModule(runtime => new HmiApiModule(runtime));
        registration.StaticPage("/index_hmi.html", () => HmiViewPages.IndexHtml);
        registration.StaticPage("/man_hmi.html", () => HmiViewPages.EditorHtml);
    }
}

/// <summary>Convenience bootstrap; hosts normally load this DLL via plugin discovery.</summary>
public static class CefHmiExtensionBootstrap
{
    public static void Register()
    {
        MdkExtensionHost.Register(new CefHmiExtension());
    }
}
