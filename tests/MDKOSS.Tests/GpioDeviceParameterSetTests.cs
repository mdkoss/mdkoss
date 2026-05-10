using MDKOSS.Core;

namespace MDKOSS.Tests;

public sealed class GpioDeviceParameterSetTests
{
    [Fact]
    public void TryParsePointRoute_accepts_driver_colon_address()
    {
        Assert.True(GpioDeviceParameterSet.TryParsePointRoute("drv-main:X0", out var d, out var a));
        Assert.Equal("drv-main", d);
        Assert.Equal("X0", a);
    }

    [Fact]
    public void ParseBindings_reads_in_and_out_keys()
    {
        var parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["in.start"] = "d1:I0",
            ["out.lamp"] = "d1:O1",
            ["ignored"] = "x",
        };

        var bindings = GpioDeviceParameterSet.ParseBindings(parameters);
        Assert.Equal(2, bindings.Count);
        Assert.Contains(bindings, b => b.Alias == "start" && !b.IsOutput && b.DriverId == "d1" && b.Address == "I0");
        Assert.Contains(bindings, b => b.Alias == "lamp" && b.IsOutput);
    }

    [Fact]
    public void ParseDriverScopeIds_returns_null_when_missing()
    {
        Assert.Null(GpioDeviceParameterSet.ParseDriverScopeIds(new Dictionary<string, string>()));
    }

    [Fact]
    public void ParseDriverScopeIds_parses_comma_separated_ids()
    {
        var parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["driverIds"] = " drv-a , drv-b ",
        };

        var scope = GpioDeviceParameterSet.ParseDriverScopeIds(parameters);
        Assert.NotNull(scope);
        Assert.Contains("drv-a", scope);
        Assert.Contains("drv-b", scope);
    }
}
