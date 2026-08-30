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
        Assert.True(DriverFactory.IsSupported("dmc"));
        Assert.Contains("dmc", DriverFactory.RegisteredTypes);
        Assert.True(DriverFactory.IsSupported("s7"));
        Assert.Contains("s7", DriverFactory.RegisteredTypes);
        Assert.True(DriverFactory.IsSupported("s7-1200"));
        Assert.True(DriverFactory.IsSupported("modbus"));
        Assert.Contains("modbus", DriverFactory.RegisteredTypes);
        Assert.True(DriverFactory.IsSupported("modbus-tcp"));
        Assert.True(DriverFactory.IsSupported("zmc"));
        Assert.True(DriverFactory.IsSupported("gtn"));
        Assert.Contains("zmc", DriverFactory.RegisteredTypes);
    }

    [Fact]
    public void Discovery_registers_expected_extensions()
    {
        Assert.Contains("driver-sim", MDKOSS.Extensions.MdkExtensionHost.RegisteredIds);
        Assert.Contains("driver-dmc", MDKOSS.Extensions.MdkExtensionHost.RegisteredIds);
        Assert.Contains("driver-s7", MDKOSS.Extensions.MdkExtensionHost.RegisteredIds);
        Assert.Contains("driver-boards", MDKOSS.Extensions.MdkExtensionHost.RegisteredIds);
        Assert.Contains("modserver", MDKOSS.Extensions.MdkExtensionHost.RegisteredIds);
        Assert.Contains("serial", MDKOSS.Extensions.MdkExtensionHost.RegisteredIds);
        Assert.Contains("tcp", MDKOSS.Extensions.MdkExtensionHost.RegisteredIds);
        Assert.Contains("camera", MDKOSS.Extensions.MdkExtensionHost.RegisteredIds);
    }
}
