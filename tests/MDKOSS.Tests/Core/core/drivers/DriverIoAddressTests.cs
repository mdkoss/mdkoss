using MDKOSS.Core.Drivers;

namespace MDKOSS.Tests.Core.Drivers;

public sealed class DriverIoAddressTests
{
    [Theory]
    [InlineData("di.gpi.bit.1", false, GtsIoType.Gpi, (short)1)]
    [InlineData("do.gpo.bit.16", true, GtsIoType.Gpo, (short)16)]
    [InlineData("di.4.bit.3", false, GtsIoType.Gpi, (short)3)]
    [InlineData("do.12.bit.1", true, GtsIoType.Gpo, (short)1)]
    [InlineData("do.MC_GPO.bit.2", true, GtsIoType.Gpo, (short)2)]
    [InlineData("di.home.bit.1", false, GtsIoType.Home, (short)1)]
    [InlineData("do.gpo.bit.0", true, GtsIoType.Gpo, (short)0)]
    public void TryParse_bit_address(string address, bool isOutput, short type, short bit)
    {
        Assert.True(DriverIoAddress.TryParse(address, out var parsed));
        Assert.Equal(isOutput, parsed.IsOutput);
        Assert.Equal(type, parsed.Type);
        Assert.Equal(bit, parsed.BitIndex);
        Assert.True(parsed.IsBit);
    }

    [Theory]
    [InlineData("di.gpi", false, GtsIoType.Gpi)]
    [InlineData("do.12", true, GtsIoType.Gpo)]
    public void TryParse_port_address(string address, bool isOutput, short type)
    {
        Assert.True(DriverIoAddress.TryParse(address, out var parsed));
        Assert.Equal(isOutput, parsed.IsOutput);
        Assert.Equal(type, parsed.Type);
        Assert.Null(parsed.BitIndex);
        Assert.False(parsed.IsBit);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("0")]
    [InlineData("Y0")]
    [InlineData("do.gpo.bit.-1")]
    [InlineData("do.gpo.bit.256")]
    [InlineData("do.unknown.bit.1")]
    [InlineData("door.gpo.bit.1")]
    [InlineData("do.gpo.bit")]
    public void TryParse_rejects_invalid(string? address)
    {
        Assert.False(DriverIoAddress.TryParse(address, out _));
    }

    [Fact]
    public void LooksLike_requires_di_or_do_head()
    {
        Assert.True(DriverIoAddress.LooksLike("do.gpo.bit.1"));
        Assert.True(DriverIoAddress.LooksLike("di.4"));
        Assert.False(DriverIoAddress.LooksLike("door.1"));
        Assert.False(DriverIoAddress.LooksLike("Y0"));
    }

    [Fact]
    public void BitMask_is_gts_one_based()
    {
        Assert.Equal(1, DriverIoAddress.BitMask(1));
        Assert.Equal(2, DriverIoAddress.BitMask(2));
        Assert.Equal(0, DriverIoAddress.BitMask(0));
        Assert.True(DriverIoAddress.IsGtsBit(1));
        Assert.False(DriverIoAddress.IsGtsBit(0));
        Assert.True(DriverIoAddress.IsDmcBit(0));
        Assert.True(DriverIoAddress.TestBit(0b0100, 3));
        Assert.Equal(0b0101, DriverIoAddress.ApplyBit(0b0001, 3, true));
        Assert.Equal(0b0001, DriverIoAddress.ApplyBit(0b0101, 3, false));
    }
}
