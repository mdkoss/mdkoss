using System.Collections.ObjectModel;
using MDKOSS.Core;
using MDKOSS.UI.WPF.Infrastructure;
using MDKOSS.UI.WPF.Models;
using MDKOSS.UI.WPF.Services;
using Prism.Commands;
using Prism.Ioc;
using Prism.Mvvm;
using Prism.Navigation.Regions;

namespace MDKOSS.UI.WPF.ViewModels.Tools.Man;

public abstract class ManCatalogViewModel : BindableBase, INavigationAware
{
    private ManItemRow? _selected;
    private string _filter = "";
    private string _editId = "";
    private string _editName = "";
    private string _editType = "";
    private string _editDriverId = "";
    private string _editDesc = "";
    private string _editValue = "";
    private string _editCode = "";
    private string _editLevel = "error";
    private string _editOp = "eq";
    private string _editVarKey = "";
    private string _editCamera = "";
    private bool _editEnabled = true;
    private bool _editLatch;
    private int _editInterval = 100;
    private string _status = "";

    protected ManCatalogViewModel(IRuntimeUiService runtime)
    {
        Runtime = runtime;
        AddCommand = new DelegateCommand(Add);
        DeleteCommand = new DelegateCommand(Delete);
        DuplicateCommand = new DelegateCommand(Duplicate);
        ApplyCommand = new DelegateCommand(Apply);
        SaveCommand = new DelegateCommand(Save);
        AddParamCommand = new DelegateCommand(() => Parameters.Add(new ParamRow { Key = "key" }));
        RemoveParamCommand = new DelegateCommand(() =>
        {
            if (Parameters.Count > 0)
            {
                Parameters.RemoveAt(Parameters.Count - 1);
            }
        });
        GoToolCommand = new DelegateCommand<string>(id =>
            ContainerLocator.Container.Resolve<IToolNavigator>().NavigateByPage(id));
    }

    public DelegateCommand<string> GoToolCommand { get; }

    protected IRuntimeUiService Runtime { get; }
    protected MdkSetting Setting => Runtime.Runtime.Setting;

    public abstract string Title { get; }
    public abstract string Note { get; }
    public virtual bool CanCreate => true;
    public virtual bool ShowId => true;
    public virtual bool ShowName => true;
    public virtual bool ShowType => true;
    public virtual bool ShowDriver => false;
    public virtual bool ShowEnabled => true;
    public virtual bool ShowInterval => false;
    public virtual bool ShowDesc => false;
    public virtual bool ShowValue => false;
    public virtual bool ShowParams => true;
    public virtual bool ShowCode => false;
    public virtual bool ShowLevel => false;
    public virtual bool ShowOp => false;
    public virtual bool ShowVarKey => false;
    public virtual bool ShowLatch => false;
    public virtual bool ShowCamera => false;
    public virtual string IdLabel => "Id";
    public virtual string NameLabel => "名称 / 描述";
    public virtual string ParamTitle => "Parameters (Key / Value)";

    public ObservableCollection<ManItemRow> Items { get; } = [];
    public ObservableCollection<ParamRow> Parameters { get; } = [];

    public string Filter
    {
        get => _filter;
        set
        {
            if (SetProperty(ref _filter, value))
            {
                RefreshList();
            }
        }
    }

    public ManItemRow? Selected
    {
        get => _selected;
        set
        {
            if (SetProperty(ref _selected, value))
            {
                LoadForm();
            }
        }
    }

    public string EditId { get => _editId; set => SetProperty(ref _editId, value); }
    public string EditName { get => _editName; set => SetProperty(ref _editName, value); }
    public string EditType { get => _editType; set => SetProperty(ref _editType, value); }
    public string EditDriverId { get => _editDriverId; set => SetProperty(ref _editDriverId, value); }
    public string EditDesc { get => _editDesc; set => SetProperty(ref _editDesc, value); }
    public string EditValue { get => _editValue; set => SetProperty(ref _editValue, value); }
    public string EditCode { get => _editCode; set => SetProperty(ref _editCode, value); }
    public string EditLevel { get => _editLevel; set => SetProperty(ref _editLevel, value); }
    public string EditOp { get => _editOp; set => SetProperty(ref _editOp, value); }
    public string EditVarKey { get => _editVarKey; set => SetProperty(ref _editVarKey, value); }
    public string EditCamera { get => _editCamera; set => SetProperty(ref _editCamera, value); }
    public bool EditEnabled { get => _editEnabled; set => SetProperty(ref _editEnabled, value); }
    public bool EditLatch { get => _editLatch; set => SetProperty(ref _editLatch, value); }
    public int EditInterval { get => _editInterval; set => SetProperty(ref _editInterval, value); }

