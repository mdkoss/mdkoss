using MDKOSS.Core;
using MDKOSS.Core.Data;

namespace MDKOSS.Tests.Core;

public sealed class MdkConfigStoreTests
{
    private static string CreateTempDbPath() =>
        Path.Combine(Path.GetTempPath(), $"mdk-cfg-{Guid.NewGuid():N}.db");

    [Fact]
    public void ExportSetting_writes_all_config_tables()
    {
        var dbPath = CreateTempDbPath();
        var setting = new MdkSetting
        {
            ProjectName = "Export-Demo",
            CycleMs = 25,
            Drivers =
            [
                new MdkSetting.DriverConfig { Id = "drv1", Type = "sim", Enabled = true },
            ],
            Devices =
            [
                new MdkSetting.DeviceConfig
                {
                    Id = "gpio1",
                    Name = "GPIO",
                    Type = "gpio",
                    DriverId = "drv1",
                    Enabled = true,
                    Parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["in.start"] = "drv1:X0",
                        ["out.lamp"] = "drv1:Y0",
                    },
                },
            ],
            Axes =
            [
                new MdkSetting.DeviceConfig
                {
                    Id = "axis1",
                    Name = "Axis1",
                    Type = "axis",
                    DriverId = "drv1",
                    Enabled = true,
                },
            ],
            Platforms =
            [
                new MdkSetting.DeviceConfig
                {
                    Id = "plat1",
                    Name = "XY",
                    Type = "xy",
                    DriverId = "drv1",
                    Enabled = true,
                },
            ],
            Recipes =
            [
                new MdkSetting.RecipeConfig
                {
                    Id = "r1",
                    Name = "R1",
                    Vars = new Dictionary<string, object?> { ["k"] = "v" },
                },
            ],
        };

        using var store = new MdkConfigStore(dbPath);
        var result = store.ExportSetting(setting, "memory");

        Assert.Equal(1, result.Drivers);
        Assert.Equal(1, result.Devices);
        Assert.Equal(2, result.Gpios);
        Assert.Equal(1, result.Axis);
        Assert.Equal(1, result.Platform);
        Assert.True(result.SysConfigs >= 8);
        Assert.Equal(1, result.Recipes);
        Assert.True(result.Langs > 0);

        var counts = store.CountTables();
        Assert.Equal(1, counts.Drivers);
        Assert.Equal(1, counts.Devices);
        Assert.Equal(2, counts.Gpios);
        Assert.Equal(1, counts.Axis);
        Assert.Equal(1, counts.Platform);
        Assert.True(counts.Langs > 0);
        Assert.True(counts.Logs >= 1);
    }

    [Fact]
    public void ImportSetting_round_trips_drivers_devices_recipes()
    {
        var dbPath = CreateTempDbPath();
        var original = new MdkSetting
        {
            ProjectName = "RoundTrip",
            CycleMs = 40,
            ActiveRecipeId = "r1",
            Drivers =
            [
                new MdkSetting.DriverConfig
                {
                    Id = "sim",
                    Type = "sim",
                    Parameters = new Dictionary<string, string> { ["ip"] = "1.2.3.4" },
                },
            ],
            Devices =
            [
                new MdkSetting.DeviceConfig { Id = "gpio1", Name = "GPIO", Type = "gpio", DriverId = "sim" },
            ],
            Axes =
            [
                new MdkSetting.DeviceConfig { Id = "d1", Name = "Dev", Type = "axis", DriverId = "sim" },
            ],
            Tasks =
            [
                new MdkSetting.TaskConfig { Name = "poll", Type = "pollDriver", DriverId = "sim", IntervalMs = 100 },
            ],
            RecipeVarKeys = ["a"],
            Vars = new Dictionary<string, object?> { ["a"] = 1 },
            Recipes =
            [
                new MdkSetting.RecipeConfig { Id = "r1", Name = "Recipe", Vars = new Dictionary<string, object?> { ["a"] = 2 } },
            ],
        };

        using (var store = new MdkConfigStore(dbPath))
        {
            store.ExportSetting(original);
        }

        using var reader = new MdkConfigStore(dbPath);
        var loaded = reader.ImportSetting();

        Assert.Equal("RoundTrip", loaded.ProjectName);
        Assert.Equal(40, loaded.CycleMs);
        Assert.Equal("r1", loaded.ActiveRecipeId);
        Assert.Single(loaded.Drivers);
        Assert.Equal("sim", loaded.Drivers[0].Id);
        Assert.Equal("1.2.3.4", loaded.Drivers[0].Parameters["ip"]);
        Assert.Single(loaded.Devices);
        Assert.Equal("gpio", loaded.Devices[0].Type);
        Assert.Single(loaded.Axes);
        Assert.Equal("axis", loaded.Axes[0].Type);
        Assert.Single(loaded.Tasks);
        Assert.Equal("poll", loaded.Tasks[0].Name);
        Assert.Single(loaded.Recipes);
        Assert.Equal("Recipe", loaded.Recipes[0].Name);
    }
}
