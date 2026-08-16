namespace MDKOSS.Cef.Extensions;

/// <summary>
/// HMI 控件扩展包。内置控件与第三方控件走同一注册入口，
/// 不必改 <c>HmiWidgetCatalog</c> / <c>hmi_runtime.js</c>。
/// </summary>
public interface IHmiWidgetPackage
{
    string Id { get; }

    void Register(IHmiWidgetRegistration registration);
}

/// <summary>Facade used by <see cref="IHmiWidgetPackage"/> to add widget types.</summary>
public interface IHmiWidgetRegistration
{
    /// <summary>Registers one widget type. First registration for a type wins.</summary>
    void Widget(HmiWidgetDescriptor descriptor, HmiWidgetAssets? assets = null);

    /// <summary>Loads every <c>{type}/widget.json</c> under a widgets root folder.</summary>
    void Folder(string widgetsRoot);
}

/// <summary>Optional JS/CSS for a widget type (file path or inline text).</summary>
public sealed class HmiWidgetAssets
{
    public string? ScriptPath { get; init; }

    public string? CssPath { get; init; }

    public string? Script { get; init; }

    public string? Css { get; init; }
}