    public string Status
    {
        get => _status;
        protected set => SetProperty(ref _status, value);
    }

    public DelegateCommand AddCommand { get; }
    public DelegateCommand DeleteCommand { get; }
    public DelegateCommand DuplicateCommand { get; }
    public DelegateCommand ApplyCommand { get; }
    public DelegateCommand SaveCommand { get; }
    public DelegateCommand AddParamCommand { get; }
    public DelegateCommand RemoveParamCommand { get; }

    public void OnNavigatedTo(NavigationContext navigationContext) => RefreshList();

    public bool IsNavigationTarget(NavigationContext navigationContext) => true;

    public void OnNavigatedFrom(NavigationContext navigationContext)
    {
    }

    protected abstract IEnumerable<ManItemRow> Enumerate();

    protected abstract void LoadItem(string key);

    protected abstract void ApplyItem(string originalKey);

    protected abstract void CreateItem();

    protected abstract void RemoveItem(string key);

    protected abstract void DuplicateItem(string key);

    protected void RefreshList()
    {
        var keep = Selected?.Id;
        var q = Filter.Trim();
        Items.Clear();
        foreach (var row in Enumerate())
        {
            if (!string.IsNullOrEmpty(q)
                && !$"{row.Id} {row.Name} {row.Type} {row.Desc} {row.Extra}"
                    .Contains(q, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            Items.Add(row);
        }

        Selected = Items.FirstOrDefault(r => string.Equals(r.Id, keep, StringComparison.OrdinalIgnoreCase))
                   ?? Items.FirstOrDefault();
    }

    protected void LoadParams(IDictionary<string, string>? src)
    {
        Parameters.Clear();
        foreach (var kv in src ?? new Dictionary<string, string>())
        {
            Parameters.Add(new ParamRow { Key = kv.Key, Value = kv.Value });
        }
    }

    protected void LoadParams(IDictionary<string, object?>? src)
    {
        Parameters.Clear();
        foreach (var kv in src ?? new Dictionary<string, object?>())
        {
            Parameters.Add(new ParamRow { Key = kv.Key, Value = kv.Value?.ToString() ?? "" });
        }
    }

    protected Dictionary<string, string> ReadParams()
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in Parameters)
        {
            if (!string.IsNullOrWhiteSpace(p.Key))
            {
                map[p.Key.Trim()] = p.Value;
            }
        }

        return map;
    }

    protected Dictionary<string, object?> ReadObjectParams()
    {
        var map = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var kv in ReadParams())
        {
            map[kv.Key] = kv.Value;
        }

