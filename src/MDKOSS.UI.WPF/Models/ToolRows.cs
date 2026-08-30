using Prism.Mvvm;

namespace MDKOSS.UI.WPF.Models;

public sealed class MatrixTile : BindableBase
{
    public string Id { get; init; } = "";
    public string Title { get; init; } = "";
    public string Meta { get; init; } = "";
    public string Mode { get; init; } = "";
}

public sealed class AxisRow : BindableBase
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string Type { get; init; } = "";
    public string Online { get; init; } = "";
    public string Enabled { get; init; } = "";
    public string Prf { get; init; } = "";
    public string Enc { get; init; } = "";
    public string Vel { get; init; } = "";
    public string Flags { get; init; } = "";
    public string State { get; init; } = "";
    public string Driver { get; init; } = "";
}

public sealed class PlatformRow : BindableBase
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string Type { get; init; } = "";
    public string Online { get; init; } = "";
    public string State { get; init; } = "";
    public int AxisCount { get; init; }
    public int EnabledCount { get; init; }
}

public sealed class PlatformAxisRow : BindableBase
{
    public string Letter { get; init; } = "";
    public string AxisId { get; init; } = "";
    public string Driver { get; init; } = "";
    public string Online { get; init; } = "";
    public string Enabled { get; init; } = "";
    public string Prf { get; init; } = "";
    public string Enc { get; init; } = "";
    public string Vel { get; init; } = "";
    public string Flags { get; init; } = "";
}

public sealed class IoLedGroup : BindableBase
{
    public string Title { get; init; } = "";
    public string Hint { get; init; } = "";
    public List<IoPointRow> Points { get; init; } = [];
}

public sealed class IoPointRow : BindableBase
{
    public string DeviceId { get; init; } = "";
    public string DeviceName { get; init; } = "";
    public string Alias { get; init; } = "";
    public string Direction { get; init; } = "";
    public string DriverId { get; init; } = "";
    public string Address { get; init; } = "";
    public string Online { get; init; } = "";
    public string Value { get; init; } = "";
    public bool IsOn { get; init; }
    public bool IsOutput { get; init; }
}

public sealed class CameraRow : BindableBase
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string Type { get; init; } = "";
    public string State { get; init; } = "";
    public string Online { get; init; } = "";
    public string Driver { get; init; } = "";
}

public sealed class VisionDefRow : BindableBase
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string Camera { get; init; } = "";
    public string Nodes { get; init; } = "";
    public string Description { get; init; } = "";
}

public sealed class AlarmMonitorRow : BindableBase
{
    public string Id { get; init; } = "";
    public string Code { get; init; } = "";
    public string Name { get; init; } = "";
    public string Level { get; init; } = "";
    public string Message { get; init; } = "";
    public string VarKey { get; init; } = "";
    public string Value { get; init; } = "";
    public bool Active { get; init; }
    public string ActiveText => Active ? "活动" : "";
}

public sealed class ManItemRow : BindableBase
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string Type { get; init; } = "";
    public string Desc { get; init; } = "";
    public bool Enabled { get; init; }
    public string Extra { get; init; } = "";
}

public sealed class ParamRow : BindableBase
{
    private string _key = "";
    private string _value = "";

    public string Key
    {
        get => _key;
        set => SetProperty(ref _key, value);
    }

    public string Value
    {
        get => _value;
        set => SetProperty(ref _value, value);
    }
}
