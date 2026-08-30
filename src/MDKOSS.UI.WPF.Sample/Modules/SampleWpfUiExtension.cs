using MDKOSS.UI.WPF.Infrastructure;
using MDKOSS.UI.WPF.Sample.ViewModels;
using MDKOSS.UI.WPF.Sample.Views;

namespace MDKOSS.UI.WPF.Sample.Modules;

/// <summary>
/// 演示如何在不改 UI.WPF 内核的前提下加监控 / 调试页。
/// </summary>
public sealed class SampleWpfUiExtension : IWpfUiExtension
{
    public string Id => "sample-wpf-ui";

    public void RegisterUi(IWpfUiRegistration ui)
    {
        ArgumentNullException.ThrowIfNull(ui);
        ui.ToolPage<MonitorSampleExtView, MonitorSampleExtViewModel>("monitor_sampleext", "monitor", "扩展示例");
        ui.ToolPage<DebugSampleExtView, DebugSampleExtViewModel>("debug_sampleext", "debug", "扩展示例");
        ui.ToolPage<DebugTcpView, DebugTcpViewModel>("debug_tcp", "debug", "TCP");
        ui.ToolPage<DebugPyScriptView, DebugPyScriptViewModel>("debug_pyscript", "debug", "Python");
        ui.ToolPage<DebugModbusView, DebugModbusViewModel>("debug_modbus", "debug", "Modbus");
    }
}