        return map;
    }

    protected static string UniqueId(IEnumerable<string> existing, string prefix)
    {
        var set = new HashSet<string>(existing.Where(s => !string.IsNullOrWhiteSpace(s)), StringComparer.OrdinalIgnoreCase);
        var n = 1;
        while (set.Contains($"{prefix}-{n}"))
        {
            n++;
        }

        return $"{prefix}-{n}";
    }

    protected void Toast(string message, bool ok = true) =>
        Status = (ok ? "" : "失败：") + message;

    private void LoadForm()
    {
        if (Selected is null)
        {
            EditId = EditName = EditType = EditDriverId = EditDesc = EditValue = EditCode = EditVarKey = EditCamera = "";
            EditLevel = "error";
            EditOp = "eq";
            EditEnabled = true;
            EditLatch = false;
            EditInterval = 100;
            Parameters.Clear();
            return;
        }

        LoadItem(Selected.Id);
    }

    private void Add()
    {
        if (!CanCreate)
        {
            return;
        }

        CreateItem();
        RefreshList();
        Toast("已新建（尚未保存到磁盘）");
    }

    private void Delete()
    {
        if (!CanCreate || Selected is null || !DeviceKind.ConfirmWrite($"确认删除 {Selected.Id}？"))
        {
            return;
        }

        RemoveItem(Selected.Id);
        RefreshList();
        Toast("已删除（尚未保存到磁盘）");
    }

    private void Duplicate()
    {
        if (!CanCreate || Selected is null)
        {
            return;
        }

        DuplicateItem(Selected.Id);
        RefreshList();
        Toast("已复制（尚未保存到磁盘）");
    }

    private void Apply()
    {
        if (Selected is null)
        {
            Toast("未选择条目", false);
            return;
        }

        ApplyItem(Selected.Id);
        RefreshList();
        Toast("已应用到内存配置，需保存并重启后对现场设备生效");
    }

    private void Save()
    {
        if (Selected is not null)
        {
            ApplyItem(Selected.Id);
        }

        Runtime.TrySaveSetting(out var err);
        RefreshList();
        Toast(err ?? "已保存到磁盘（现场设备需重启运行时）", err is null);
    }
}

public sealed class ManMachineViewModel : BindableBase, INavigationAware
{
    private readonly IRuntimeUiService _runtime;
    private string _projectName = "";
    private string _cycleMs = "20";
    private string _monitoringPrefix = "";
    private string _startPage = "";
    private string _databasePath = "";
    private string _machineId = "";
    private string _machineType = "";
    private string _status = "";

    public ManMachineViewModel(IRuntimeUiService runtime)
    {
        _runtime = runtime;
        ApplyCommand = new DelegateCommand(Apply);
        SaveCommand = new DelegateCommand(Save);
        GoToolCommand = new DelegateCommand<string>(id =>
            ContainerLocator.Container.Resolve<IToolNavigator>().NavigateByPage(id));
    }

    public DelegateCommand<string> GoToolCommand { get; }

    public string ProjectName { get => _projectName; set => SetProperty(ref _projectName, value); }
    public string CycleMs { get => _cycleMs; set => SetProperty(ref _cycleMs, value); }
    public string MonitoringPrefix { get => _monitoringPrefix; set => SetProperty(ref _monitoringPrefix, value); }
    public string StartPage { get => _startPage; set => SetProperty(ref _startPage, value); }
    public string DatabasePath { get => _databasePath; set => SetProperty(ref _databasePath, value); }
    public string MachineId { get => _machineId; set => SetProperty(ref _machineId, value); }
    public string MachineType { get => _machineType; set => SetProperty(ref _machineType, value); }
    public string SettingPath => _runtime.Runtime.SettingPath ?? "—";
    public string Status { get => _status; private set => SetProperty(ref _status, value); }
    public DelegateCommand ApplyCommand { get; }
    public DelegateCommand SaveCommand { get; }

    public void OnNavigatedTo(NavigationContext navigationContext) => Load();

    public bool IsNavigationTarget(NavigationContext navigationContext) => true;

    public void OnNavigatedFrom(NavigationContext navigationContext)
    {
    }

    private void Load()
    {
        var s = _runtime.Runtime.Setting;
        ProjectName = s.ProjectName;
        CycleMs = s.CycleMs.ToString();
        MonitoringPrefix = s.MonitoringPrefix ?? "";
        StartPage = s.StartPage ?? "";
        DatabasePath = s.DatabasePath ?? "";
        MachineId = s.MachineId ?? "";
        MachineType = s.MachineType ?? "";
        RaisePropertyChanged(nameof(SettingPath));
    }

    private void Apply()
    {
        var s = _runtime.Runtime.Setting;
        s.ProjectName = ProjectName.Trim();
        s.CycleMs = int.TryParse(CycleMs, out var ms) ? Math.Max(1, ms) : s.CycleMs;
        s.MonitoringPrefix = NullIfBlank(MonitoringPrefix);
        s.StartPage = NullIfBlank(StartPage);
        s.DatabasePath = NullIfBlank(DatabasePath);
        s.MachineId = NullIfBlank(MachineId);
        s.MachineType = NullIfBlank(MachineType);
        Status = "已应用到内存配置，需保存并重启后生效";
    }

