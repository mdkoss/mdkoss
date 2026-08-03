using System.Text.Json;

namespace MDKOSS.Core;

/// <summary>
/// Registry for device action handlers supplied by extension assemblies.
/// </summary>
public static class DeviceActionRegistry
{
    private static readonly List<(Func<MDeviceBase, bool> Match, DeviceActionHandler Execute)> Handlers = [];

    public delegate DeviceActionResult DeviceActionHandler(
        MDeviceBase device,
        string action,
        Dictionary<string, JsonElement>? parameters);

    /// <summary>Registers a handler invoked when <paramref name="match"/> returns true.</summary>
    public static void Register(Func<MDeviceBase, bool> match, DeviceActionHandler execute)
    {
        ArgumentNullException.ThrowIfNull(match);
        ArgumentNullException.ThrowIfNull(execute);
        Handlers.Add((match, execute));
    }

    /// <summary>Runs the first matching handler, if any.</summary>
    public static bool TryExecute(
        MDeviceBase device,
        string action,
        Dictionary<string, JsonElement>? parameters,
        out DeviceActionResult result)
    {
        foreach (var (match, execute) in Handlers)
        {
            if (!match(device))
            {
                continue;
            }

            result = execute(device, action, parameters);
            return true;
        }

        result = default!;
        return false;
    }
}
