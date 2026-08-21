using MDKOSS.Core;
using MDKOSS.Core.Monitor;
using MDKOSS.Extensions;

namespace MDKOSS.Extensions.ModServer;

/// <summary>
/// Modbus TCP extension package:
/// <list type="bullet">
/// <item><c>devmodserver</c> — local slave/server</item>
/// <item><c>devmodclient</c> — remote master/client (batch read)</item>
/// <item>driver <c>modbus</c> / <c>modbus-tcp</c> — <see cref="DrvModbus"/> IO backend</item>
/// </list>
/// </summary>
public sealed class ModServerExtension : IMdkExtension
{
    public string Id => "modserver";

    public string DisplayName => "Modbus TCP Server / Client / Driver";

    public void Register(IExtensionRegistration registration)
    {
        ArgumentNullException.ThrowIfNull(registration);

        registration.Driver("modbus", () => new DrvModbus());
        registration.Driver("modbus-tcp", () => new DrvModbus());

        registration.Device("devmodserver", (cfg, name, vars, _) =>
        {
            var parameters = ModServerDeviceParameters.ParseConfig(cfg.Parameters);
            return new ModServerDevice(cfg.Id, name, parameters, vars);
        });

        registration.Device("devmodclient", (cfg, name, vars, _) =>
        {
            var parameters = ModClientDeviceParameters.ParseConfig(cfg.Parameters);
            return new ModClientDevice(cfg.Id, name, parameters, vars);
        });

        registration.Action(
            device => device is ModServerDevice,
            (device, action, parameters) =>
                ModServerDeviceActions.Execute((ModServerDevice)device, action, parameters));

        registration.Action(
            device => device is ModClientDevice,
            (device, action, parameters) =>
                ModClientDeviceActions.Execute((ModClientDevice)device, action, parameters));

        registration.MonitoringModule(runtime => new ModServerApiModule(runtime));
        registration.MonitoringModule(runtime => new ModClientApiModule(runtime));
    }
}

/// <summary>Call once before creating <see cref="MdkRuntime"/> (or rely on plugin discovery).</summary>
public static class ModServerExtensionBootstrap
{
    public static void Register() => MdkExtensionHost.Register(new ModServerExtension());
}
