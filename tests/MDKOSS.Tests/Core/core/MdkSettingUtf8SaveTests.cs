using System.Text;
using MDKOSS.Core;

namespace MDKOSS.Tests.Core;

public sealed class MdkSettingUtf8SaveTests
{
    [Fact]
    public void Save_writes_cjk_as_utf8_literals_not_unicode_escapes()
    {
        var setting = new MdkSetting { ProjectName = "样机" };
        setting.Axes.Add(new MdkSetting.DeviceConfig
        {
            Id = "AxisTransY",
            Name = "上下料Y轴",
            Type = "axis",
        });
        setting.Devices.Add(new MdkSetting.DeviceConfig
        {
            Id = "gpio-main",
            Name = "整机 GPIO",
            Type = "gpio",
            Parameters =
            {
                ["in.DiStartButton"] = "drv-m1:1|启动按钮",
                ["in.DiStopButton"] = "drv-m1:2|停止按钮",
            },
        });

        var path = Path.Combine(Path.GetTempPath(), $"mdkoss-utf8-{Guid.NewGuid():N}.json");
        try
        {
            setting.Save(path);
            var json = File.ReadAllText(path, Encoding.UTF8);
            Assert.Contains("上下料Y轴", json, StringComparison.Ordinal);
            Assert.Contains("启动按钮", json, StringComparison.Ordinal);
            Assert.Contains("drv-m1:1|启动按钮", json, StringComparison.Ordinal);
            Assert.DoesNotContain("\u4E0A", json, StringComparison.Ordinal);
            Assert.DoesNotContain("\u542F", json, StringComparison.Ordinal);
            Assert.Contains("样机", json, StringComparison.Ordinal);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
