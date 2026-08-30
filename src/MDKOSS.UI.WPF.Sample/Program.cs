using MDKOSS.Extensions;
using MDKOSS.Sample.SampleExt;
using MDKOSS.UI.WPF;
using MDKOSS.UI.WPF.Infrastructure;
using MDKOSS.UI.WPF.Sample.Modules;

namespace MDKOSS.UI.WPF.Sample;

/// <summary>
/// WPF 启动示例：插件发现 + SampleExt + 各扩展模块联调页。
/// </summary>
internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        WpfUiExtensionHost.Register(new SampleWpfUiExtension());
        MdkWpfHost.ExtraExtensions = () => MdkExtensionHost.Register(new SampleExtExtension());
        MdkWpfHost.Run(args);
    }
}
