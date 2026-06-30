namespace MDKOSS.Core.Monitor;

/// <summary>
/// Registry for optional monitoring API modules supplied by extension assemblies.
/// </summary>
public static class MonitoringModuleRegistry
{
    private static readonly List<Func<MdkRuntime, MonitoringApiModule>> Factories = [];

    /// <summary>Registers a factory that creates a monitoring module for a runtime instance.</summary>
    public static void Register(Func<MdkRuntime, MonitoringApiModule> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        Factories.Add(factory);
    }

    /// <summary>Creates all registered modules for the given runtime.</summary>
    public static IEnumerable<MonitoringApiModule> CreateModules(MdkRuntime runtime)
    {
        foreach (var factory in Factories)
        {
            yield return factory(runtime);
        }
    }
}
