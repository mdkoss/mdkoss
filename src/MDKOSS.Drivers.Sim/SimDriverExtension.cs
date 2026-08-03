using MDKOSS.Core.Drivers;
using MDKOSS.Extensions;

namespace MDKOSS.Drivers.Sim;

/// <summary>Simulation motion-card driver plugin (config type <c>sim</c>).</summary>
public sealed class SimDriverExtension : IMdkExtension
{
    public string Id => "driver-sim";

    public string DisplayName => "Simulation driver";

    public void Register(IExtensionRegistration registration)
    {
        ArgumentNullException.ThrowIfNull(registration);
        registration.Driver("sim", () => new DrvSim());
    }
}

/// <summary>Call once before creating <see cref="Core.MdkRuntime"/>.</summary>
public static class SimDriverBootstrap
{
    public static void Register() => MdkExtensionHost.Register(new SimDriverExtension());
}