    private void Save()
    {
        Apply();
        _runtime.TrySaveSetting(out var err);
        Status = err is null ? "已保存到磁盘（现场设备需重启运行时）" : "失败：" + err;
    }

    private static string? NullIfBlank(string value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public sealed class ManDriverViewModel : ManCatalogViewModel
{
    public ManDriverViewModel(IRuntimeUiService runtime) : base(runtime) { }

    public override string Title => "驱动配置";
    public override string Note => "增删驱动与参数。写入磁盘后需重启运行时才会重建驱动实例。";
    public override string IdLabel => "Name (Id)";
    public override string NameLabel => "Desc (描述)";

    protected override IEnumerable<ManItemRow> Enumerate() =>
        Setting.Drivers.Select(d => new ManItemRow
        {
            Id = d.Id,
            Name = d.Name,
            Type = d.Type,
            Enabled = d.Enabled,
            Extra = d.Enabled ? "启用" : "禁用",
        });

    protected override void LoadItem(string key)
    {
        var d = Find(key);
        if (d is null)
        {
            return;
        }

        EditId = d.Id;
        EditName = d.Name;
        EditType = d.Type;
        EditEnabled = d.Enabled;
        LoadParams(d.Parameters);
    }

    protected override void ApplyItem(string originalKey)
    {
        var d = Find(originalKey);
        if (d is null)
        {
            return;
        }

        d.Id = EditId.Trim();
        d.Name = EditName.Trim();
        d.Type = EditType.Trim();
        d.Enabled = EditEnabled;
        d.Parameters = ReadParams();
    }

    protected override void CreateItem()
    {
        var id = UniqueId(Setting.Drivers.Select(d => d.Id), "drv");
        Setting.Drivers.Add(new MdkSetting.DriverConfig { Id = id, Name = id, Type = "gts" });
    }

    protected override void RemoveItem(string key) => Setting.Drivers.RemoveAll(d => d.Id == key);

    protected override void DuplicateItem(string key)
    {
        var d = Find(key);
        if (d is null)
        {
            return;
        }

        var id = UniqueId(Setting.Drivers.Select(x => x.Id), d.Id);
        Setting.Drivers.Add(new MdkSetting.DriverConfig
        {
            Id = id,
            Name = d.Name,
            Type = d.Type,
            Enabled = d.Enabled,
            Parameters = new Dictionary<string, string>(d.Parameters, StringComparer.OrdinalIgnoreCase),
        });
    }

    private MdkSetting.DriverConfig? Find(string id) =>
        Setting.Drivers.FirstOrDefault(d => string.Equals(d.Id, id, StringComparison.OrdinalIgnoreCase));
}

public sealed class ManDeviceViewModel : ManDeviceCatalogViewModel
{
    public ManDeviceViewModel(IRuntimeUiService runtime) : base(runtime) { }

    public override string Title => "设备配置";
    public override string Note => "通用设备（相机/GPIO/串口等）。轴与平台请用对应页。";
    protected override List<MdkSetting.DeviceConfig> Source => Setting.Devices;
    protected override string DefaultType => "gpio";
    protected override string IdPrefix => "dev";
}

public sealed class ManAxisViewModel : ManDeviceCatalogViewModel
{
    public ManAxisViewModel(IRuntimeUiService runtime) : base(runtime) { }

    public override string Title => "轴配置";
    public override string Note => "编辑 axes 集合。保存后需重启运行时。";
    protected override List<MdkSetting.DeviceConfig> Source => Setting.Axes;
    protected override string DefaultType => "axis";
    protected override string IdPrefix => "axis";
}

public sealed class ManPlatformViewModel : ManDeviceCatalogViewModel
{
    public ManPlatformViewModel(IRuntimeUiService runtime) : base(runtime) { }

    public override string Title => "平台配置";
    public override string Note => "编辑 platforms 集合。保存后需重启运行时。";
    public override bool ShowDriver => false;
    protected override List<MdkSetting.DeviceConfig> Source => Setting.Platforms;
    protected override string DefaultType => "platform";
    protected override string IdPrefix => "plat";
}

public sealed class ManGpioViewModel : ManDeviceCatalogViewModel
{
    public ManGpioViewModel(IRuntimeUiService runtime) : base(runtime) { }

    public override string Title => "GPIO / VIO 配置";
    public override string Note => "筛选 Devices 中 type=gpio/vio 的点位设备。";
    protected override List<MdkSetting.DeviceConfig> Source => Setting.Devices;
    protected override string DefaultType => "gpio";
    protected override string IdPrefix => "gpio";
    protected override bool Include(MdkSetting.DeviceConfig d) =>
        d.Type is "gpio" or "vio";
}

public abstract class ManDeviceCatalogViewModel : ManCatalogViewModel
{
    protected ManDeviceCatalogViewModel(IRuntimeUiService runtime) : base(runtime) { }

    public override bool ShowDriver => true;
    public override string IdLabel => "Name (Id)";
    public override string NameLabel => "Desc (描述)";

    protected abstract List<MdkSetting.DeviceConfig> Source { get; }
    protected abstract string DefaultType { get; }
    protected abstract string IdPrefix { get; }

    protected virtual bool Include(MdkSetting.DeviceConfig d) => true;

    protected override IEnumerable<ManItemRow> Enumerate() =>
        Source.Where(Include).Select(d => new ManItemRow
        {
            Id = d.Id,
            Name = d.Name,
            Type = d.Type,
            Desc = d.DriverId,
            Enabled = d.Enabled,
            Extra = d.Enabled ? "启用" : "禁用",
        });

    protected override void LoadItem(string key)
    {
        var d = Find(key);
        if (d is null)
        {
            return;
        }

        EditId = d.Id;
        EditName = d.Name;
        EditType = d.Type;
        EditDriverId = d.DriverId;
        EditEnabled = d.Enabled;
        LoadParams(d.Parameters);
    }

    protected override void ApplyItem(string originalKey)
    {
        var d = Find(originalKey);
        if (d is null)
        {
            return;
        }

        d.Id = EditId.Trim();
        d.Name = EditName.Trim();
        d.Type = EditType.Trim();
        d.DriverId = EditDriverId.Trim();
        d.Enabled = EditEnabled;
        d.Parameters = ReadParams();
    }

    protected override void CreateItem()
    {
        var id = UniqueId(Source.Select(d => d.Id), IdPrefix);
        Source.Add(new MdkSetting.DeviceConfig { Id = id, Name = id, Type = DefaultType, Enabled = true });
    }

    protected override void RemoveItem(string key) =>
        Source.RemoveAll(d => string.Equals(d.Id, key, StringComparison.OrdinalIgnoreCase));

    protected override void DuplicateItem(string key)
    {
        var d = Find(key);
        if (d is null)
        {
            return;
        }

        var id = UniqueId(Source.Select(x => x.Id), d.Id);
        Source.Add(new MdkSetting.DeviceConfig
        {
            Id = id,
            Name = d.Name,
            Type = d.Type,
            DriverId = d.DriverId,
            Enabled = d.Enabled,
            Parameters = new Dictionary<string, string>(d.Parameters, StringComparer.OrdinalIgnoreCase),
        });
    }

    private MdkSetting.DeviceConfig? Find(string id) =>
        Source.FirstOrDefault(d => string.Equals(d.Id, id, StringComparison.OrdinalIgnoreCase));
}

public sealed class ManTaskViewModel : ManCatalogViewModel
{
    public ManTaskViewModel(IRuntimeUiService runtime) : base(runtime) { }

    public override string Title => "任务配置";
    public override string Note => "任务增删与周期。保存后需重启运行时。";
    public override bool ShowId => false;
    public override bool ShowDriver => true;
    public override bool ShowEnabled => false;
    public override bool ShowInterval => true;
    public override string NameLabel => "Name";

    protected override IEnumerable<ManItemRow> Enumerate() =>
        Setting.Tasks.Select(t => new ManItemRow
        {
            Id = t.Name,
            Name = t.Name,
            Type = t.Type,
            Desc = t.DriverId,
            Extra = $"{t.IntervalMs} ms",
        });

    protected override void LoadItem(string key)
    {
        var t = Find(key);
        if (t is null)
        {
            return;
        }

        EditName = t.Name;
        EditType = t.Type;
        EditDriverId = t.DriverId;
        EditInterval = t.IntervalMs;
        LoadParams(t.Parameters);
    }

    protected override void ApplyItem(string originalKey)
    {
        var t = Find(originalKey);
        if (t is null)
        {
            return;
        }

        t.Name = EditName.Trim();
        t.Type = EditType.Trim();
        t.DriverId = EditDriverId.Trim();
        t.IntervalMs = Math.Max(1, EditInterval);
        t.Parameters = ReadParams();
    }

    protected override void CreateItem()
    {
        var name = UniqueId(Setting.Tasks.Select(t => t.Name), "task");
        Setting.Tasks.Add(new MdkSetting.TaskConfig { Name = name, Type = "pollDriver", IntervalMs = 100 });
    }

    protected override void RemoveItem(string key) =>
        Setting.Tasks.RemoveAll(t => string.Equals(t.Name, key, StringComparison.OrdinalIgnoreCase));

    protected override void DuplicateItem(string key)
    {
        var t = Find(key);
        if (t is null)
        {
            return;
        }

        var name = UniqueId(Setting.Tasks.Select(x => x.Name), t.Name);
        Setting.Tasks.Add(new MdkSetting.TaskConfig
        {
            Name = name,
            Type = t.Type,
            DriverId = t.DriverId,
            IntervalMs = t.IntervalMs,
            Parameters = new Dictionary<string, string>(t.Parameters, StringComparer.OrdinalIgnoreCase),
        });
    }

    private MdkSetting.TaskConfig? Find(string name) =>
        Setting.Tasks.FirstOrDefault(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase));
}

public sealed class ManVarsViewModel : ManCatalogViewModel
{
    public ManVarsViewModel(IRuntimeUiService runtime) : base(runtime) { }

