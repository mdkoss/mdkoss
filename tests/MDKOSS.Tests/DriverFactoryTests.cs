using MDKOSS.Core;
using MDKOSS.Core.Drivers;

namespace MDKOSS.Tests;

public sealed class DriverFactoryTests
{
    [Fact]
    public void Create_sim_returns_connected_driver()
    {
        var d = DriverFactory.Create("sim");
        d.Initialize(new MdkSetting.DriverConfig { Id = "x", Type = "sim" });
        Assert.True(d.IsConnected);
        d.Dispose();
    }

    [Fact]
    public void Create_unknown_throws_mdk_exception()
    {
        var ex = Assert.Throws<MdkException>(() => DriverFactory.Create("no-such-driver"));
        Assert.Equal(MdkErrorCode.UnsupportedDriverType, ex.Code);
    }
}
