using MDKOSS.Extensions;

namespace MDKOSS.Sample.Modbus.Machine;

/// <summary>
/// Modbus IDriver 测试扩展：API <c>/api/modbusdrv</c>、页面 <c>indexModbus.html</c>（200 holding）。
/// </summary>
public sealed class ModbusDriverSampleExtension : IMdkExtension
{
    public string Id => "sample-modbus-driver";

    public string DisplayName => "Sample Modbus IDriver Holding Test";

    public void Register(IExtensionRegistration registration)
    {
        ArgumentNullException.ThrowIfNull(registration);

        registration.MonitoringModule(runtime => new ModbusDriverApiModule(runtime));
        registration.StaticPage("/indexModbus.html", () => ModbusDriverViewPages.IndexHtml);
    }
}
