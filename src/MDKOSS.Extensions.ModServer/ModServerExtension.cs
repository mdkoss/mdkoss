using MDKOSS.Core;
using MDKOSS.Core.Monitor;
using MDKOSS.Extensions;

namespace MDKOSS.Extensions.ModServer;

/// <summary>Modbus TCP server extension package (config type <c>devmodserver</c>).</summary>
public sealed class ModServerExtension : IMdkExtension
{
    public string Id => "modserver";

    public string DisplayName => "Modbus TCP Server";

    public void Register(IExtensionRegistration registration)
    {
        ArgumentNullException.ThrowIfNull(registration);

        registration.Device("devmodserver", (cfg, name, vars, _) =>
        {
            var parameters = ModServerDeviceParameters.ParseConfig(cfg.Parameters);
            return new ModServerDevice(cfg.Id, name, parameters, vars);
        });

        registration.Action(
            device => device is ModServerDevice,
            (device, action, parameters) =>
                ModServerDeviceActions.Execute((ModServerDevice)device, action, parameters));

        registration.MonitoringModule(runtime => new ModServerApiModule(runtime));
    }
}

/// <summary>Call once before creating <see cref="MdkRuntime"/> (or rely on plugin discovery).</summary>
public static class ModServerExtensionBootstrap
{
    public static void Register() => MdkExtensionHost.Register(new ModServerExtension());
}