    public override string Title => "变量配置";
    public override string Note => "种子变量 Key / Value。保存后下次启动加载。";
    public override bool ShowName => false;
    public override bool ShowType => false;
    public override bool ShowEnabled => false;
    public override bool ShowParams => false;
    public override bool ShowValue => true;
    public override string IdLabel => "Key";

    protected override IEnumerable<ManItemRow> Enumerate() =>
        Setting.Vars.OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase).Select(kv => new ManItemRow
        {
            Id = kv.Key,
            Name = kv.Key,
            Extra = kv.Value?.ToString() ?? "",
        });

    protected override void LoadItem(string key)
    {
        EditId = key;
        EditValue = Setting.Vars.TryGetValue(key, out var v) ? v?.ToString() ?? "" : "";
    }

    protected override void ApplyItem(string originalKey)
    {
        if (Setting.Vars.ContainsKey(originalKey) && !string.Equals(originalKey, EditId, StringComparison.OrdinalIgnoreCase))
        {
            Setting.Vars.Remove(originalKey);
        }

        Setting.Vars[EditId.Trim()] = EditValue;
    }

    protected override void CreateItem()
    {
        var key = UniqueId(Setting.Vars.Keys, "var");
        Setting.Vars[key] = "";
    }

    protected override void RemoveItem(string key) => Setting.Vars.Remove(key);

