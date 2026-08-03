using MDKOSS.Extensions;

namespace MDKOSS.Drivers.Dmc;

/// <summary>
/// DMC / LTDMC native bindings package.
/// Registers when an <see cref="Core.Drivers.IDriver"/> wrapper is available;
/// currently exposes <c>csLTDMC.LTDMC</c> P/Invoke only.
/// </summary>
public sealed class DmcDriverExtension : IMdkExtension
{
    public string Id => "driver-dmc";

    public string DisplayName => "DMC (LTDMC) native bindings";

    public void Register(IExtensionRegistration registration)
    {
        ArgumentNullException.ThrowIfNull(registration);
        // IDriver wrapper not yet implemented — native API lives in csLTDMC.LTDMC.
        // When DrvDmc is added: registration.Driver("dmc", () => new DrvDmc());
    }
}

/// <summary>Call once before creating <see cref="Core.MdkRuntime"/> when using DMC hardware.</summary>
public static class DmcDriverBootstrap
{
    public static void Register() => MdkExtensionHost.Register(new DmcDriverExtension());
}
