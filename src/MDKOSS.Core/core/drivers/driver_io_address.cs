using System.Globalization;

namespace MDKOSS.Core.Drivers;

/// <summary>
/// GTS-style digital IO type codes used in <see cref="DriverIoAddress"/>
/// (<c>di.{type}</c> / <c>do.{type}</c>). Names match the Googol MC_* constants.
/// </summary>
public static class GtsIoType
{
    public const short LimitPositive = 0;
    public const short LimitNegative = 1;
    public const short Alarm = 2;
    public const short Home = 3;
    public const short Gpi = 4;
    public const short Arrive = 5;
    public const short Enable = 10;
    public const short Clear = 11;
    public const short Gpo = 12;

    /// <summary>Resolves a numeric type or name (<c>gpi</c>, <c>gpo</c>, <c>home</c>, …).</summary>
    public static bool TryResolve(string? token, out short type)
    {
        type = 0;
        if (string.IsNullOrWhiteSpace(token))
        {
            return false;
        }

        var key = token.Trim();
        if (short.TryParse(key, NumberStyles.Integer, CultureInfo.InvariantCulture, out type))
        {
            return true;
        }

        type = key.ToLowerInvariant() switch
        {
            "gpi" or "mc_gpi" => Gpi,
            "gpo" or "mc_gpo" => Gpo,
            "home" or "mc_home" => Home,
            "alarm" or "mc_alarm" => Alarm,
            "enable" or "mc_enable" => Enable,
            "clear" or "mc_clear" => Clear,
            "arrive" or "mc_arrive" => Arrive,
            "limit+" or "limitp" or "limit_positive" or "mc_limit_positive" => LimitPositive,
            "limit-" or "limitn" or "limit_negative" or "mc_limit_negative" => LimitNegative,
            _ => short.MinValue,
        };
        return type != short.MinValue;
    }
}

/// <summary>
/// Digital IO address parsed from <see cref="IDriver.TryRead"/> / <see cref="IDriver.Write"/>.
/// GPIO <c>in.*</c>/<c>out.*</c> address fields must use this form.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><c>di.{type}</c> / <c>do.{type}</c> — whole port (int bitmask)</item>
/// <item><c>di.{type}.bit.{n}</c> / <c>do.{type}.bit.{n}</c> — one bit; <c>n</c> is the card-native index (GTS 1-based, DMC 0-based, SIM <c>ioBitBase</c>)</item>
/// </list>
/// <c>{type}</c> is a number or name: <c>gpi</c>(4), <c>gpo</c>(12), <c>home</c>(3), <c>alarm</c>(2),
/// <c>enable</c>(10), <c>clear</c>(11), <c>arrive</c>(5), <c>limit+</c>(0), <c>limit-</c>(1).
/// </remarks>
public readonly record struct DriverIoAddress(bool IsOutput, short Type, short? BitIndex)
{
    /// <summary>Parser floor: cards may start at 0 (DMC) or 1 (GTS).</summary>
    public const short MinBit = 0;

    public const short MaxBit = 255;

    /// <summary>固高 <c>GT_SetDoBit</c> 点号从 1 起。</summary>
    public const short GtsMinBit = 1;

    /// <summary>雷赛 <c>dmc_read_inbit</c> / <c>dmc_write_outbit</c> 点号从 0 起。</summary>
    public const short DmcMinBit = 0;

    public bool IsBit => BitIndex is not null;

    public static bool LooksLike(string? address)
    {
        if (string.IsNullOrWhiteSpace(address))
        {
            return false;
        }

        var dot = address.IndexOf('.');
        if (dot <= 0)
        {
            return false;
        }

        var head = address.AsSpan(0, dot);
        return head.Equals("di", StringComparison.OrdinalIgnoreCase)
            || head.Equals("do", StringComparison.OrdinalIgnoreCase);
    }

    public static bool TryParse(string? address, out DriverIoAddress parsed)
    {
        parsed = default;
        if (string.IsNullOrWhiteSpace(address))
        {
            return false;
        }

        var parts = address.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length is not (2 or 4))
        {
            return false;
        }

        var isOutput = parts[0].Equals("do", StringComparison.OrdinalIgnoreCase);
        if (!isOutput && !parts[0].Equals("di", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!GtsIoType.TryResolve(parts[1], out var type))
        {
            return false;
        }

        if (parts.Length == 2)
        {
            parsed = new DriverIoAddress(isOutput, type, BitIndex: null);
            return true;
        }

        if (!parts[2].Equals("bit", StringComparison.OrdinalIgnoreCase)
            || !short.TryParse(parts[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out var bit)
            || bit is < MinBit or > MaxBit)
        {
            return false;
        }

        parsed = new DriverIoAddress(isOutput, type, bit);
        return true;
    }

    public static bool IsGtsBit(short bit) => bit is >= GtsMinBit and <= 32;

    public static bool IsDmcBit(short bit) => bit is >= DmcMinBit and <= MaxBit;

    /// <summary>GTS port-word mask for a 1-based bit index.</summary>
    public static int BitMask(short gtsBit) => gtsBit < GtsMinBit ? 0 : 1 << (gtsBit - 1);

    public static bool TestBit(int word, short gtsBit) => (word & BitMask(gtsBit)) != 0;

    public static int ApplyBit(int word, short gtsBit, bool value)
    {
        var mask = BitMask(gtsBit);
        return value ? word | mask : word & ~mask;
    }
}
