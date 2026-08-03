using MDKOSS.Core.Monitor;
using MDKOSS.Extensions;

namespace MDKOSS.Extensions.Camera;

/// <summary>
/// Camera device extension package (config type <c>extcamera</c>).
/// Register via <see cref="MdkExtensionHost"/> or <see cref="CameraExtensionBootstrap"/>.
/// </summary>
public sealed class CameraExtension : IMdkExtension
{
    public string Id => "camera";

    public string DisplayName => "Extension Camera (sim)";

    public void Register(IExtensionRegistration registration)
    {
        ArgumentNullException.ThrowIfNull(registration);

        registration.Device("extcamera", (cfg, name, vars, _) =>
        {
            var parameters = ExtCameraDeviceParameters.ParseConfig(cfg.Parameters);
            return new ExtCameraDevice(cfg.Id, name, parameters, vars);
        });

        registration.Action(
            device => device is ExtCameraDevice,
            (device, action, parameters) =>
                ExtCameraDeviceActions.Execute((ExtCameraDevice)device, action, parameters));

        registration.MonitoringModule(runtime => new ExtCameraApiModule(runtime));
    }
}

/// <summary>Convenience bootstrap mirroring other extension packages / <c>PnpBootstrap</c>.</summary>
public static class CameraExtensionBootstrap
{
    /// <summary>Call once before creating <see cref="Core.MdkRuntime"/>.</summary>
    public static void Register()
    {
        MdkExtensionHost.Register(new CameraExtension());
    }
}
