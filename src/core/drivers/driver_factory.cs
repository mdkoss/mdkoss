namespace MDKOSS.Core.Drivers;

/// <summary>
/// Registry for <see cref="IDriver"/> implementations keyed by config <c>type</c> string.
/// </summary>
public static class DriverFactory
{
    private static readonly Dictionary<string, Func<IDriver>> Factories = new(StringComparer.OrdinalIgnoreCase)
    {
        ["gts"] = () => new DrvGts(),
        ["sim"] = () => new DrvSim(),
    };

    /// <summary>Registers or replaces a driver factory for the given type key.</summary>
    public static void Register(string type, Func<IDriver> factory)
    {
        if (string.IsNullOrWhiteSpace(type))
        {
            throw new ArgumentException("Driver type cannot be empty.", nameof(type));
        }

        ArgumentNullException.ThrowIfNull(factory);
        Factories[type.Trim()] = factory;
    }

    /// <summary>Returns whether a factory exists for the type.</summary>
    public static bool IsSupported(string? type)
    {
        return !string.IsNullOrWhiteSpace(type) && Factories.ContainsKey(type.Trim());
    }

    /// <summary>Creates a new driver instance for the given type.</summary>
    /// <exception cref="NotSupportedException">Unknown type.</exception>
    public static IDriver Create(string type)
    {
        if (string.IsNullOrWhiteSpace(type))
        {
            throw new NotSupportedException("Driver type is empty.");
        }

        if (!Factories.TryGetValue(type.Trim(), out var factory))
        {
            throw new NotSupportedException($"Unsupported driver type: {type}");
        }

        return factory();
    }
}
