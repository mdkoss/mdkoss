using MDKOSS.Core;
using MDKOSS.Core.Drivers;
using MDKOSS.Drivers.Boards;

namespace MDKOSS.Tests.Core;

public sealed class DrvBoardsTests
{
    [Theory]
    [InlineData("zmc")]
    [InlineData("zmotion")]
    [InlineData("adt")]
    [InlineData("mpc")]
    [InlineData("emc")]
    [InlineData("gtn")]
    [InlineData("adlink")]
    [InlineData("advantech")]
    [InlineData("galil")]
    [InlineData("inovance")]
    public void Factory_registers_catalog_types(string type)
    {
        TestPluginBootstrap.EnsureRegistered();
        Assert.True(DriverFactory.IsSupported(type));
        Assert.Contains("driver-boards", MDKOSS.Extensions.MdkExtensionHost.RegisteredIds);

        using var drv = DriverFactory.Create(type);
        drv.Initialize(Config(type));
        Assert.True(drv.IsConnected);
        Assert.False(string.IsNullOrWhiteSpace(drv.Name));
    }

    [Fact]
    public void Zmc_simulate_do_bit_and_trap()
    {
        using var drv = new SimulatedCardDriver(BoardCatalog.Zmc);
        drv.Initialize(Config("zmc"));

        Assert.True(drv.Write("do.gpo.bit.0", true));
        Assert.True(drv.TryRead("do.gpo.bit.0", out var bit));
        Assert.True(Convert.ToBoolean(bit));

        Assert.True(drv.MoveAxisTrap(0, 1200, 100, 1000, 1000));
        Assert.True(drv.TryGetAxisPrfPosition(0, out var pos));
        Assert.Equal(1200, pos);
        Assert.True(drv.IsAxisEnabled(0));
    }

    [Fact]
    public void Gtn_default_io_bit_base_is_one()
    {
        using var drv = new SimulatedCardDriver(BoardCatalog.Gtn);
        drv.Initialize(Config("gtn"));
        Assert.True(drv.Write("do.gpo.bit.1", true));
        Assert.True(drv.TryReadDo(GtsIoType.Gpo, out var word));
        Assert.Equal(1, word & 1);
    }

    [Fact]
    public void Live_without_dll_stays_disconnected()
    {
        using var drv = new BoardCardDriver(BoardCatalog.Zmc);
        drv.Initialize(new MdkSetting.DriverConfig
        {
            Id = "zmc",
            Type = "zmc",
            Parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["simulate"] = "false",
            },
        });
        Assert.False(drv.IsConnected);
        Assert.True(drv.TryRead("driver.lastError", out var err));
        var text = Convert.ToString(err) ?? "";
        Assert.True(
            text.Contains("native_dll_missing", StringComparison.OrdinalIgnoreCase)
            || text.Contains("open_failed", StringComparison.OrdinalIgnoreCase),
            text);
    }

    [Fact]
    public void Factory_live_without_dll_stays_disconnected()
    {
        TestPluginBootstrap.EnsureRegistered();
        using var drv = DriverFactory.Create("adt");
        drv.Initialize(new MdkSetting.DriverConfig
        {
            Id = "adt",
            Type = "adt",
            Parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["simulate"] = "false",
            },
        });
        Assert.False(drv.IsConnected);
    }

    [Fact]
    public void Catalog_covers_market_types()
    {
        Assert.Equal(10, BoardCatalog.All.Count);
        Assert.True(BoardCatalog.TryGet("ZMC", out var zmc));
        Assert.Equal("正运动 Zmotion", zmc.Vendor);
    }

    private static MdkSetting.DriverConfig Config(string type) => new()
    {
        Id = type,
        Type = type,
        Enabled = true,
        Parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["simulate"] = "true",
        },
    };
}
