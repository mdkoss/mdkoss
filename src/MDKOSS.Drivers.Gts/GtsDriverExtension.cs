using MDKOSS.Core.Drivers;
using MDKOSS.Extensions;

namespace MDKOSS.Drivers.Gts;

/// <summary>GTS motion-card driver plugin (config type <c>gts</c>).</summary>
public sealed class GtsDriverExtension : IMdkExtension
{
    public string Id => "driver-gts";

    public string DisplayName => "GTS motion driver";

    public void Register(IExtensionRegistration registration)
    {
        ArgumentNullException.ThrowIfNull(registration);
        registration.Driver("gts", () => new DrvGts());
    }
}

/// <summary>Call once before creating <see cref="Core.MdkRuntime"/>.</summary>
public static class GtsDriverBootstrap
{
    public static void Register() => MdkExtensionHost.Register(new GtsDriverExtension());
}