    protected override void DuplicateItem(string key)
    {
        var next = UniqueId(Setting.Vars.Keys, key);
        Setting.Vars[next] = Setting.Vars.TryGetValue(key, out var v) ? v : "";
    }
}

public sealed class ManRecipeViewModel : ManCatalogViewModel
{
    public ManRecipeViewModel(IRuntimeUiService runtime) : base(runtime) { }

    public override string Title => "配方配置";
    public override string Note => "排单增删与 Vars。应用属性写入配方库；完整编辑仍以 Config.Wpf 为主。";
    public override bool ShowType => false;
    public override bool ShowEnabled => false;
    public override bool ShowDesc => true;
    public override string IdLabel => "Name (Id)";
    public override string NameLabel => "名称";
    public override string ParamTitle => "Vars (Key / Value)";

    protected override IEnumerable<ManItemRow> Enumerate() =>
        Setting.Recipes.Select(r => new ManItemRow
        {
            Id = r.Id,
            Name = r.Name,
            Desc = r.Description ?? "",
            Extra = $"{r.Vars.Count} vars",
        });

    protected override void LoadItem(string key)
    {
        var r = Find(key);
        if (r is null)
        {
            return;
        }

        EditId = r.Id;
        EditName = r.Name;
        EditDesc = r.Description ?? "";
        LoadParams(r.Vars);
    }

