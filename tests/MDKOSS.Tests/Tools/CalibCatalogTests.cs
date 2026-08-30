using MDKOSS.Core;
using MDKOSS.Tools.Calib.Calib;

namespace MDKOSS.Tests.Tools;

public sealed class CalibCatalogTests
{
    [Fact]
    public void IsCalibTask_flag_or_type_prefix()
    {
        Assert.True(CalibCatalog.IsCalibTask(new MdkSetting.TaskConfig
        {
            Name = "a",
            Type = "flow",
            Parameters = { ["calib"] = "true" },
        }));
        Assert.True(CalibCatalog.IsCalibTask(new MdkSetting.TaskConfig
        {
            Name = "b",
            Type = "calib.ninepoint",
        }));
        Assert.False(CalibCatalog.IsCalibTask(new MdkSetting.TaskConfig
        {
            Name = "c",
            Type = "cycle",
        }));
    }

    [Fact]
    public void List_orders_by_group_then_display_name()
    {
        var setting = new MdkSetting
        {
            Tasks =
            [
                new() { Name = "z", Type = "flow", Parameters = { ["calib"] = "true", ["displayName"] = "平台", ["group"] = "流程" } },
                new() { Name = "a", Type = "calib.axisoffset", Parameters = { ["displayName"] = "轴偏置", ["group"] = "轴" } },
            ],
        };

        var list = CalibCatalog.List(setting);
        Assert.Equal(2, list.Count);
        Assert.Equal("z", list[0].Name);
        Assert.Equal("平台", CalibCatalog.DisplayName(list[0]));
        Assert.True(CalibCatalog.IsFlowKind(list[0].Type));
        Assert.Equal("MotionTask", CalibCatalog.KindLabel(list[1]));
    }
}
