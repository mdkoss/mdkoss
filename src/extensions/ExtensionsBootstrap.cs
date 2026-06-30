using MDKOSS.Core;
using MDKOSS.Core.Monitor;

namespace MDKOSS.Extensions;

/// <summary>Registers serial/TCP extension devices, actions, and monitoring modules.</summary>
public static class ExtensionsBootstrap
{
    /// <summary>Registers all built-in extension components. Call once before creating <see cref="MdkRuntime"/>.</summary>
    public static void Register()
    {
        DeviceExtensionRegistry.Register("serialdev", (cfg, name, vars, _) =>
        {
            var serialConfig = SerialDeviceParameterSet.ParseConfig(cfg.Parameters);
            return new SerialDevice(cfg.Id, name, serialConfig, vars);
        });

        DeviceExtensionRegistry.Register("tcpdev", (cfg, name, vars, _) =>
        {
            var tcpConfig = TcpDeviceParameterSet.ParseConfig(cfg.Parameters);
            return new TcpDevice(cfg.Id, name, tcpConfig, vars);
        });

        DeviceActionRegistry.Register(
            device => device is SerialDevice,
            (device, action, parameters) =>
                ExtensionDeviceActions.ExecuteSerial((SerialDevice)device, action, parameters));

        DeviceActionRegistry.Register(
            device => device is TcpDevice,
            (device, action, parameters) =>
                ExtensionDeviceActions.ExecuteTcp((TcpDevice)device, action, parameters));

        MonitoringModuleRegistry.Register(runtime => new SerialApiModule(runtime));
        MonitoringModuleRegistry.Register(runtime => new TcpApiModule(runtime));
    }
}
