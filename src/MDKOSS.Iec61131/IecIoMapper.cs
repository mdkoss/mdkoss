using System.Globalization;
using System.Text.RegularExpressions;
using MDKOSS.Core;
using MDKOSS.Core.Drivers;

namespace MDKOSS.Iec61131;

/// <summary>Maps GPIO aliases to IEC %I/%Q addresses (S7-style bit packing).</summary>
public static class IecIoMapper
{
    private static readonly Regex S7Bit = new(
        @"^%?([IQ])(\d+)\.([0-7])$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static List<IecIoPoint> FromSetting(MdkSetting setting, List<IecNote> notes)
    {
        var points = new List<IecIoPoint>();
        var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var usedAt = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var driverBases = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var nextByte = 0;

        var gpioDevices = setting.Devices
            .Where(d => d.Enabled && string.Equals(d.Type, "gpio", StringComparison.OrdinalIgnoreCase))
            .ToList();

        foreach (var device in gpioDevices)
        {
            var bindings = GpioDeviceParameterSet.ParseBindings(device.Parameters, device.DriverId);
            foreach (var group in bindings.GroupBy(b => b.DriverId, StringComparer.OrdinalIgnoreCase))
            {
                if (!driverBases.TryGetValue(group.Key, out var byteBase))
                {
                    byteBase = nextByte;
                    driverBases[group.Key] = byteBase;
                    var maxBit = 0;
                    foreach (var b in group)
                    {
                        if (DriverIoAddress.TryParse(b.Address, out var parsed) && parsed.BitIndex is { } bit)
                        {
                            maxBit = Math.Max(maxBit, bit);
                        }
                    }

                    nextByte += Math.Max(4, (maxBit / 8) + 1);
                }

                foreach (var binding in group)
                {
                    var name = IecNames.Unique(
                        binding.IsOutput ? IecNames.IoOutput(binding.Alias) : IecNames.IoInput(binding.Alias),
                        usedNames);
                    var at = ToAtAddress(binding, byteBase, notes);
                    if (!usedAt.Add(at))
                    {
                        notes.Add(new IecNote
                        {
                            Severity = "warn",
                            Message = $"IO address {at} reused by {device.Id}/{binding.Alias}",
                        });
                    }

                    points.Add(new IecIoPoint
                    {
                        Name = name,
                        Alias = binding.Alias,
                        DeviceId = device.Id,
                        MdkAddress = binding.Address,
                        AtAddress = at,
                        IsOutput = binding.IsOutput,
                        Label = string.IsNullOrWhiteSpace(binding.Label) ? binding.Alias : binding.Label,
                    });
                }
            }
        }

        return points;
    }

    public static IecIoPoint? Find(IReadOnlyList<IecIoPoint> points, string deviceId, string alias)
    {
        return points.FirstOrDefault(p =>
            (string.IsNullOrWhiteSpace(deviceId)
             || string.Equals(p.DeviceId, deviceId, StringComparison.OrdinalIgnoreCase))
            && string.Equals(p.Alias, alias, StringComparison.OrdinalIgnoreCase));
    }

    public static string ToAtAddress(GpioPointBinding binding, int driverByteBase, List<IecNote> notes)
    {
        var raw = (binding.Address ?? "").Trim();
        var s7 = S7Bit.Match(raw);
        if (s7.Success)
        {
            var iq = s7.Groups[1].Value.ToUpperInvariant();
            return $"%{iq}{s7.Groups[2].Value}.{s7.Groups[3].Value}";
        }

        if (DriverIoAddress.TryParse(raw, out var parsed) && parsed.BitIndex is { } bit)
        {
            var abs = driverByteBase * 8 + bit;
            var by = abs / 8;
            var bi = abs % 8;
            var iq = binding.IsOutput ? "Q" : "I";
            return $"%{iq}{by.ToString(CultureInfo.InvariantCulture)}.{bi.ToString(CultureInfo.InvariantCulture)}";
        }

        notes.Add(new IecNote
        {
            Severity = "warn",
            Message = $"Cannot map IO address '{raw}' for alias '{binding.Alias}'; placed at %{(binding.IsOutput ? "Q" : "I")}{driverByteBase}.0",
        });
        return $"%{(binding.IsOutput ? "Q" : "I")}{driverByteBase.ToString(CultureInfo.InvariantCulture)}.0";
    }
}
