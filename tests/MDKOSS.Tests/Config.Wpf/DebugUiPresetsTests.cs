using MDKOSS.Config.Wpf.Debug;
using MDKOSS.Core.Drivers;

namespace MDKOSS.Tests.Config.Wpf;

public sealed class DebugUiPresetsTests
{
    [Fact]
    public void Default_io_groups_match_gpi_and_gpo()
    {
        Assert.Equal(GtsIoType.Gpi, DebugUi.DefaultDiGroup);
        Assert.Equal(GtsIoType.Gpo, DebugUi.DefaultDoGroup);
        Assert.Equal(4, DebugUi.DefaultDiGroup);
        Assert.Equal(12, DebugUi.DefaultDoGroup);
    }

    [Fact]
    public void Di_presets_start_with_gpi_and_cover_common_inputs()
    {
        Assert.Equal(GtsIoType.Gpi, DebugUi.DiPortPresets[0].Type);
        Assert.Contains(DebugUi.DiPortPresets, p => p.Type == GtsIoType.Home);
        Assert.Contains(DebugUi.DiPortPresets, p => p.Type == GtsIoType.Alarm);
        Assert.All(DebugUi.DiPortPresets, p => Assert.False(string.IsNullOrWhiteSpace(p.Label)));
    }

    [Fact]
    public void Do_presets_start_with_gpo()
    {
        Assert.Equal(GtsIoType.Gpo, DebugUi.DoPortPresets[0].Type);
        Assert.Contains(DebugUi.DoPortPresets, p => p.Type == GtsIoType.Enable);
        Assert.Contains(DebugUi.DoPortPresets, p => p.Type == GtsIoType.Clear);
    }
}
