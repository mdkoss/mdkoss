using MDKOSS.Core;
using MDKOSS.Core.Drivers;
using MDKOSS.Core.Monitor;

namespace MDKOSS.Extensions;

/// <summary>
/// Unified registration facade over Core registries.
/// Extensions call these helpers instead of touching registries directly.
/// </summary>
public interface IExtensionRegistration
{
    /// <summary>Registers a device factory for a JSON <c>type</c> key.</summary>
    void Device(string deviceType, DeviceExtensionRegistry.DeviceFactory factory);

    /// <summary>Registers a unified device-action handler.</summary>
    void Action(Func<MDeviceBase, bool> match, DeviceActionRegistry.DeviceActionHandler execute);

    /// <summary>Registers an HTTP monitoring API module.</summary>
    void MonitoringModule(Func<MdkRuntime, MonitoringApiModule> factory);

    /// <summary>Registers a task factory for a JSON task <c>type</c> key.</summary>
    void Task(string type, Func<TaskBootstrapContext, MdkSetting.TaskConfig, string, MTaskBase?> factory);

    /// <summary>Registers a driver factory for a JSON driver <c>type</c> key.</summary>
    void Driver(string type, Func<IDriver> factory);

    /// <summary>Registers a static HTML page served by the monitoring server.</summary>
    void StaticPage(string path, Func<string> htmlFactory);
}

/// <summary>Default implementation that forwards to Core registries.</summary>
internal sealed class ExtensionRegistration : IExtensionRegistration
{
    public void Device(string deviceType, DeviceExtensionRegistry.DeviceFactory factory)
        => DeviceExtensionRegistry.Register(deviceType, factory);

    public void Action(Func<MDeviceBase, bool> match, DeviceActionRegistry.DeviceActionHandler execute)
        => DeviceActionRegistry.Register(match, execute);

    public void MonitoringModule(Func<MdkRuntime, MonitoringApiModule> factory)
        => MonitoringModuleRegistry.Register(factory);

    public void Task(string type, Func<TaskBootstrapContext, MdkSetting.TaskConfig, string, MTaskBase?> factory)
        => RuntimeTaskFactory.Register(type, factory);

    public void Driver(string type, Func<IDriver> factory)
        => DriverFactory.Register(type, factory);

    public void StaticPage(string path, Func<string> htmlFactory)
        => StaticPageRegistry.Register(path, htmlFactory);
}
