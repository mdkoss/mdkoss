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
    public const uint AxisOrg = 0x0010;
    public const uint AxisInp = 0x0080;

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