    protected override void ApplyItem(string originalKey)
    {
        var r = Find(originalKey);
        if (r is null)
        {
            return;
        }

        r.Id = EditId.Trim();
        r.Name = EditName.Trim();
        r.Description = string.IsNullOrWhiteSpace(EditDesc) ? null : EditDesc.Trim();
        r.Vars = ReadObjectParams();
    }

    protected override void CreateItem()
    {
        var id = UniqueId(Setting.Recipes.Select(r => r.Id), "recipe");
        Setting.Recipes.Add(new MdkSetting.RecipeConfig { Id = id, Name = id });
    }

    protected override void RemoveItem(string key) =>
        Setting.Recipes.RemoveAll(r => string.Equals(r.Id, key, StringComparison.OrdinalIgnoreCase));

    protected override void DuplicateItem(string key)
    {
        var r = Find(key);
        if (r is null)
        {
            return;
        }

        var id = UniqueId(Setting.Recipes.Select(x => x.Id), r.Id);
        Setting.Recipes.Add(new MdkSetting.RecipeConfig
        {
            Id = id,
            Name = r.Name,
            Description = r.Description,
            Vars = new Dictionary<string, object?>(r.Vars, StringComparer.OrdinalIgnoreCase),
        });
    }

    private MdkSetting.RecipeConfig? Find(string id) =>
        Setting.Recipes.FirstOrDefault(r => string.Equals(r.Id, id, StringComparison.OrdinalIgnoreCase));
}

public sealed class ManVisionViewModel : ManCatalogViewModel
{
    public ManVisionViewModel(IRuntimeUiService runtime) : base(runtime) { }

    public override string Title => "视觉配置";
    public override string Note => "名称与默认相机。管线节点请用 Config.Wpf 视觉编辑器。";
    public override bool ShowType => false;
    public override bool ShowEnabled => false;
    public override bool ShowParams => false;
    public override bool ShowDesc => true;
    public override bool ShowCamera => true;
    public override string IdLabel => "Name (Id)";

    protected override IEnumerable<ManItemRow> Enumerate() =>
        Setting.Visions.Select(v => new ManItemRow
        {
            Id = v.Id,
            Name = v.Name,
            Desc = v.Description ?? "",
            Extra = v.CameraDeviceId,
        });

    protected override void LoadItem(string key)
    {
        var v = Find(key);
        if (v is null)
        {
            return;
        }

        EditId = v.Id;
        EditName = v.Name;
        EditDesc = v.Description ?? "";
        EditCamera = v.CameraDeviceId;
    }

    protected override void ApplyItem(string originalKey)
    {
        var v = Find(originalKey);
        if (v is null)
        {
            return;
        }

        v.Id = EditId.Trim();
        v.Name = EditName.Trim();
        v.Description = string.IsNullOrWhiteSpace(EditDesc) ? null : EditDesc.Trim();
        v.CameraDeviceId = EditCamera.Trim();
    }

    protected override void CreateItem()
    {
        var id = UniqueId(Setting.Visions.Select(v => v.Id), "vis");
        Setting.Visions.Add(new MdkSetting.VisionConfig { Id = id, Name = id });
    }

    protected override void RemoveItem(string key) =>
        Setting.Visions.RemoveAll(v => string.Equals(v.Id, key, StringComparison.OrdinalIgnoreCase));

