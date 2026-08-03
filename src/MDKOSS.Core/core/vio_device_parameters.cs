namespace MDKOSS.Core;

/// <summary>One virtual IO point declared in <see cref="MdkSetting.DeviceConfig.Parameters"/>.</summary>
public readonly record struct VioPointBinding(string Alias, bool IsOutput);

/// <summary>Parses virtual GPIO (<c>vio</c> device) bindings: <c>in.*</c> / <c>out.*</c> with empty or <c>virtual</c> values.</summary>
public static class VioDeviceParameterSet
{
    /// <summary>
    /// Accepts the same key shape as GPIO (<c>in.alias</c>, <c>out.alias</c>). Value must be empty/whitespace or <c>virtual</c>
    /// (physical <c>driverId:address</c> routes are not allowed on <c>vio</c> devices).
    /// </summary>
    public static IReadOnlyList<VioPointBinding> ParseVirtualBindings(IReadOnlyDictionary<string, string> parameters)
    {
        var list = new List<VioPointBinding>();
        foreach (var kv in parameters)
        {
            var key = kv.Key;
            if (string.Equals(key, "driverIds", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (key.StartsWith("in.", StringComparison.OrdinalIgnoreCase))
            {
                var alias = key[3..];
                EnsureVirtualValue(key, kv.Value);
                list.Add(new VioPointBinding(alias, IsOutput: false));
            }
            else if (key.StartsWith("out.", StringComparison.OrdinalIgnoreCase))
            {
                var alias = key[4..];
                EnsureVirtualValue(key, kv.Value);
                list.Add(new VioPointBinding(alias, IsOutput: true));
            }
        }

        return list;
    }

    private static void EnsureVirtualValue(string parameterKey, string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return;
        }

        if (string.Equals(raw.Trim(), "virtual", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (GpioDeviceParameterSet.TryParsePointRoute(raw, out _, out _))
        {
            throw new MdkException(
                MdkErrorCode.VioBindingInvalid,
                $"VIO parameter '{parameterKey}' must be empty or 'virtual', not a physical route ({raw}).");
        }

        throw new MdkException(
            MdkErrorCode.VioBindingInvalid,
            $"VIO parameter '{parameterKey}' has unsupported value '{raw}' (use empty or 'virtual').");
    }
}
