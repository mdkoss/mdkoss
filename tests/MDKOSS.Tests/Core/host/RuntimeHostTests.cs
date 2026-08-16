using MDKOSS.Host;

namespace MDKOSS.Tests.Core;

public sealed class RuntimeHostTests
{
    [Fact]
    public void ResolveDefaultSettingPath_uses_first_json_in_output_configs()
    {
        var path = RuntimeHost.ResolveDefaultSettingPath();
        Assert.True(File.Exists(path), $"Missing default settings at {path}");
        Assert.Equal(".json", Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);
        Assert.False(path.EndsWith(".layout.json", StringComparison.OrdinalIgnoreCase));
        Assert.False(path.EndsWith(".hmi.json", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ResolveSettingPath_keeps_explicit_argument()
    {
        var specified = Path.Combine(AppContext.BaseDirectory, "configs", "sample.setting.json");
        Assert.True(File.Exists(specified), $"Missing {specified}");
        var resolved = RuntimeHost.ResolveSettingPath(["--setting", specified]);
        Assert.Equal(Path.GetFullPath(specified), resolved);
    }
}
