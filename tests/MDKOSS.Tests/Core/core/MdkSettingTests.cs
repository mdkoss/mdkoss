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
        Assert.Equal("MDKOSS-Demo", setting.ProjectName);
        Assert.NotEmpty(setting.Drivers);
        Assert.Contains(setting.Drivers, d => string.Equals(d.Id, "drv-main", StringComparison.OrdinalIgnoreCase));
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
