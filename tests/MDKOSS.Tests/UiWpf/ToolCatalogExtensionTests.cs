using MDKOSS.UI.WPF.Infrastructure;

namespace MDKOSS.Tests.UiWpf;

public sealed class ToolCatalogExtensionTests
{
    [Fact]
    public void AddPage_appends_debug_page_once()
    {
        const string id = "debug_catalog_test_page";
        ToolCatalog.AddPage("debug", id, "测试页");
        ToolCatalog.AddPage("debug", id, "测试页");

        var debug = ToolCatalog.ResolveGroup("debug");
        Assert.Equal(1, debug.Pages.Count(p => p.Id == id));
        Assert.Equal("测试页", ToolCatalog.ResolvePage(debug, id).Label);
    }

    [Fact]
    public void DeviceKind_recognizes_extension_types()
    {
        Assert.True(DeviceKind.IsTcp(new MDKOSS.Core.DeviceSnapshot(
            "tcp1", "TCP", "tcpdev", "Idle", "tcp", false)));
        Assert.True(DeviceKind.IsPyScript(new MDKOSS.Core.DeviceSnapshot(
            "py-1", "Py", "devpyscript", "Idle", "py", false)));
        Assert.True(DeviceKind.IsModbus(new MDKOSS.Core.DeviceSnapshot(
            "mod-1", "Mod", "devmodserver", "Idle", "mod", false)));
        Assert.True(DeviceKind.IsSampleBeacon(new MDKOSS.Core.DeviceSnapshot(
            "sample-beacon", "Beacon", "samplebeacon", "Running", "SAMPLE-BEACON", true)));
    }
}
