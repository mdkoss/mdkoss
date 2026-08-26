using MDKOSS.Core.Drivers;

namespace MDKOSS.Core;

/// <summary>
/// Registry for optional device types supplied by extension assemblies (e.g. serialdev, tcpdev).
/// </summary>
public static class DeviceExtensionRegistry
{
    public delegate MDeviceBase? DeviceFactory(
        MdkSetting.DeviceConfig config,
        string deviceName,
        MVarStore vars,
        IReadOnlyDictionary<string, IDriver> drivers);

    private static readonly Dictionary<string, DeviceFactory> Factories = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Registers a factory for a device type key (case-insensitive).</summary>
    public static void Register(string deviceType, DeviceFactory factory)
    {
        if (string.IsNullOrWhiteSpace(deviceType))
        {
            throw new ArgumentException("Device type cannot be empty.", nameof(deviceType));
        }

        ArgumentNullException.ThrowIfNull(factory);
        Factories[deviceType.Trim()] = factory;
    }

    /// <summary>Creates a device when a factory is registered for the type.</summary>
    public static bool TryCreate(
        string deviceType,
        MdkSetting.DeviceConfig config,
        string deviceName,
        MVarStore vars,
        IReadOnlyDictionary<string, IDriver> drivers,
        out MDeviceBase? device)
    {
        if (Factories.TryGetValue(deviceType, out var factory))
        {
            device = factory(config, deviceName, vars, drivers);
            return device is not null;
        }

        device = null;
        return false;
    }

    /// <summary>Returns whether a factory is registered for the type.</summary>
    public static bool IsRegistered(string? deviceType) =>
        !string.IsNullOrWhiteSpace(deviceType) && Factories.ContainsKey(deviceType.Trim());
}
