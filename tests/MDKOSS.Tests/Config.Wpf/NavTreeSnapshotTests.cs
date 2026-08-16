using MDKOSS.Config.Wpf;
using MDKOSS.Core;

namespace MDKOSS.Tests.Config.Wpf;

public sealed class NavTreeSnapshotTests
{
    [Fact]
    public void BuildNavTreeSnapshot_does_not_change_module_filter_or_selection()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "configs", "sample.setting.json");
        Assert.True(File.Exists(path), $"missing sample setting: {path}");

        var ws = new ConfigWorkspace();
        ws.Open(path);
        ws.ListFilter = "sim";
        ws.SelectModule(ConfigModule.Drivers, null);
        var beforeModule = ws.CurrentModule;
        var beforeFilter = ws.ListFilter;
        var beforeKey = ws.SelectedItem?.Key;
        var beforeItemCount = ws.Items.Count;

        var snap = ws.BuildNavTreeSnapshot();

        Assert.Equal(beforeModule, ws.CurrentModule);
        Assert.Equal(beforeFilter, ws.ListFilter);
        Assert.Equal(beforeKey, ws.SelectedItem?.Key);
        Assert.Equal(beforeItemCount, ws.Items.Count);
        Assert.DoesNotContain(snap, e => e.Module == ConfigModule.Machine);
        Assert.Contains(snap, e => e.Module == ConfigModule.Drivers && e.Components.Count > 0);
        Assert.Contains(snap, e => e.Module == ConfigModule.Tasks);
        Assert.All(snap, e => Assert.False(string.IsNullOrWhiteSpace(e.Title)));
    }
}