    protected override void DuplicateItem(string key)
    {
        var v = Find(key);
        if (v is null)
        {
            return;
        }

        var id = UniqueId(Setting.Visions.Select(x => x.Id), v.Id);
        Setting.Visions.Add(new MdkSetting.VisionConfig
        {
            Id = id,
            Name = v.Name,
            Description = v.Description,
            CameraDeviceId = v.CameraDeviceId,
        });
    }

    private MdkSetting.VisionConfig? Find(string id) =>
        Setting.Visions.FirstOrDefault(v => string.Equals(v.Id, id, StringComparison.OrdinalIgnoreCase));
}

public sealed class ManAlarmViewModel : ManCatalogViewModel
{
    public ManAlarmViewModel(IRuntimeUiService runtime) : base(runtime) { }

    public override string Title => "报警定义";
    public override string Note => "报警目录增删与条件。保存后运行时按变量条件评估。";
    public override bool ShowType => false;
    public override bool ShowParams => false;
    public override bool ShowDesc => true;
    public override bool ShowCode => true;
    public override bool ShowLevel => true;
    public override bool ShowOp => true;
    public override bool ShowVarKey => true;
    public override bool ShowValue => true;
    public override bool ShowLatch => true;
    public override string IdLabel => "Key";
    public override string NameLabel => "名称";

    protected override IEnumerable<ManItemRow> Enumerate() =>
        Setting.Alarms.Select(a => new ManItemRow
        {
            Id = a.EffectiveId,
            Name = a.Name,
            Type = a.Code,
            Desc = $"{a.VarKey} {a.Op} {a.Value}",
            Enabled = a.Enabled,
            Extra = a.Enabled ? a.Level : "禁用",
        });

    protected override void LoadItem(string key)
    {
        var a = Find(key);
        if (a is null)
        {
            return;
        }

        EditId = a.EffectiveId;
        EditName = a.Name;
        EditCode = a.Code;
        EditLevel = string.IsNullOrWhiteSpace(a.Level) ? "error" : a.Level;
        EditOp = string.IsNullOrWhiteSpace(a.Op) ? "eq" : a.Op;
        EditVarKey = a.VarKey;
        EditValue = a.Value;
        EditDesc = a.EffectiveMessage;
        EditEnabled = a.Enabled;
        EditLatch = a.Latch;
    }

    protected override void ApplyItem(string originalKey)
    {
        var a = Find(originalKey);
        if (a is null)
        {
            return;
        }

        a.Id = EditId.Trim();
        a.Key = EditId.Trim();
        a.Name = EditName.Trim();
        a.Code = EditCode.Trim();
        a.Level = EditLevel.Trim();
        a.Op = EditOp.Trim();
        a.VarKey = EditVarKey.Trim();
        a.Value = EditValue;
        a.Message = EditDesc;
        a.Msg = EditDesc;
        a.Enabled = EditEnabled;
        a.Latch = EditLatch;
    }

    protected override void CreateItem()
    {
        var id = UniqueId(Setting.Alarms.Select(a => a.EffectiveId), "alm");
        Setting.Alarms.Add(new MdkSetting.AlarmConfig { Id = id, Key = id, Name = id, Level = "error", Enabled = true });
    }

    protected override void RemoveItem(string key) =>
        Setting.Alarms.RemoveAll(a => string.Equals(a.EffectiveId, key, StringComparison.OrdinalIgnoreCase));

    protected override void DuplicateItem(string key)
    {
        var a = Find(key);
        if (a is null)
        {
            return;
        }

        var id = UniqueId(Setting.Alarms.Select(x => x.EffectiveId), a.EffectiveId);
        Setting.Alarms.Add(new MdkSetting.AlarmConfig
        {
            Id = id,
            Key = id,
            Name = a.Name,
            Code = a.Code,
            Level = a.Level,
            Op = a.Op,
            VarKey = a.VarKey,
            Value = a.Value,
            Message = a.EffectiveMessage,
            Msg = a.EffectiveMessage,
            Enabled = a.Enabled,
            Latch = a.Latch,
        });
    }

    private MdkSetting.AlarmConfig? Find(string id) =>
        Setting.Alarms.FirstOrDefault(a => string.Equals(a.EffectiveId, id, StringComparison.OrdinalIgnoreCase));
}
