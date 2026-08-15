using MDKOSS.Core;

namespace MDKOSS.Tests.Core;

public sealed class GpioDeviceParameterSetTests
{
    [Fact]
    public void TryParsePointRoute_accepts_unified_pipe_and_legacy_colon()
    {
        Assert.True(GpioDeviceParameterSet.TryParsePointRoute("drv-main|X0", out var d, out var a));
        Assert.Equal("drv-main", d);
        Assert.Equal("X0", a);

        Assert.True(GpioDeviceParameterSet.TryParsePointRoute("drv-main:X0", out d, out a));
        Assert.Equal("drv-main", d);
        Assert.Equal("X0", a);
    }

    [Fact]
    public void TryParsePointValue_accepts_short_address_with_default_driver_and_label()
    {
        Assert.True(GpioDeviceParameterSet.TryParsePointValue(
            "0|急停", "drv-m1", out var d, out var a, out var label));
        Assert.Equal("drv-m1", d);
        Assert.Equal("0", a);
        Assert.Equal("急停", label);
    }

    [Fact]
    public void TryParsePointValue_accepts_driver_io_bit_address()
    {
        Assert.True(GpioDeviceParameterSet.TryParsePointValue(
            "drv-m1|do.gpo.bit.1|绿灯", null, out var d, out var a, out var label));
        Assert.Equal("drv-m1", d);
        Assert.Equal("do.gpo.bit.1", a);
        Assert.Equal("绿灯", label);

        Assert.True(GpioDeviceParameterSet.TryParsePointRoute("drv-io1|di.gpi.bit.12", out d, out a));
        Assert.Equal("drv-io1", d);
        Assert.Equal("di.gpi.bit.12", a);
    }

    [Fact]
    public void TryParsePointValue_accepts_unified_driver_address_label()
    {
        Assert.True(GpioDeviceParameterSet.TryParsePointValue(
            "drv-io1|12|Vaccum2", null, out var d, out var a, out var label));
        Assert.Equal("drv-io1", d);
        Assert.Equal("12", a);
        Assert.Equal("Vaccum2", label);
    }

    [Fact]
    public void TryParsePointValue_accepts_legacy_colon_form()
    {
        Assert.True(GpioDeviceParameterSet.TryParsePointValue(
            "drv-io1:12|Vaccum2", null, out var d, out var a, out var label));
        Assert.Equal("drv-io1", d);
        Assert.Equal("12", a);
        Assert.Equal("Vaccum2", label);
    }

    [Fact]
    public void FormatPointValue_uses_only_pipe_separators()
    {
        var v = GpioDeviceParameterSet.FormatPointValue("drv-m1", "3", "复位按钮", "drv-m1");
        Assert.Equal("drv-m1|3|复位按钮", v);
        Assert.DoesNotContain(':', v);
        Assert.Equal(2, v.Count(c => c == GpioDeviceParameterSet.LabelSeparator));
    }

    [Fact]
    public void NormalizeParameters_unifies_legacy_colon_to_pipe()
    {
        var parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["in.DiEstopButton"] = "drv-m1:0｜急停",
            ["in.DiAirPressure"] = "drv-io1:8|ap",
            ["out.lamp"] = "d1:1",
            ["desc.lamp"] = "灯",
            ["driverIds"] = "d1,d2",
        };

        var normalized = GpioDeviceParameterSet.NormalizeParameters(parameters, "d1");
        Assert.Equal("drv-m1|0|急停", normalized["in.DiEstopButton"]);
        Assert.Equal("drv-io1|8|ap", normalized["in.DiAirPressure"]);
        Assert.Equal("d1|1|灯", normalized["out.lamp"]);
        Assert.Equal("d1,d2", normalized["driverIds"]);
        Assert.False(normalized.ContainsKey("desc.lamp"));
        Assert.All(normalized.Where(kv => kv.Key.StartsWith("in.") || kv.Key.StartsWith("out.")),
            kv => Assert.DoesNotContain(':', kv.Value));
    }

    [Fact]
    public void IsVioDriverType_detects_vio()
    {
        Assert.True(GpioDeviceParameterSet.IsVioDriverType("vio"));
        Assert.True(GpioDeviceParameterSet.IsVioDriverType("VIO"));
        Assert.False(GpioDeviceParameterSet.IsVioDriverType("sim"));
    }

    [Fact]
    public void ParseBindings_reads_in_and_out_keys()
    {
        var parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["in.start"] = "d1|I0",
            ["out.lamp"] = "d1|O1|灯",
            ["ignored"] = "x",
        };

        var bindings = GpioDeviceParameterSet.ParseBindings(parameters);
        Assert.Equal(2, bindings.Count);
        Assert.Contains(bindings, b => b.Alias == "start" && !b.IsOutput && b.DriverId == "d1" && b.Address == "I0");
        Assert.Contains(bindings, b => b.Alias == "lamp" && b.IsOutput && b.Label == "灯");
    }

    [Fact]
    public void ParseBindings_uses_default_driver_for_short_io()
    {
        var parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["in.DiStartButton"] = "1|启动按钮",
        };

        var bindings = GpioDeviceParameterSet.ParseBindings(parameters, "drv-m1");
        Assert.Single(bindings);
        Assert.Equal("drv-m1", bindings[0].DriverId);
        Assert.Equal("1", bindings[0].Address);
        Assert.Equal("启动按钮", bindings[0].Label);
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
