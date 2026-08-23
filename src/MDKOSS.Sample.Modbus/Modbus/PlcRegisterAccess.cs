using System.Globalization;
using System.Text.Json;
using MDKOSS.Core.Drivers;

namespace MDKOSS.Sample.Modbus.Machine;

/// <summary>Typed read/write for catalog points: reg, regi, regf, bit (plus di/do as 16-bit).</summary>
public static class PlcRegisterAccess
{
    public static object? Decode(PlcRegisterPoint point, IReadOnlyList<ushort> words, int start)
    {
        ArgumentNullException.ThrowIfNull(point);
        ArgumentNullException.ThrowIfNull(words);
        var idx = point.Address - start;
        if (idx < 0 || idx >= words.Count)
        {
            return null;
        }

        var loAddr = idx + 1;
        return point.Type switch
        {
            "regi" when loAddr < words.Count => WordsToInt(words[idx], words[loAddr]),
            "regf" when loAddr < words.Count => WordsToFloat(words[idx], words[loAddr]),
            "bit" when point.Bit is int bit => ((words[idx] >> bit) & 1) != 0,
            _ => words[idx],
        };
    }

    public static bool TryRead(IDriver driver, PlcRegisterPoint point, out object? value)
    {
        ArgumentNullException.ThrowIfNull(driver);
        ArgumentNullException.ThrowIfNull(point);
        value = null;
        if (!TryReadWord(driver, point.Address, out var hi))
        {
            return false;
        }

        switch (point.Type)
        {
            case "regi":
                if (!TryReadWord(driver, point.Address + 1, out var loI))
                {
                    return false;
                }

                value = WordsToInt(hi, loI);
                return true;
            case "regf":
                if (!TryReadWord(driver, point.Address + 1, out var loF))
                {
                    return false;
                }

                value = WordsToFloat(hi, loF);
                return true;
            case "bit":
                if (point.Bit is not int bit || bit is < 0 or > 15)
                {
                    return false;
                }

                value = ((hi >> bit) & 1) != 0;
                return true;
            default:
                value = hi;
                return true;
        }
    }

    public static bool TryWrite(IDriver driver, PlcRegisterPoint point, JsonElement value)
    {
        ArgumentNullException.ThrowIfNull(driver);
        ArgumentNullException.ThrowIfNull(point);
        if (point.Type == "di")
        {
            return false;
        }

        return point.Type switch
        {
            "regi" => TryWriteRegi(driver, point.Address, value),
            "regf" => TryWriteRegf(driver, point.Address, value),
            "bit" => TryWriteBit(driver, point.Address, point.Bit ?? 0, value),
            _ => TryWriteReg(driver, point.Address, value),
        };
    }

    public static bool TryWriteReg(IDriver driver, int address, JsonElement value)
    {
        if (!TryParseUInt16(value, out var word))
        {
            return false;
        }

        return HoldingRegisterBank.WriteOne(driver, address, word);
    }

    public static bool TryWriteRegi(IDriver driver, int address, JsonElement value)
    {
        if (!TryParseInt32(value, out var n))
        {
            return false;
        }

        var bits = unchecked((uint)n);
        var hi = (ushort)(bits >> 16);
        var lo = (ushort)bits;
        return HoldingRegisterBank.WriteOne(driver, address, hi)
            && HoldingRegisterBank.WriteOne(driver, address + 1, lo);
    }

    public static bool TryWriteRegf(IDriver driver, int address, JsonElement value)
    {
        if (!TryParseFloat(value, out var f))
        {
            return false;
        }

        var bits = BitConverter.SingleToUInt32Bits(f);
        var hi = (ushort)(bits >> 16);
        var lo = (ushort)bits;
        return HoldingRegisterBank.WriteOne(driver, address, hi)
            && HoldingRegisterBank.WriteOne(driver, address + 1, lo);
    }

    public static bool TryWriteBit(IDriver driver, int address, int bit, JsonElement value)
    {
        if (bit is < 0 or > 15 || !TryParseBool(value, out var on))
        {
            return false;
        }

        if (!TryReadWord(driver, address, out var word))
        {
            word = 0;
        }

        var next = on
            ? (ushort)(word | (1 << bit))
            : (ushort)(word & ~(1 << bit));
        return HoldingRegisterBank.WriteOne(driver, address, next);
    }

    public static int WordsToInt(ushort hi, ushort lo)
        => unchecked((int)(((uint)hi << 16) | lo));

    public static float WordsToFloat(ushort hi, ushort lo)
        => BitConverter.UInt32BitsToSingle(((uint)hi << 16) | lo);

    public static (ushort Hi, ushort Lo) IntToWords(int value)
    {
        var bits = unchecked((uint)value);
        return ((ushort)(bits >> 16), (ushort)bits);
    }

    public static (ushort Hi, ushort Lo) FloatToWords(float value)
    {
        var bits = BitConverter.SingleToUInt32Bits(value);
        return ((ushort)(bits >> 16), (ushort)bits);
    }

    private static bool TryReadWord(IDriver driver, int address, out ushort word)
    {
        word = 0;
        if (!driver.TryRead($"holding.{address}", out var raw) || raw is null)
        {
            return false;
        }

        try
        {
            word = Convert.ToUInt16(raw, CultureInfo.InvariantCulture);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryParseUInt16(JsonElement value, out ushort word)
    {
        word = 0;
        switch (value.ValueKind)
        {
            case JsonValueKind.Number:
                if (value.TryGetUInt16(out word))
                {
                    return true;
                }

                if (value.TryGetInt32(out var n) && n is >= 0 and <= 65535)
                {
                    word = (ushort)n;
                    return true;
                }

                return false;
            case JsonValueKind.String:
                var s = value.GetString()?.Trim() ?? "";
                if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
                    && ushort.TryParse(s[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out word))
                {
                    return true;
                }

                return ushort.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out word)
                    || (int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i)
                        && i is >= 0 and <= 65535 && (word = (ushort)i) == word);
            case JsonValueKind.True:
                word = 1;
                return true;
            case JsonValueKind.False:
                word = 0;
                return true;
            default:
                return false;
        }
    }

    private static bool TryParseInt32(JsonElement value, out int n)
    {
        n = 0;
        return value.ValueKind switch
        {
            JsonValueKind.Number => value.TryGetInt32(out n)
                || (value.TryGetInt64(out var l) && l is >= int.MinValue and <= int.MaxValue && (n = (int)l) == n),
            JsonValueKind.String => int.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out n),
            _ => false,
        };
    }

    private static bool TryParseFloat(JsonElement value, out float f)
    {
        f = 0;
        return value.ValueKind switch
        {
            JsonValueKind.Number => value.TryGetSingle(out f) || (value.TryGetDouble(out var d) && (f = (float)d) == f),
            JsonValueKind.String => float.TryParse(value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out f),
            _ => false,
        };
    }

    private static bool TryParseBool(JsonElement value, out bool on)
    {
        on = false;
        switch (value.ValueKind)
        {
            case JsonValueKind.True:
                on = true;
                return true;
            case JsonValueKind.False:
                on = false;
                return true;
            case JsonValueKind.Number:
                if (value.TryGetInt32(out var n))
                {
                    on = n != 0;
                    return true;
                }

                return false;
            case JsonValueKind.String:
                var s = value.GetString()?.Trim() ?? "";
                if (s is "1" or "true" or "TRUE" or "on" or "ON")
                {
                    on = true;
                    return true;
                }

                if (s is "0" or "false" or "FALSE" or "off" or "OFF")
                {
                    on = false;
                    return true;
                }

                return false;
            default:
                return false;
        }
    }
}
