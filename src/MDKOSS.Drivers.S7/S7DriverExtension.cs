using MDKOSS.Core.Drivers;
using MDKOSS.Extensions;

namespace MDKOSS.Drivers.S7;

/// <summary>Siemens S7-1200 (ISO-on-TCP) driver plugin (config types <c>s7</c>, <c>s7-1200</c>).</summary>
public sealed class S7DriverExtension : IMdkExtension
{
    public string Id => "driver-s7";

    public string DisplayName => "Siemens S7-1200 PLC driver";

    public void Register(IExtensionRegistration registration)
    {
        ArgumentNullException.ThrowIfNull(registration);
        registration.Driver("s7", () => new DrvS7());
        registration.Driver("s7-1200", () => new DrvS7());
    }
}

/// <summary>Call once before creating <see cref="Core.MdkRuntime"/> when using S7 hardware.</summary>
public static class S7DriverBootstrap
{
    public static void Register() => MdkExtensionHost.Register(new S7DriverExtension());
}
