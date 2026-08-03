using MDKOSS.Core;
using MDKOSS.Core.Drivers;

namespace MDKOSS.Tests.Core;

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

    [Fact]
    public void Sim_driver_type_is_registered()
    {
        Assert.True(DriverFactory.IsSupported("sim"));
        Assert.Contains("sim", DriverFactory.RegisteredTypes);
    }

    [Fact]
    public void Discovery_registers_expected_extensions()
    {
        Assert.Contains("driver-sim", MDKOSS.Extensions.MdkExtensionHost.RegisteredIds);
        Assert.Contains("serial", MDKOSS.Extensions.MdkExtensionHost.RegisteredIds);
        Assert.Contains("tcp", MDKOSS.Extensions.MdkExtensionHost.RegisteredIds);
        Assert.Contains("camera", MDKOSS.Extensions.MdkExtensionHost.RegisteredIds);
    }
}
