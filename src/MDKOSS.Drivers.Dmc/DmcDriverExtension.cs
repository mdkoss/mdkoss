using MDKOSS.Core.Drivers;
using MDKOSS.Extensions;

namespace MDKOSS.Drivers.Dmc;

/// <summary>DMC / LTDMC motion-card driver plugin (config type <c>dmc</c>).</summary>
public sealed class DmcDriverExtension : IMdkExtension
{
    public string Id => "driver-dmc";

    public string DisplayName => "DMC (LTDMC) motion driver";

    public void Register(IExtensionRegistration registration)
    {
        ArgumentNullException.ThrowIfNull(registration);
        registration.Driver("dmc", () => new DrvDmc());
    }
}

/// <summary>Call once before creating <see cref="Core.MdkRuntime"/> when using DMC hardware.</summary>
public static class DmcDriverBootstrap
{
    public static void Register() => MdkExtensionHost.Register(new DmcDriverExtension());
}
