using MDKOSS.Core.Drivers;

namespace MDKOSS.Drivers.Dmc;

/// <summary>
/// Maps <see cref="DriverIoAddress"/> onto LTDMC general / axis IO.
/// Address <c>bit.{n}</c> is the card-native 0-based <c>bitno</c> / axis channel.
/// </summary>
public static class DmcIoMap
{
    /// <summary>Leadshine <c>dmc_axis_io_status</c> bits (DMC3000 / 5x10 family).</summary>
    public const uint AxisAlm = 0x0001;
    public const uint AxisElP = 0x0002;
    public const uint AxisElN = 0x0004;
    public const uint AxisEmg = 0x0008;
    public const uint AxisOrg = 0x0010;
    public const uint AxisSlP = 0x0020;
    public const uint AxisSlN = 0x0040;
    public const uint AxisInp = 0x0080;
    public const uint AxisEz = 0x0100;
    public const uint AxisRdy = 0x0200;
    public const uint AxisDstp = 0x0400;

    public static bool Test(uint word, uint mask) => (word & mask) != 0;

    /// <summary>
    /// Maps a native <c>dmc_axis_io_status</c> word plus motion/servo pins onto
    /// the GTS-aligned <see cref="AxisStatus"/> snapshot.
    /// </summary>
    public static AxisStatus ToAxisStatus(
        uint ioWord,
        bool servoOn,
        bool moving,
        double prfPosition = 0,
        double encPosition = 0,
        double velocity = 0) =>
        AxisStatus.Create(
            alarm: Test(ioWord, AxisAlm),
            positiveLimitLevel: Test(ioWord, AxisElP),
            positiveLimit: Test(ioWord, AxisElP) || Test(ioWord, AxisSlP),
            negativeLimit: Test(ioWord, AxisElN) || Test(ioWord, AxisSlN),
            smoothStop: Test(ioWord, AxisDstp),
            abruptStop: Test(ioWord, AxisEmg),
            servoOn: servoOn,
            moving: moving,
            inPosition: Test(ioWord, AxisInp),
            home: Test(ioWord, AxisOrg),
            prfPosition: prfPosition,
            encPosition: encPosition,
            velocity: velocity);

    public static bool IsGeneral(short type) =>
        type is GtsIoType.Gpi or GtsIoType.Gpo;

    public static bool TryNativeBit(short addressBit, out ushort bitno)
    {
        bitno = 0;
        if (!DriverIoAddress.IsDmcBit(addressBit))
        {
            return false;
        }

        bitno = (ushort)addressBit;
        return true;
    }

    /// <summary>
    /// Splits a native 0-based bit number into a 32-bit port index and shift.
    /// Used so many bit reads share one <c>dmc_read_inport</c> / <c>dmc_read_outport</c>.
    /// </summary>
    public static bool TrySplitPortBit(short addressBit, out ushort port, out int shift)
    {
        port = 0;
        shift = 0;
        if (!TryNativeBit(addressBit, out var bitno))
        {
            return false;
        }

        port = (ushort)(bitno / 32);
        shift = bitno % 32;
        return true;
    }

    public static bool TestPortBit(int word, int shift) =>
        shift is >= 0 and <= 31 && (word & (1 << shift)) != 0;

    public static bool TryAxisStatusMask(short type, out uint mask)
    {
        mask = type switch
        {
            GtsIoType.Alarm => AxisAlm,
            GtsIoType.LimitPositive => AxisElP,
            GtsIoType.LimitNegative => AxisElN,
            GtsIoType.Home => AxisOrg,
            GtsIoType.Arrive => AxisInp,
            _ => 0u,
        };
        return mask != 0;
    }

    public static bool IsServoEnable(short type) => type == GtsIoType.Enable;

    public static bool IsAlarmClear(short type) => type == GtsIoType.Clear;
}
