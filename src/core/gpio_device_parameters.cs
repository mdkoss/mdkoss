namespace MDKOSS.Core;

/// <summary>One GPIO point binding parsed from device <c>parameters</c> (<c>in.*</c> / <c>out.*</c> keys).</summary>
public readonly record struct GpioPointBinding(string Alias, string DriverId, string Address, bool IsOutput);

/// <summary>Parses GPIO routing entries from <see cref="MdkSetting.DeviceConfig.Parameters"/>.</summary>
public static class GpioDeviceParameterSet
{
    /// <summary>Parses <c>in.alias</c> and <c>out.alias</c> keys into point bindings; skips malformed routes.</summary>
    public static IReadOnlyList<GpioPointBinding> ParseBindings(IReadOnlyDictionary<string, string> parameters)
    {
        var list = new List<GpioPointBinding>();
        foreach (var kv in parameters)
        {
            var key = kv.Key;
            if (key.StartsWith("in.", StringComparison.OrdinalIgnoreCase))
            {
                var alias = key[3..];
                if (TryParsePointRoute(kv.Value, out var driverId, out var address))
                {
                    list.Add(new GpioPointBinding(alias, driverId, address, IsOutput: false));
                }
            }
            else if (key.StartsWith("out.", StringComparison.OrdinalIgnoreCase))
            {
                var alias = key[4..];
                if (TryParsePointRoute(kv.Value, out var driverId, out var address))
                {
                    list.Add(new GpioPointBinding(alias, driverId, address, IsOutput: true));
                }
            }
        }

        return list;
    }

    /// <summary>Parses <c>driverId:address</c> route syntax.</summary>
    public static bool TryParsePointRoute(string? raw, out string driverId, out string address)
    {
        driverId = string.Empty;
        address = string.Empty;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        var route = raw.Trim();
        var splitIndex = route.IndexOf(':');
        if (splitIndex <= 0 || splitIndex >= route.Length - 1)
        {
            return false;
        }

        driverId = route[..splitIndex].Trim();
        address = route[(splitIndex + 1)..].Trim();
        return !string.IsNullOrWhiteSpace(driverId) && !string.IsNullOrWhiteSpace(address);
    }
}
