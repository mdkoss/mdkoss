using MDKOSS.Core;
using MDKOSS.Tools.Calib.Calib;

namespace MDKOSS.Tools.Calib.ViewModels;

public sealed class CalibItemViewModel : ObservableObject
{
    public CalibItemViewModel(MdkSetting.TaskConfig config)
    {
        Config = config;
    }

    public MdkSetting.TaskConfig Config { get; }

    public string Name => Config.Name;

    public string Title => CalibCatalog.DisplayName(Config);

    public string Group => CalibCatalog.DisplayGroup(Config);

    public string Kind => CalibCatalog.KindLabel(Config);

    public string Type => Config.Type;

    public bool IsFlow => CalibCatalog.IsFlowKind(Config.Type);

    public string Badge => IsFlow ? "Flow" : "Code";

    public void Refresh()
    {
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(Group));
        OnPropertyChanged(nameof(Kind));
        OnPropertyChanged(nameof(Type));
        OnPropertyChanged(nameof(IsFlow));
        OnPropertyChanged(nameof(Badge));
    }
}
