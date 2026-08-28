using MDKOSS.Extensions;

namespace MDKOSS.Sample.Modbus.Machine;

/// <summary>
/// Modbus IDriver 测试扩展：API <c>/api/modbusdrv</c>、
/// 默认页 <c>debug_modbus_holding.html</c>（200 holding）、PLC 组态页 <c>indexModbus.html</c>。
/// </summary>
public sealed class ModbusDriverSampleExtension : IMdkExtension
{
    public string Id => "sample-modbus-driver";

    public string DisplayName => "Sample Modbus IDriver Holding Test";

    public void Register(IExtensionRegistration registration)
    {
        ArgumentNullException.ThrowIfNull(registration);

        registration.MonitoringModule(runtime => new ModbusDriverApiModule(runtime));
        registration.StaticPage("/debug_modbus_holding.html", () => ModbusDriverViewPages.HoldingHtml);
        registration.StaticPage("/indexModbus.html", () => ModbusDriverViewPages.IndexHtml);
    }
}
