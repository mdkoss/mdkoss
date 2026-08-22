using System.Globalization;
using MDKOSS.Core.Drivers;

namespace MDKOSS.Sample.Modbus.Machine;

/// <summary>
/// Batch read/write helper for Modbus IDriver native addresses <c>holding.{n}</c>.
/// Default span is 200 holding registers (0..199) for the sample UI.
/// </summary>
public static class HoldingRegisterBank
{
    public const int DefaultCount = 200;
    public const int MaxCount = 2000;

    public static HoldingRegisterSnapshot Read(
        IDriver driver,
        int start = 0,
        int count = DefaultCount)
    {
        ArgumentNullException.ThrowIfNull(driver);
        start = Math.Clamp(start, 0, ushort.MaxValue);
        count = Math.Clamp(count, 1, MaxCount);
        if (start + count - 1 > ushort.MaxValue)
        {
            count = ushort.MaxValue - start + 1;
        }

        var values = new ushort[count];
        var ok = 0;
        for (var i = 0; i < count; i++)
        {
            var address = $"holding.{start + i}";
            if (driver.TryRead(address, out var raw) && TryToUInt16(raw, out var word))
            {
                values[i] = word;
                ok++;
            }
        }

        return new HoldingRegisterSnapshot(start, values, ok, driver.IsConnected);
    }

    public static bool WriteOne(IDriver driver, int address, ushort value)
    {
        ArgumentNullException.ThrowIfNull(driver);
        if (address < 0 || address > ushort.MaxValue)
        {
            return false;
        }

        return driver.Write($"holding.{address}", value);
    }

    public static int WriteMany(IDriver driver, int start, IReadOnlyList<ushort> values)
    {
        ArgumentNullException.ThrowIfNull(driver);
        ArgumentNullException.ThrowIfNull(values);
        start = Math.Clamp(start, 0, ushort.MaxValue);
        var written = 0;
        for (var i = 0; i < values.Count; i++)
        {
            var addr = start + i;
            if (addr > ushort.MaxValue)
            {
                break;
            }

            if (driver.Write($"holding.{addr}", values[i]))
            {
                written++;
            }
        }

        return written;
    }

    public static int FillPattern(IDriver driver, int start = 0, int count = DefaultCount)
    {
        ArgumentNullException.ThrowIfNull(driver);
        start = Math.Clamp(start, 0, ushort.MaxValue);
        count = Math.Clamp(count, 1, MaxCount);
        var written = 0;
        for (var i = 0; i < count; i++)
        {
            var addr = start + i;
            if (addr > ushort.MaxValue)
            {
                break;
            }

            // Distinct, easy-to-spot pattern: address index in low byte + high nibble tag.
            ushort value = (ushort)((0xA0 << 8) | (addr & 0xFF));
            if (driver.Write($"holding.{addr}", value))
            {
                written++;
            }
        }

        return written;
    }

    private static bool TryToUInt16(object? raw, out ushort value)
    {
        value = 0;
        if (raw is null)
        {
            return false;
        }

        try
        {
            value = Convert.ToUInt16(raw, CultureInfo.InvariantCulture);
            return true;
        }
        catch
        {
            return false;
        }
    }
}

/// <param name="Start">First holding register address.</param>
/// <param name="Values">Register values in address order.</param>
/// <param name="OkCount">Successful reads.</param>
/// <param name="Connected">Driver online flag at read time.</param>
public sealed record HoldingRegisterSnapshot(
    int Start,
    IReadOnlyList<ushort> Values,
    int OkCount,
    bool Connected);
