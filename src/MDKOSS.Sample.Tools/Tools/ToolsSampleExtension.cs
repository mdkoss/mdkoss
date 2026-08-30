using MDKOSS.Extensions;

namespace MDKOSS.Sample.Tools.Machine;

/// <summary>
/// 设备组件调试扩展：注册入口页 <c>index_tools.html</c>。
/// 驱动 / 设备实现由插件提供，本工程只做宿主与目录页。
/// </summary>
public sealed class ToolsSampleExtension : IMdkExtension
{
    public string Id => "sample-tools";

    public string DisplayName => "Sample Device Tools";

    public void Register(IExtensionRegistration registration)
    {
        ArgumentNullException.ThrowIfNull(registration);
        registration.StaticPage("/index_tools.html", () => ToolsViewPages.IndexHtml);
    }
}
