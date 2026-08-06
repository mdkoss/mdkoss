using System.Text.Json;
using MDKOSS.Core;

namespace MDKOSS.Tests.Core;

public sealed class MdkSettingTests
{
    [Fact]
    public void Load_parses_sample_shape()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "configs", "sample.setting.json");
        Assert.True(File.Exists(path), $"Missing sample settings at {path}");
        var setting = MdkSetting.Load(path);
        Assert.Equal("检测上下料机", setting.ProjectName);
        Assert.NotEmpty(setting.Drivers);
        Assert.Contains(setting.Drivers, d => string.Equals(d.Id, "drv-m1", StringComparison.OrdinalIgnoreCase));
        Assert.NotEmpty(setting.Devices);
        Assert.NotEmpty(setting.Axes);
        Assert.NotEmpty(setting.Platforms);
    }

    [Fact]
    public void NormalizeSections_moves_legacy_axis_platform_out_of_devices()
    {
        var setting = new MdkSetting
        {
            Devices =
            [
                new MdkSetting.DeviceConfig { Id = "gpio1", Type = "gpio" },
                new MdkSetting.DeviceConfig { Id = "ax1", Type = "axis" },
                new MdkSetting.DeviceConfig { Id = "plat1", Type = "xy" },
            ],
        };

        setting.NormalizeSections();

        Assert.Single(setting.Devices);
        Assert.Equal("gpio1", setting.Devices[0].Id);
        Assert.Single(setting.Axes);
        Assert.Equal("ax1", setting.Axes[0].Id);
        Assert.Single(setting.Platforms);
        Assert.Equal("plat1", setting.Platforms[0].Id);
    }

    [Fact]
    public void Deserialize_uses_case_insensitive_property_names()
    {
        const string json = """{"projectName":"P","drivers":[],"devices":[],"tasks":[],"vars":{}}""";
        var s = JsonSerializer.Deserialize<MdkSetting>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        Assert.NotNull(s);
        Assert.Equal("P", s.ProjectName);
    }
}
