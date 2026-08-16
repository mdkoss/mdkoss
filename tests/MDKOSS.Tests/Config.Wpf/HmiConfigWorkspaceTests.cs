using MDKOSS.Cef.Extensions;
using MDKOSS.Config.Wpf;
using MDKOSS.Core;

namespace MDKOSS.Tests.Config.Wpf;

public sealed class HmiConfigWorkspaceTests
{
    [Fact]
    public void Open_loads_default_hmi_when_sidecar_missing()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"mdk-hmi-cfg-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        var settingPath = Path.Combine(dir, "sample.setting.json");
        new MdkSetting { ProjectName = "hmi-cfg" }.Save(settingPath);

        try
        {
            var ws = new ConfigWorkspace();
            ws.Open(settingPath);
            ws.SelectModule(ConfigModule.Hmi, null);
            Assert.Equal(ConfigModule.Hmi, ws.CurrentModule);
            Assert.True(ws.Hmi.Widgets.Count >= 6);
            Assert.Contains(ws.Items, i => i.Source is HmiWidgetInstance);
            Assert.EndsWith("hmi.layout.json", ws.HmiPath, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void Save_writes_hmi_layout_beside_setting()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"mdk-hmi-cfg-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        var settingPath = Path.Combine(dir, "sample.setting.json");
        new MdkSetting { ProjectName = "hmi-save" }.Save(settingPath);

        try
        {
            var ws = new ConfigWorkspace();
            ws.Open(settingPath);
            ws.SelectModule(ConfigModule.Hmi, null);
            var req = ws.PrepareCreateRequest("lamp");
            var created = ws.CommitCreate(req);
            Assert.NotNull(created);
            Assert.Equal("lamp", ((HmiWidgetInstance)created!.Source).Type);

            ws.Save();
            var layoutPath = Path.Combine(dir, "hmi.layout.json");
            Assert.True(File.Exists(layoutPath));
            var loaded = HmiLayoutStore.LoadFromFile(layoutPath);
            Assert.Contains(loaded.Widgets, w => w.Type == "lamp");
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void ReplaceHmi_refreshes_list()
    {
        var ws = new ConfigWorkspace();
        ws.SelectModule(ConfigModule.Hmi, null);
        var layout = HmiLayout.CreateDefault();
        layout.Widgets =
        [
            HmiWidgetCatalog.CreateInstance("label", 0, 0, "w-only"),
        ];
        ws.ReplaceHmi(layout, "w-only");
        Assert.Single(ws.Items);
        Assert.Equal("w-only", ws.SelectedItem?.Key);
    }
}
