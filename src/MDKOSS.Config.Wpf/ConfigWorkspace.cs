using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Windows.Data;
using MDKOSS.Core;
using MDKOSS.Core.Data;

namespace MDKOSS.Config.Wpf;

public enum ConfigDocumentKind
{
    None,
    Json,
    Database,
}

public enum ConfigModule
{
    Machine,
    Drivers,
    Devices,
    Axis,
    Platform,
    Gpios,
    Vios,
    Tasks,
    Vars,
    Recipes,
    Visions,
    SysConfig,
    Database,
}

/// <summary>One list/tree component row bound to underlying setting data.</summary>
public sealed class ComponentItem : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    public ConfigModule Module { get; init; }
    public object Source { get; init; } = null!;
    public string Key { get; set; } = string.Empty;

    private string _title = string.Empty;
    public string Title
    {
        get => _title;
        set { _title = value; OnPropertyChanged(); }
    }

    private string _subtitle = string.Empty;
    public string Subtitle
    {
        get => _subtitle;
        set { _subtitle = value; OnPropertyChanged(); }
    }

    private string _col1 = string.Empty;
    public string Col1
    {
        get => _col1;
        set { _col1 = value; OnPropertyChanged(); }
    }

    private string _col2 = string.Empty;
    public string Col2
    {
        get => _col2;
        set { _col2 = value; OnPropertyChanged(); }
    }

    private string _col3 = string.Empty;
    public string Col3
    {
        get => _col3;
        set { _col3 = value; OnPropertyChanged(); }
    }

    private string _col4 = string.Empty;
    public string Col4
    {
        get => _col4;
        set { _col4 = value; OnPropertyChanged(); }
    }

    private string _col5 = string.Empty;
    public string Col5
    {
        get => _col5;
        set { _col5 = value; OnPropertyChanged(); }
    }

    private string _col6 = string.Empty;
    public string Col6
    {
        get => _col6;
        set { _col6 = value; OnPropertyChanged(); }
    }

    private string _col7 = string.Empty;
    public string Col7
    {
        get => _col7;
        set { _col7 = value; OnPropertyChanged(); }
    }

    private bool _enabled = true;
    public bool Enabled
    {
        get => _enabled;
        set { _enabled = value; OnPropertyChanged(); }
    }

    public bool HasEnabled { get; init; }

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>Editable fields shown in the right property panel.</summary>
public sealed class PropertyDraft : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    private string _fieldId = string.Empty;
    private string _fieldName = string.Empty;
    private string _fieldType = string.Empty;
    private string _fieldDriverId = string.Empty;
    private string _fieldInterval = "100";
    private string _fieldDescription = string.Empty;
    private string _fieldValue = string.Empty;
    private string _fieldParameters = "{}";
    private bool _fieldEnabled = true;
    private bool _showId = true;
    private bool _showName;
    private bool _showType = true;
    private bool _showDriverId;
    private bool _showInterval;
    private bool _showDescription;
    private bool _showValue;
    private bool _showEnabled = true;
    private bool _showParameters = true;
    private bool _isReadOnly;
    private bool _parametersAsObject;
    private bool _isDirty;
    private bool _suppressDirty;
    private bool _showQuickAddTypes;
    private bool _showComposeAxes;
    private bool _showPickRecipeVars;
    private bool _showEditVisionPipeline;
    private string _headline = "未选择组件";
    private string _labelId = "Name (Id)";
    private string _labelName = "Desc (描述)";
    private string _labelType = "Type";
    private string _labelDriverId = "DriverId";
    private string _labelInterval = "IntervalMs";
    private string _labelDescription = "Description / Label";
    private string _labelValue = "Port / Value";

    public PropertyDraft()
    {
        ParameterRows.CollectionChanged += OnParameterRowsChanged;
    }

    public ObservableCollection<KvPairRow> ParameterRows { get; } = [];
    public ObservableCollection<string> TypeOptions { get; } = [];
    public ObservableCollection<string> DriverOptions { get; } = [];
    /// <summary>Suggested parameter keys for the current Type (editable ComboBox source).</summary>
    public ObservableCollection<string> ParamKeySuggestions { get; } = [];
    /// <summary>Suggested parameter values (drivers / axis ids / kind tokens).</summary>
    public ObservableCollection<string> ParamValueSuggestions { get; } = [];
    /// <summary>Module-level quick-add type chips (shown when no component is selected).</summary>
    public ObservableCollection<string> QuickAddTypes { get; } = [];

    public string Headline { get => _headline; set { _headline = value; OnPropertyChanged(); } }
    public string LabelId { get => _labelId; set { if (_labelId == value) return; _labelId = value; OnPropertyChanged(); } }
    public string LabelName { get => _labelName; set { if (_labelName == value) return; _labelName = value; OnPropertyChanged(); } }
    public string LabelType { get => _labelType; set { if (_labelType == value) return; _labelType = value; OnPropertyChanged(); } }
    public string LabelDriverId { get => _labelDriverId; set { if (_labelDriverId == value) return; _labelDriverId = value; OnPropertyChanged(); } }
    public string LabelInterval { get => _labelInterval; set { if (_labelInterval == value) return; _labelInterval = value; OnPropertyChanged(); } }
    public string LabelDescription { get => _labelDescription; set { if (_labelDescription == value) return; _labelDescription = value; OnPropertyChanged(); } }
    public string LabelValue { get => _labelValue; set { if (_labelValue == value) return; _labelValue = value; OnPropertyChanged(); } }
    public string FieldId
    {
        get => _fieldId;
        set { if (_fieldId == value) return; _fieldId = value; OnPropertyChanged(); MarkDirty(); }
    }
    public string FieldName
    {
        get => _fieldName;
        set { if (_fieldName == value) return; _fieldName = value; OnPropertyChanged(); MarkDirty(); }
    }
    public string FieldType
    {
        get => _fieldType;
        set { if (_fieldType == value) return; _fieldType = value; OnPropertyChanged(); MarkDirty(); }
    }
    public string FieldDriverId
    {
        get => _fieldDriverId;
        set { if (_fieldDriverId == value) return; _fieldDriverId = value; OnPropertyChanged(); MarkDirty(); }
    }
    public string FieldInterval
    {
        get => _fieldInterval;
        set { if (_fieldInterval == value) return; _fieldInterval = value; OnPropertyChanged(); MarkDirty(); }
    }
    public string FieldDescription
    {
        get => _fieldDescription;
        set { if (_fieldDescription == value) return; _fieldDescription = value; OnPropertyChanged(); MarkDirty(); }
    }
    public string FieldValue
    {
        get => _fieldValue;
        set { if (_fieldValue == value) return; _fieldValue = value; OnPropertyChanged(); MarkDirty(); }
    }
    public string FieldParameters
    {
        get => _fieldParameters;
        set { if (_fieldParameters == value) return; _fieldParameters = value; OnPropertyChanged(); MarkDirty(); }
    }
    public bool FieldEnabled
    {
        get => _fieldEnabled;
        set { if (_fieldEnabled == value) return; _fieldEnabled = value; OnPropertyChanged(); MarkDirty(); }
    }

    public bool ShowId { get => _showId; set { _showId = value; OnPropertyChanged(); } }
    public bool ShowName { get => _showName; set { _showName = value; OnPropertyChanged(); } }
    public bool ShowType { get => _showType; set { _showType = value; OnPropertyChanged(); } }
    public bool ShowDriverId { get => _showDriverId; set { _showDriverId = value; OnPropertyChanged(); } }
    public bool ShowInterval { get => _showInterval; set { _showInterval = value; OnPropertyChanged(); } }
    public bool ShowDescription { get => _showDescription; set { _showDescription = value; OnPropertyChanged(); } }
    public bool ShowValue { get => _showValue; set { _showValue = value; OnPropertyChanged(); } }
    public bool ShowEnabled { get => _showEnabled; set { _showEnabled = value; OnPropertyChanged(); } }
    public bool ShowParameters { get => _showParameters; set { _showParameters = value; OnPropertyChanged(); } }
    public bool ShowQuickAddTypes
    {
        get => _showQuickAddTypes;
        set { if (_showQuickAddTypes == value) return; _showQuickAddTypes = value; OnPropertyChanged(); }
    }
    /// <summary>Show「组合轴」button when editing a Platform component.</summary>
    public bool ShowComposeAxes
    {
        get => _showComposeAxes;
        set { if (_showComposeAxes == value) return; _showComposeAxes = value; OnPropertyChanged(); }
    }
    /// <summary>Show「从 Vars…」button when editing a Recipe component.</summary>
    public bool ShowPickRecipeVars
    {
        get => _showPickRecipeVars;
        set { if (_showPickRecipeVars == value) return; _showPickRecipeVars = value; OnPropertyChanged(); }
    }
    /// <summary>Show「编辑视觉流程…」button when editing a Vision component.</summary>
    public bool ShowEditVisionPipeline
    {
        get => _showEditVisionPipeline;
        set { if (_showEditVisionPipeline == value) return; _showEditVisionPipeline = value; OnPropertyChanged(); }
    }
    public bool IsReadOnly { get => _isReadOnly; set { _isReadOnly = value; OnPropertyChanged(); } }
    public bool ParametersAsObject { get => _parametersAsObject; set { _parametersAsObject = value; OnPropertyChanged(); } }
    public bool IsDirty
    {
        get => _isDirty;
        private set
        {
            if (_isDirty == value) return;
            _isDirty = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(DirtyBadge));
        }
    }

    public string DirtyBadge => IsDirty && !IsReadOnly ? "已修改（未应用）" : string.Empty;

    private string _parameterPreview = "";
    public string ParameterPreview
    {
        get => _parameterPreview;
        private set { if (_parameterPreview == value) return; _parameterPreview = value; OnPropertyChanged(); }
    }

    public bool ShowParameterPreview => ShowParameters && !string.IsNullOrWhiteSpace(ParameterPreview);

    public IDisposable SuppressDirtyScope() => new DirtySuppressor(this);

    public void ClearDirty() => IsDirty = false;

    public void MarkDirty()
    {
        if (_suppressDirty || IsReadOnly)
        {
            return;
        }

        IsDirty = true;
    }

    public void Clear(string message = "未选择组件")
    {
        using (SuppressDirtyScope())
        {
            Headline = message;
            IsReadOnly = true;
            ShowId = ShowName = ShowType = ShowDriverId = ShowInterval = ShowDescription = ShowValue = ShowEnabled = ShowParameters = false;
            ShowQuickAddTypes = false;
            ShowComposeAxes = false;
            ShowPickRecipeVars = false;
            ShowEditVisionPipeline = false;
            ResetFieldLabels();
            FieldId = FieldName = FieldType = FieldDriverId = FieldDescription = FieldValue = string.Empty;
            FieldParameters = "{}";
            FieldInterval = "100";
            FieldEnabled = true;
            ParametersAsObject = false;
            DetachAllParamRows();
            ParameterRows.Clear();
            TypeOptions.Clear();
            DriverOptions.Clear();
            ParamKeySuggestions.Clear();
            ParamValueSuggestions.Clear();
            QuickAddTypes.Clear();
            ParameterPreview = "";
        }

        ClearDirty();
    }

    public void ResetFieldLabels()
    {
        LabelId = "Name (Id)";
        LabelName = "Desc (描述)";
        LabelType = "Type";
        LabelDriverId = "驱动";
        LabelInterval = "IntervalMs";
        LabelDescription = "Description / Label";
        LabelValue = "Port / Value";
    }

    /// <summary>Field captions for Gpios point editing (Name/Desc/Type/DriverId/Port).</summary>
    public void ApplyGpioFieldLabels()
    {
        LabelId = "Name (Alias)";
        LabelName = "Desc (描述)";
        LabelType = "Type / Direction";
        LabelDriverId = "驱动";
        LabelValue = "Port";
    }

    /// <summary>Field captions for Vios point editing.</summary>
    public void ApplyVioFieldLabels()
    {
        LabelId = "Name (Alias)";
        LabelName = "Desc (描述)";
        LabelType = "Type";
        LabelDriverId = "驱动";
        LabelValue = "DeviceId";
    }

    public void SetQuickAddTypes(IEnumerable<string> types)
    {
        QuickAddTypes.Clear();
        foreach (var t in types)
        {
            if (!string.IsNullOrWhiteSpace(t))
            {
                QuickAddTypes.Add(t);
            }
        }

        ShowQuickAddTypes = QuickAddTypes.Count > 0;
    }

    public void SetTypeOptions(IEnumerable<string> options)
    {
        TypeOptions.Clear();
        foreach (var o in options)
        {
            TypeOptions.Add(o);
        }
    }

    public void SetDriverOptions(IEnumerable<string> options)
    {
        DriverOptions.Clear();
        foreach (var o in options)
        {
            DriverOptions.Add(o);
        }
    }

    public void SetParamKeySuggestions(IEnumerable<string> keys)
    {
        ParamKeySuggestions.Clear();
        foreach (var key in keys.Where(k => !string.IsNullOrWhiteSpace(k))
                     .Distinct(StringComparer.OrdinalIgnoreCase)
                     .OrderBy(k => k, StringComparer.OrdinalIgnoreCase))
        {
            ParamKeySuggestions.Add(key);
        }
    }

    public void SetParamValueSuggestions(IEnumerable<string> values)
    {
        ParamValueSuggestions.Clear();
        foreach (var v in values.Where(x => !string.IsNullOrWhiteSpace(x))
                     .Distinct(StringComparer.OrdinalIgnoreCase)
                     .OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
        {
            ParamValueSuggestions.Add(v);
        }
    }

    public void LoadStringParameters(IReadOnlyDictionary<string, string> dict)
    {
        ParametersAsObject = false;
        KvTableHelper.LoadStringDict(ParameterRows, dict);
        SyncJsonFromRows();
    }

    /// <summary>Load parameter rows in caller order (no alphabetical sort).</summary>
    public void LoadOrderedStringParameters(IEnumerable<(string Key, string Value)> pairs)
    {
        ParametersAsObject = false;
        DetachAllParamRows();
        ParameterRows.Clear();
        foreach (var (key, value) in pairs)
        {
            ParameterRows.Add(new KvPairRow { Key = key, Value = value ?? "" });
        }

        SyncJsonFromRows();
    }

    public void LoadObjectParameters(IReadOnlyDictionary<string, object?> dict)
    {
        ParametersAsObject = true;
        KvTableHelper.LoadObjectDict(ParameterRows, dict);
        SyncJsonFromRows();
    }

    public void SyncJsonFromRows()
    {
        FieldParameters = KvTableHelper.ToJson(ParameterRows, ParametersAsObject);
        RefreshParameterPreview();
    }

    public void SyncRowsFromJson()
    {
        KvTableHelper.LoadFromJsonObject(ParameterRows, FieldParameters);
        RefreshParameterPreview();
    }

    public void RefreshParameterPreview()
    {
        var parts = new List<string>();
        foreach (var row in ParameterRows)
        {
            if (string.IsNullOrWhiteSpace(row.Key))
            {
                continue;
            }

            var v = row.Value ?? "";
            if (v.Length > 28)
            {
                v = v[..25] + "…";
            }

            parts.Add($"{row.Key}={v}");
            if (parts.Count >= 8)
            {
                break;
            }
        }

        var extra = ParameterRows.Count(r => !string.IsNullOrWhiteSpace(r.Key)) - parts.Count;
        ParameterPreview = parts.Count == 0
            ? "(无参数)"
            : string.Join(" · ", parts) + (extra > 0 ? $" · +{extra} 项" : "");
        OnPropertyChanged(nameof(ShowParameterPreview));
    }

    public Dictionary<string, string> CollectStringParameters() =>
        KvTableHelper.ToStringDict(ParameterRows);

    public Dictionary<string, object?> CollectObjectParameters() =>
        KvTableHelper.ToObjectDict(ParameterRows);

    private void OnParameterRowsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
        {
            foreach (KvPairRow row in e.OldItems)
            {
                row.PropertyChanged -= OnParamRowPropertyChanged;
            }
        }

        if (e.NewItems is not null)
        {
            foreach (KvPairRow row in e.NewItems)
            {
                row.PropertyChanged += OnParamRowPropertyChanged;
            }
        }

        if (e.Action == NotifyCollectionChangedAction.Reset)
        {
            foreach (var row in ParameterRows)
            {
                row.PropertyChanged -= OnParamRowPropertyChanged;
                row.PropertyChanged += OnParamRowPropertyChanged;
            }
        }

        MarkDirty();
        RefreshParameterPreview();
    }

    private void OnParamRowPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        MarkDirty();
        RefreshParameterPreview();
    }

    private void DetachAllParamRows()
    {
        foreach (var row in ParameterRows)
        {
            row.PropertyChanged -= OnParamRowPropertyChanged;
        }
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    private sealed class DirtySuppressor : IDisposable
    {
        private readonly PropertyDraft _draft;
        private readonly bool _previous;

        public DirtySuppressor(PropertyDraft draft)
        {
            _draft = draft;
            _previous = draft._suppressDirty;
            draft._suppressDirty = true;
        }

        public void Dispose() => _draft._suppressDirty = _previous;
    }
}

public sealed class ConfigWorkspace : INotifyPropertyChanged
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private MdkSetting _setting = new();
    private string _jsonPath = string.Empty;
    private string _dbPath = string.Empty;
    private ConfigDocumentKind _documentKind = ConfigDocumentKind.None;
    private ConfigModule _module = ConfigModule.Machine;
    private ComponentItem? _selected;
    private string _statusLine = "未打开配置";
    private string _moduleTitle = "Drivers";
    private string _colHeader1 = "Id";
    private string _colHeader2 = "Type";
    private string _colHeader3 = "Enabled";
    private string _colHeader4 = "";
    private string _colHeader5 = "";
    private string _colHeader6 = "";
    private string _colHeader7 = "";
    private ConfigTableCounts? _dbCounts;
    private List<ConfigLogRecord> _logs = [];
    private string? _selectedDbTable;
    private bool _isBrowsingDbTable;
    private string? _dbPrimaryKey;
    private DbRowItem? _selectedDbRow;
    private string _listFilter = "";
    private ICollectionView? _itemsView;

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<ComponentItem> Items { get; } = [];
    /// <summary>Filtered view of <see cref="Items"/> (center grid binds here).</summary>
    public ICollectionView ItemsView
    {
        get
        {
            if (_itemsView is null)
            {
                _itemsView = CollectionViewSource.GetDefaultView(Items);
                _itemsView.Filter = MatchesListFilter;
            }

            return _itemsView;
        }
    }

    public ObservableCollection<DbRowItem> DbRows { get; } = [];
    /// <summary>Column names of the currently browsed SQLite table (middle pane headers).</summary>
    public ObservableCollection<string> DbColumns { get; } = [];
    public PropertyDraft Draft { get; } = new();

    /// <summary>Center-list text filter (Name / Desc / Driver / Port / …). Empty = show all.</summary>
    public string ListFilter
    {
        get => _listFilter;
        set
        {
            var next = value ?? "";
            if (_listFilter == next)
            {
                return;
            }

            _listFilter = next;
            OnPropertyChanged();
            RefreshListFilter();
        }
    }

    /// <summary>Hint under the filter box, e.g. 「筛选 · 显示 3/42」.</summary>
    public string ListFilterHint
    {
        get
        {
            if (string.IsNullOrWhiteSpace(_listFilter))
            {
                return Items.Count == 0 ? "" : $"共 {Items.Count} 项";
            }

            var visible = 0;
            foreach (var _ in ItemsView)
            {
                visible++;
            }

            return $"筛选 «{_listFilter.Trim()}» · 显示 {visible}/{Items.Count}";
        }
    }

    public string ProjectName => _setting.ProjectName;
    public MdkSetting Setting => _setting;
    public ConfigModule CurrentModule => _module;
    public ComponentItem? SelectedItem => _selected;
    public ConfigDocumentKind DocumentKind => _documentKind;
    public string JsonPath => _jsonPath;
    public string DatabasePath => _dbPath;

    public bool IsBrowsingDbTable
    {
        get => _isBrowsingDbTable;
        private set { _isBrowsingDbTable = value; OnPropertyChanged(); }
    }

    public string? SelectedDbTable
    {
        get => _selectedDbTable;
        private set { _selectedDbTable = value; OnPropertyChanged(); }
    }

    public DbRowItem? SelectedDbRow
    {
        get => _selectedDbRow;
        private set { _selectedDbRow = value; OnPropertyChanged(); }
    }

    public string? DbPrimaryKey => _dbPrimaryKey;

    /// <summary>Primary document path (JSON or DB depending on <see cref="DocumentKind"/>).</summary>
    public string DocumentPath => _documentKind switch
    {
        ConfigDocumentKind.Json => _jsonPath,
        ConfigDocumentKind.Database => _dbPath,
        _ => string.Empty,
    };

    public string DocumentKindLabel => _documentKind switch
    {
        ConfigDocumentKind.Json => "JSON",
        ConfigDocumentKind.Database => "DB",
        _ => "未打开",
    };

    public string StatusLine
    {
        get => _statusLine;
        private set { _statusLine = value; OnPropertyChanged(); }
    }

    public string ModuleTitle
    {
        get => _moduleTitle;
        private set { _moduleTitle = value; OnPropertyChanged(); }
    }

    public string ColHeader1 { get => _colHeader1; private set { _colHeader1 = value; OnPropertyChanged(); } }
    public string ColHeader2 { get => _colHeader2; private set { _colHeader2 = value; OnPropertyChanged(); } }
    public string ColHeader3 { get => _colHeader3; private set { _colHeader3 = value; OnPropertyChanged(); } }
    public string ColHeader4 { get => _colHeader4; private set { _colHeader4 = value; OnPropertyChanged(); } }
    public string ColHeader5 { get => _colHeader5; private set { _colHeader5 = value; OnPropertyChanged(); } }
    public string ColHeader6 { get => _colHeader6; private set { _colHeader6 = value; OnPropertyChanged(); } }
    public string ColHeader7 { get => _colHeader7; private set { _colHeader7 = value; OnPropertyChanged(); } }

    public bool CanEditList => _module is not (ConfigModule.Machine or ConfigModule.Database or ConfigModule.Gpios or ConfigModule.Vios or ConfigModule.SysConfig);

    public static ConfigDocumentKind DetectKind(string path)
    {
        var ext = System.IO.Path.GetExtension(path);
        if (ext.Equals(".db", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".sqlite", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".sqlite3", StringComparison.OrdinalIgnoreCase))
        {
            return ConfigDocumentKind.Database;
        }

        if (ext.Equals(".json", StringComparison.OrdinalIgnoreCase))
        {
            return ConfigDocumentKind.Json;
        }

        // Fallback: try SQLite header / JSON text
        try
        {
            using var fs = System.IO.File.OpenRead(path);
            Span<byte> header = stackalloc byte[16];
            var n = fs.Read(header);
            if (n >= 16)
            {
                var sig = System.Text.Encoding.ASCII.GetString(header);
                if (sig.StartsWith("SQLite format 3", StringComparison.Ordinal))
                {
                    return ConfigDocumentKind.Database;
                }
            }
        }
        catch
        {
            // ignore
        }

        return ConfigDocumentKind.Json;
    }

    /// <summary>Open a JSON setting or SQLite config database.</summary>
    public void Open(string path)
    {
        var full = System.IO.Path.GetFullPath(path);
        if (!System.IO.File.Exists(full))
        {
            throw new FileNotFoundException("文件不存在。", full);
        }

        var kind = DetectKind(full);
        if (kind == ConfigDocumentKind.Database)
        {
            OpenDatabase(full, setAsPrimary: true);
        }
        else
        {
            OpenJson(full, setAsPrimary: true);
        }
    }

    public void Reload()
    {
        if (_documentKind == ConfigDocumentKind.None || string.IsNullOrWhiteSpace(DocumentPath))
        {
            throw new InvalidOperationException("尚未打开文档。");
        }

        Open(DocumentPath);
        StatusLine = $"已重新加载 [{DocumentKindLabel}] {DocumentPath}";
    }

    /// <summary>Save back to the opened document (JSON → JSON file, DB → same DB).</summary>
    public void Save()
    {
        ValidateBeforeSave();
        switch (_documentKind)
        {
            case ConfigDocumentKind.Json:
                if (string.IsNullOrWhiteSpace(_jsonPath))
                {
                    throw new InvalidOperationException("尚未指定 JSON 路径，请「另存为 JSON」。");
                }

                _setting.Save(_jsonPath);
                StatusLine = $"已保存 JSON {_jsonPath}";
                break;
            case ConfigDocumentKind.Database:
                if (string.IsNullOrWhiteSpace(_dbPath))
                {
                    throw new InvalidOperationException("尚未指定数据库路径，请「另存为数据库」。");
                }

                WriteDatabase(_dbPath, sourceHint: _jsonPath);
                StatusLine = $"已保存到数据库 {_dbPath}";
                break;
            default:
                throw new InvalidOperationException("尚未打开文档，请先打开 JSON 或数据库。");
        }
    }

    /// <summary>Write JSON and switch primary document to that JSON.</summary>
    public void SaveAsJson(string path)
    {
        ValidateBeforeSave();
        var full = System.IO.Path.GetFullPath(path);
        _setting.Save(full);
        _jsonPath = full;
        _documentKind = ConfigDocumentKind.Json;
        NotifyDocumentChanged();
        StatusLine = $"已另存为 JSON {_jsonPath}";
    }

    /// <summary>Write SQLite and switch primary document to that DB.</summary>
    public void SaveAsDatabase(string path)
    {
        ValidateBeforeSave();
        var full = System.IO.Path.GetFullPath(path);
        WriteDatabase(full, sourceHint: _jsonPath);
        _dbPath = full;
        _documentKind = ConfigDocumentKind.Database;
        NotifyDocumentChanged();
        StatusLine = $"已另存为数据库 {_dbPath}";
    }

    /// <summary>Export a JSON copy without changing the primary document.</summary>
    public void ExportJson(string path)
    {
        ValidateBeforeSave();
        var full = System.IO.Path.GetFullPath(path);
        _setting.Save(full);
        _jsonPath = full;
        OnPropertyChanged(nameof(JsonPath));
        StatusLine = $"已导出 JSON {_jsonPath}（主文档仍为 {DocumentKindLabel}）";
    }

    /// <summary>Export a SQLite copy without changing the primary document.</summary>
    public ConfigExportResult ExportDatabase(string? path = null)
    {
        ValidateBeforeSave();
        var full = ResolveExportDbPath(path);
        var result = WriteDatabase(full, sourceHint: DocumentPath);
        if (string.IsNullOrWhiteSpace(_dbPath))
        {
            _dbPath = full;
            OnPropertyChanged(nameof(DatabasePath));
        }

        StatusLine = $"已导出数据库 {result.DatabasePath} | {result}（主文档仍为 {DocumentKindLabel}）";
        if (_module == ConfigModule.Database)
        {
            SelectModule(ConfigModule.Database, null);
        }

        return result;
    }

    public void RefreshDbPreview()
    {
        var path = !string.IsNullOrWhiteSpace(_dbPath) ? _dbPath : ResolveExportDbPath(null);
        if (!System.IO.File.Exists(path))
        {
            StatusLine = $"数据库不存在: {path}";
            return;
        }

        using var store = new MdkConfigStore(path);
        _dbCounts = store.CountTables();
        _logs = store.ListLogs(100).ToList();
        _dbPath = System.IO.Path.GetFullPath(path);
        OnPropertyChanged(nameof(DatabasePath));
        StatusLine = $"已刷新 DB 预览: {_dbPath}";
        if (_module == ConfigModule.Database)
        {
            SelectModule(ConfigModule.Database, null);
        }
    }

    // Compatibility aliases used by older call sites
    public string SettingPath => DocumentPath;
    public void OpenSetting(string path) => Open(path);
    public void ReloadSetting() => Reload();
    public void SaveSetting() => Save();
    public void SaveSettingAs(string path) => SaveAsJson(path);
    public ConfigExportResult ExportToDatabase(string? dbPath = null) => ExportDatabase(dbPath);
    public void ImportFromDatabase(string dbPath) => OpenDatabase(dbPath, setAsPrimary: true);

    private void OpenJson(string fullPath, bool setAsPrimary)
    {
        _setting = MdkSetting.Load(fullPath);
        _jsonPath = fullPath;
        if (setAsPrimary)
        {
            _documentKind = ConfigDocumentKind.Json;
        }

        NotifyDocumentChanged();
        StatusLine = $"已打开 JSON {_jsonPath}";
        SelectModule(_module, keepSelectionKey: null);
    }

    private void OpenDatabase(string fullPath, bool setAsPrimary)
    {
        using var store = new MdkConfigStore(fullPath);
        _setting = store.ImportSetting();
        _dbCounts = store.CountTables();
        _logs = store.ListLogs(100).ToList();
        _dbPath = fullPath;
        if (setAsPrimary)
        {
            _documentKind = ConfigDocumentKind.Database;
        }

        NotifyDocumentChanged();
        StatusLine = $"已打开数据库 {_dbPath}";
        SelectModule(_module, keepSelectionKey: null);
    }

    private ConfigExportResult WriteDatabase(string fullPath, string? sourceHint)
    {
        using var store = new MdkConfigStore(fullPath);
        var result = store.ExportSetting(_setting, sourceHint);
        _dbCounts = store.CountTables();
        _logs = store.ListLogs(100).ToList();
        return result;
    }

    private void NotifyDocumentChanged()
    {
        OnPropertyChanged(nameof(ProjectName));
        OnPropertyChanged(nameof(DocumentKind));
        OnPropertyChanged(nameof(DocumentKindLabel));
        OnPropertyChanged(nameof(DocumentPath));
        OnPropertyChanged(nameof(SettingPath));
        OnPropertyChanged(nameof(JsonPath));
        OnPropertyChanged(nameof(DatabasePath));
    }

    private string ResolveExportDbPath(string? overridePath)
    {
        if (!string.IsNullOrWhiteSpace(overridePath))
        {
            return System.IO.Path.GetFullPath(overridePath);
        }

        if (!string.IsNullOrWhiteSpace(_dbPath))
        {
            return _dbPath;
        }

        if (!string.IsNullOrWhiteSpace(_setting.DatabasePath))
        {
            var configured = _setting.DatabasePath!;
            return System.IO.Path.IsPathRooted(configured)
                ? configured
                : System.IO.Path.GetFullPath(System.IO.Path.Combine(AppContext.BaseDirectory, configured));
        }

        if (!string.IsNullOrWhiteSpace(_jsonPath))
        {
            var dir = System.IO.Path.GetDirectoryName(_jsonPath) ?? AppContext.BaseDirectory;
            var name = System.IO.Path.GetFileNameWithoutExtension(_jsonPath);
            if (name.EndsWith(".setting", StringComparison.OrdinalIgnoreCase))
            {
                name = name[..^".setting".Length];
            }

            return System.IO.Path.Combine(dir, string.IsNullOrWhiteSpace(name) ? "mdk.db" : $"{name}.db");
        }

        return MdkSetting.DefaultDatabasePath;
    }

    public void SelectModule(ConfigModule module, string? keepSelectionKey)
    {
        var moduleChanged = _module != module;
        _module = module;
        OnPropertyChanged(nameof(CurrentModule));
        OnPropertyChanged(nameof(CanEditList));
        OnPropertyChanged(nameof(SupportsVioDefaultLoad));
        OnPropertyChanged(nameof(SupportsExcelModuleExchange));
        SetColumnHeaders(module);
        ModuleTitle = ModuleDisplayName(module);
        if (moduleChanged && !string.IsNullOrEmpty(_listFilter))
        {
            _listFilter = "";
            OnPropertyChanged(nameof(ListFilter));
        }

        if (module == ConfigModule.Database && !string.IsNullOrWhiteSpace(keepSelectionKey)
            && !string.Equals(keepSelectionKey, "hint", StringComparison.OrdinalIgnoreCase)
            && MdkConfigStore.IsEditableTable(keepSelectionKey))
        {
            EnsureDbCounts();
            RebuildItems(); // still keep table nodes for tree
            OpenDbTable(keepSelectionKey);
            return;
        }

        ClearDbTableView();
        RebuildItems();

        ComponentItem? next = null;
        if (!string.IsNullOrEmpty(keepSelectionKey))
        {
            next = Items.FirstOrDefault(i => string.Equals(i.Key, keepSelectionKey, StringComparison.OrdinalIgnoreCase));
        }
        else if (module == ConfigModule.Machine)
        {
            next = Items.FirstOrDefault();
        }

        SelectItem(next);
        StatusLine = $"{ModuleTitle} · {VisibleItemCount()}/{Items.Count} 项 · [{DocumentKindLabel}] {DocumentPath}";
    }

    /// <summary>Clear the center-list filter box.</summary>
    public void ClearListFilter() => ListFilter = "";

    public void SelectItem(ComponentItem? item)
    {
        _selected = item;
        OnPropertyChanged(nameof(SelectedItem));

        if (_module == ConfigModule.Database && item is not null
            && !string.Equals(item.Key, "hint", StringComparison.OrdinalIgnoreCase)
            && MdkConfigStore.IsEditableTable(item.Key))
        {
            OpenDbTable(item.Key);
            return;
        }

        if (_module != ConfigModule.Database)
        {
            ClearDbTableView();
        }

        LoadDraft(item);
        if (item is not null)
        {
            StatusLine = $"{ModuleTitle} · 选中 {item.Title} · [{DocumentKindLabel}] {DocumentPath}";
        }
    }

    public void SelectDbRow(DbRowItem? row)
    {
        SelectedDbRow = row;
        if (row is null || string.IsNullOrWhiteSpace(SelectedDbTable))
        {
            Draft.Clear(SelectedDbTable is null
                ? "选择 Database 下的表以浏览行"
                : $"表 {SelectedDbTable} · 选择一行以编辑");
            return;
        }

        Draft.IsReadOnly = false;
        Draft.Headline = $"DB / {SelectedDbTable} / 行 {_dbPrimaryKey}={row.RowKey}";
        Draft.ShowId = Draft.ShowName = Draft.ShowType = Draft.ShowDriverId =
            Draft.ShowInterval = Draft.ShowDescription = Draft.ShowValue = Draft.ShowEnabled = false;
        Draft.ShowParameters = true;
        Draft.ParametersAsObject = false;
        Draft.ParameterRows.Clear();
        var columnOrder = DbColumns.Count > 0 ? DbColumns.ToList() : row.Columns.ToList();
        if (columnOrder.Count == 0)
        {
            columnOrder = row.Cells.Keys.ToList();
        }

        foreach (var col in columnOrder)
        {
            Draft.ParameterRows.Add(new KvPairRow
            {
                Key = col,
                Value = row.Cells.TryGetValue(col, out var v) ? v : "",
            });
        }

        Draft.SyncJsonFromRows();
        StatusLine = $"表 {SelectedDbTable} · 行 {row.RowKey} · {_dbPath}";
    }

    public void OpenDbTable(string tableName)
    {
        EnsureDbPathAvailable();
        using var store = new MdkConfigStore(_dbPath);
        var snap = store.QueryTable(tableName);
        _dbCounts = store.CountTables();

        SelectedDbTable = snap.TableName;
        _dbPrimaryKey = snap.PrimaryKey;
        IsBrowsingDbTable = true;
        ModuleTitle = $"Database / {snap.TableName}";
        OnPropertyChanged(nameof(DbPrimaryKey));

        DbColumns.Clear();
        foreach (var col in snap.Columns)
        {
            DbColumns.Add(col);
        }

        DbRows.Clear();
        foreach (var row in snap.Rows)
        {
            var key = snap.PrimaryKey is not null && row.TryGetValue(snap.PrimaryKey, out var pk)
                ? pk
                : Guid.NewGuid().ToString("N");
            DbRows.Add(new DbRowItem(key, row, snap.Columns));
        }

        SelectedDbRow = null;
        Draft.Clear($"表 {snap.TableName} · {DbRows.Count} 行 · 选择一行编辑（右侧 Key=列名）");
        StatusLine = $"已加载表 {snap.TableName} · {DbRows.Count} 行 · {_dbPath}";
    }

    public void RefreshDbTable()
    {
        if (string.IsNullOrWhiteSpace(SelectedDbTable))
        {
            throw new InvalidOperationException("尚未选中数据库表。");
        }

        var table = SelectedDbTable;
        var keepKey = SelectedDbRow?.RowKey;
        OpenDbTable(table);
        if (!string.IsNullOrEmpty(keepKey))
        {
            var row = DbRows.FirstOrDefault(r => string.Equals(r.RowKey, keepKey, StringComparison.OrdinalIgnoreCase));
            SelectDbRow(row);
        }
    }

    public void ApplyDbRow()
    {
        if (string.IsNullOrWhiteSpace(SelectedDbTable) || !IsBrowsingDbTable)
        {
            throw new InvalidOperationException("请先选择 Database 下的表与行。");
        }

        var values = Draft.CollectStringParameters();
        if (values.Count == 0)
        {
            throw new InvalidOperationException("没有可保存的列。");
        }

        EnsureDbPathAvailable();
        using var store = new MdkConfigStore(_dbPath);
        var pk = store.UpsertTableRow(SelectedDbTable, values);
        _dbCounts = store.CountTables();
        OpenDbTable(SelectedDbTable);
        var row = DbRows.FirstOrDefault(r => string.Equals(r.RowKey, pk, StringComparison.OrdinalIgnoreCase));
        SelectDbRow(row);
        Draft.ClearDirty();
        StatusLine = $"已保存表 {SelectedDbTable} 行 {pk}";
    }

    public void AddDbRow()
    {
        if (string.IsNullOrWhiteSpace(SelectedDbTable) || !IsBrowsingDbTable)
        {
            throw new InvalidOperationException("请先选择 Database 下的表。");
        }

        EnsureDbPathAvailable();
        using var store = new MdkConfigStore(_dbPath);
        var snap = store.QueryTable(SelectedDbTable, limit: 1);
        var blank = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var col in snap.Columns)
        {
            blank[col] = "";
        }

        if (_dbPrimaryKey is not null
            && !string.Equals(SelectedDbTable, "logs", StringComparison.OrdinalIgnoreCase))
        {
            blank[_dbPrimaryKey] = UniqueId(
                DbRows.Select(r => r.RowKey),
                $"{SelectedDbTable}-new");
        }

        SelectedDbRow = new DbRowItem(
            blank.GetValueOrDefault(_dbPrimaryKey ?? "", ""),
            blank,
            snap.Columns);
        Draft.IsReadOnly = false;
        Draft.Headline = $"DB / {SelectedDbTable} / 新建行";
        Draft.ShowId = Draft.ShowName = Draft.ShowType = Draft.ShowDriverId =
            Draft.ShowInterval = Draft.ShowDescription = Draft.ShowValue = Draft.ShowEnabled = false;
        Draft.ShowParameters = true;
        Draft.ParameterRows.Clear();
        foreach (var col in snap.Columns)
        {
            Draft.ParameterRows.Add(new KvPairRow { Key = col, Value = blank.GetValueOrDefault(col, "") });
        }

        Draft.SyncJsonFromRows();
        StatusLine = $"新建行 · 编辑右侧列后点「应用属性」写入 {SelectedDbTable}";
    }

    public void DeleteDbRow()
    {
        if (string.IsNullOrWhiteSpace(SelectedDbTable) || SelectedDbRow is null)
        {
            throw new InvalidOperationException("请先选择要删除的行。");
        }

        EnsureDbPathAvailable();
        using var store = new MdkConfigStore(_dbPath);
        if (!store.DeleteTableRow(SelectedDbTable, SelectedDbRow.RowKey))
        {
            throw new InvalidOperationException("删除失败：未找到该行。");
        }

        _dbCounts = store.CountTables();
        OpenDbTable(SelectedDbTable);
        StatusLine = $"已删除表 {SelectedDbTable} 中的行";
    }

    private void ClearDbTableView()
    {
        IsBrowsingDbTable = false;
        SelectedDbTable = null;
        SelectedDbRow = null;
        _dbPrimaryKey = null;
        DbColumns.Clear();
        DbRows.Clear();
        OnPropertyChanged(nameof(DbPrimaryKey));
    }

    private void EnsureDbPathAvailable()
    {
        if (string.IsNullOrWhiteSpace(_dbPath))
        {
            _dbPath = ResolveExportDbPath(null);
        }

        var dir = System.IO.Path.GetDirectoryName(_dbPath);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        // Touch/create schema via store ctor
        using var store = new MdkConfigStore(_dbPath);
        _dbCounts = store.CountTables();
        OnPropertyChanged(nameof(DatabasePath));
    }

    private void EnsureDbCounts()
    {
        try
        {
            EnsureDbPathAvailable();
        }
        catch
        {
            // leave counts null → hint row
        }
    }

    public CreateComponentRequest PrepareCreateRequest(string? preferredType = null)
    {
        if (!CanEditList)
        {
            throw new InvalidOperationException("当前模块不支持新建（GPIO 点位请在 GPIOs 模块编辑或 Excel 导入；Database 只读）。");
        }

        if (_module == ConfigModule.SysConfig)
        {
            throw new InvalidOperationException("系统配置键为固定集合，请直接编辑现有项。");
        }

        var typeOptions = ConfigTypeCatalog.TypesForModule(_module);
        var type = string.IsNullOrWhiteSpace(preferredType)
            ? ConfigTypeCatalog.DefaultType(_module)
            : preferredType.Trim();

        var req = new CreateComponentRequest
        {
            TypeOptions = typeOptions,
            DriverOptions = _setting.Drivers.Select(d => d.Id).Where(id => !string.IsNullOrWhiteSpace(id)).ToList(),
            Type = type,
            Enabled = true,
            IntervalMs = 100,
        };

        switch (_module)
        {
            case ConfigModule.Drivers:
                req.Id = UniqueId(_setting.Drivers.Select(d => d.Id), "drv-new");
                req.Name = req.Id;
                break;
            case ConfigModule.Devices:
            case ConfigModule.Axis:
                req.Id = UniqueId(AllDeviceIds(), "dev-new");
                req.Name = req.Id;
                req.DriverId = req.DriverOptions.FirstOrDefault() ?? "";
                break;
            case ConfigModule.Platform:
                req.Id = UniqueId(AllDeviceIds(), "plat-new");
                req.Name = req.Id;
                req.DriverId = "";
                break;
            case ConfigModule.Tasks:
                req.Name = UniqueId(_setting.Tasks.Select(t => t.Name), "task-new");
                req.DriverId = req.DriverOptions.FirstOrDefault() ?? "";
                break;
            case ConfigModule.Recipes:
                req.Id = UniqueId(_setting.Recipes.Select(r => r.Id), "recipe-new");
                req.Name = req.Id;
                {
                    var seed = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                    if (_setting.RecipeVarKeys.Count > 0)
                    {
                        foreach (var key in _setting.RecipeVarKeys.Where(k => !string.IsNullOrWhiteSpace(k)))
                        {
                            var k = key.Trim();
                            seed[k] = _setting.Vars.TryGetValue(k, out var value) ? value : "";
                        }
                    }

                    req.Vars = seed;
                    var candidates = GetRecipeVarCandidates();
                    req.VarCandidates = candidates;
                    req.KeySuggestions = candidates.Select(c => c.Key).ToList();
                }
                break;
            case ConfigModule.Visions:
                req.Id = UniqueId(_setting.Visions.Select(v => v.Id), "vision-new");
                req.Name = req.Id;
                req.Description = "";
                req.Parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["cameraDeviceId"] = "",
                };
                break;
            case ConfigModule.Vars:
                req.Id = UniqueId(_setting.Vars.Keys, "var.new");
                req.Value = "";
                break;
            default:
                throw new InvalidOperationException("当前模块不支持新建。");
        }

        // Seed type-matched default parameters so the create dialog shows them.
        if (_module is ConfigModule.Drivers or ConfigModule.Devices or ConfigModule.Axis
            or ConfigModule.Platform or ConfigModule.Tasks)
        {
            req.Parameters = ConfigTypeCatalog.DefaultParameters(_module, req.Type, req.DriverId);
        }

        return req;
    }

    public ComponentItem? CommitCreate(CreateComponentRequest req)
    {
        ComponentItem created = _module switch
        {
            ConfigModule.Drivers => CommitDriver(req),
            ConfigModule.Devices or ConfigModule.Axis or ConfigModule.Platform => CommitDevice(req),
            ConfigModule.Tasks => CommitTask(req),
            ConfigModule.Vars => CommitVar(req),
            ConfigModule.Recipes => CommitRecipe(req),
            ConfigModule.Visions => CommitVision(req),
            _ => throw new InvalidOperationException("当前模块不支持新建。"),
        };

        SelectModule(_module, created.Key);
        var item = Items.FirstOrDefault(i => i.Key == created.Key);
        SelectItem(item);
        return item;
    }

    /// <summary>Legacy helper — prefer dialog + <see cref="CommitCreate"/>.</summary>
    public ComponentItem? AddNew()
    {
        var req = PrepareCreateRequest();
        return CommitCreate(req);
    }

    public void ExportModule(string path)
    {
        var full = System.IO.Path.GetFullPath(path);
        object payload = _module switch
        {
            ConfigModule.Drivers => _setting.Drivers,
            ConfigModule.Devices => _setting.Devices.Where(IsDevicesModuleEntry).ToList(),
            ConfigModule.Axis => _setting.Axes.ToList(),
            ConfigModule.Platform => _setting.Platforms.ToList(),
            ConfigModule.Tasks => _setting.Tasks,
            ConfigModule.Recipes => _setting.Recipes,
            ConfigModule.Visions => _setting.Visions,
            ConfigModule.Vars => _setting.Vars,
            ConfigModule.SysConfig => new Dictionary<string, object?>
            {
                ["projectName"] = _setting.ProjectName,
                ["cycleMs"] = _setting.CycleMs,
                ["monitoringPrefix"] = _setting.MonitoringPrefix,
                ["startPage"] = _setting.StartPage,
                ["databasePath"] = _setting.DatabasePath,
                ["activeRecipeId"] = _setting.ActiveRecipeId,
                ["recipeVarKeys"] = _setting.RecipeVarKeys,
                ["activeVisionId"] = _setting.ActiveVisionId,
            },
            ConfigModule.Gpios => BuildGpioItems().Select(i =>
            {
                var g = (GpioEditTarget)i.Source!;
                return new
                {
                    Id = i.Col1,
                    Name = i.Col2,
                    Type = i.Col3,
                    Desc = i.Col4,
                    Enabled = i.Col5,
                    DriverId = g.DriverId,
                    Port = i.Col7,
                    DeviceId = g.Device.Id,
                    Alias = g.Alias,
                };
            }).ToList(),
            ConfigModule.Vios => BuildVioItems().Select(i =>
            {
                var v = (VioEditTarget)i.Source!;
                return new
                {
                    Id = i.Col1,
                    Name = i.Col2,
                    Type = i.Col3,
                    Desc = i.Col4,
                    Enabled = i.Col5,
                    DeviceId = i.Col6,
                    DriverId = v.Device.DriverId ?? "",
                    Alias = v.Alias,
                };
            }).ToList(),
            _ => throw new InvalidOperationException("当前模块不支持导出。"),
        };

        var json = JsonSerializer.Serialize(payload, JsonOptions);
        System.IO.File.WriteAllText(full, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        StatusLine = $"已导出模块 {ModuleTitle} → {full}";
    }

    public bool SupportsExcelModuleExchange =>
        _module is ConfigModule.Gpios or ConfigModule.Vios or ConfigModule.Axis or ConfigModule.Platform;

    /// <summary>VIOs 模块可一键补全/重置默认点位数量（vio.b1…vio.bN）。</summary>
    public bool SupportsVioDefaultLoad => _module == ConfigModule.Vios;

    /// <summary>
    /// Load default undirected VIO points (<c>vio.b1</c>…<c>vio.bN</c>) into the target vio device(s).
    /// When <paramref name="replaceAll"/> is false, only missing keys are filled.
    /// </summary>
    /// <returns>Number of devices updated.</returns>
    public int LoadVioDefaultPoints(bool replaceAll, int? bitCount = null)
    {
        if (_module != ConfigModule.Vios)
        {
            throw new InvalidOperationException("请先切换到 VIOs 模块。");
        }

        var devices = ResolveVioTargetDevices().ToList();
        if (devices.Count == 0)
        {
            throw new InvalidOperationException("没有可用的 vio 设备。请先在 Devices 中新建 type=vio 的设备。");
        }

        var template = VioDeviceParameterSet.DefaultParameters(bitCount ?? VioDeviceParameterSet.DefaultBitCount);
        foreach (var device in devices)
        {
            if (replaceAll)
            {
                var keys = device.Parameters.Keys
                    .Where(k => k.StartsWith("in.", StringComparison.OrdinalIgnoreCase)
                                || k.StartsWith("out.", StringComparison.OrdinalIgnoreCase)
                                || k.StartsWith("desc.", StringComparison.OrdinalIgnoreCase)
                                || VioDeviceParameterSet.IsUndirectedBitKey(k))
                    .ToList();
                foreach (var k in keys)
                {
                    device.Parameters.Remove(k);
                }

                foreach (var kv in template)
                {
                    device.Parameters[kv.Key] = kv.Value;
                }
            }
            else
            {
                foreach (var kv in template)
                {
                    if (!device.Parameters.TryGetValue(kv.Key, out var cur) || string.IsNullOrWhiteSpace(cur))
                    {
                        device.Parameters[kv.Key] = kv.Value;
                    }
                }
            }
        }

        var keep = _selected?.Key;
        SelectModule(ConfigModule.Vios, keepSelectionKey: keep);
        StatusLine = replaceAll
            ? $"已重置 {devices.Count} 个 vio 设备默认点位（{template.Count} 项）"
            : $"已补全 {devices.Count} 个 vio 设备缺失点位（模板 {template.Count} 项）";
        return devices.Count;
    }

    private IEnumerable<MdkSetting.DeviceConfig> ResolveVioTargetDevices()
    {
        if (_selected?.Source is VioEditTarget v
            && string.Equals(v.Device.Type, "vio", StringComparison.OrdinalIgnoreCase))
        {
            yield return v.Device;
            yield break;
        }

        foreach (var d in _setting.Devices.Where(x =>
                     string.Equals(x.Type, "vio", StringComparison.OrdinalIgnoreCase)))
        {
            yield return d;
        }
    }

    /// <summary>Export current Gpios / Vios / Axis / Platform list as SpreadsheetML (.xls).</summary>
    public void ExportModuleExcel(string path)
    {
        var full = System.IO.Path.GetFullPath(path);
        switch (_module)
        {
            case ConfigModule.Gpios:
                ExportGpiosExcel(full);
                break;
            case ConfigModule.Vios:
                ExportViosExcel(full);
                break;
            case ConfigModule.Axis:
                ExportAxisExcel(full);
                break;
            case ConfigModule.Platform:
                ExportPlatformExcel(full);
                break;
            default:
                throw new InvalidOperationException("仅 Gpios / Vios / Axis / Platform 支持 Excel 导出。");
        }

        StatusLine = $"已导出 Excel {ModuleTitle} → {full}";
    }

    /// <summary>Import Gpios / Vios / Axis / Platform list from SpreadsheetML (.xls) or CSV.</summary>
    public void ImportModuleExcel(string path, bool replace)
    {
        var full = System.IO.Path.GetFullPath(path);
        switch (_module)
        {
            case ConfigModule.Gpios:
                ImportGpiosExcel(full, replace);
                break;
            case ConfigModule.Vios:
                ImportViosExcel(full, replace);
                break;
            case ConfigModule.Axis:
                ImportAxisExcel(full, replace);
                break;
            case ConfigModule.Platform:
                ImportPlatformExcel(full, replace);
                break;
            default:
                throw new InvalidOperationException("仅 Gpios / Vios / Axis / Platform 支持 Excel 导入。");
        }

        SelectModule(_module, null);
        StatusLine = $"已导入 Excel {ModuleTitle} ← {full}";
    }

    private void ExportGpiosExcel(string path)
    {
        var headers = new[] { "DeviceId", "Name", "Type", "Desc", "Enabled", "DriverId", "Port" };
        var rows = BuildGpioItems().Select(i =>
        {
            var g = (GpioEditTarget)i.Source!;
            return (IReadOnlyList<string>)new[]
            {
                g.Device.Id,
                g.Alias,
                g.Direction,
                i.Col4,
                g.Device.Enabled ? "True" : "False",
                g.DriverId,
                g.Port,
            };
        });
        ExcelSheetIo.WriteSheet(path, "Gpios", headers, rows);
    }

    private void ImportGpiosExcel(string path, bool replace)
    {
        var (_, rows) = ExcelSheetIo.ReadSheet(path);
        if (rows.Count == 0)
        {
            throw new InvalidOperationException("Excel 中没有 IO 行。");
        }

        // Group by device; create gpio device if missing.
        var byDevice = rows
            .Select(r =>
            {
                var name = Cell(r, "Name", "Alias", "alias");
                var deviceId = Cell(r, "DeviceId", "Device", "device");
                var driverId = Cell(r, "DriverId", "driverId", "driver");
                // Name may be deviceId.alias (legacy) or driverId.alias (list display).
                if (name.Contains('.', StringComparison.Ordinal))
                {
                    var dot = name.IndexOf('.');
                    var prefix = name[..dot].Trim();
                    var suffix = name[(dot + 1)..].Trim();
                    if (!string.IsNullOrWhiteSpace(prefix) && !string.IsNullOrWhiteSpace(suffix))
                    {
                        var prefixIsDriver = _setting.Drivers.Any(d =>
                            string.Equals(d.Id, prefix, StringComparison.OrdinalIgnoreCase));
                        var prefixIsGpioDevice = _setting.Devices.Any(d =>
                            string.Equals(d.Id, prefix, StringComparison.OrdinalIgnoreCase)
                            && string.Equals(d.Type, "gpio", StringComparison.OrdinalIgnoreCase));
                        if (string.IsNullOrWhiteSpace(deviceId) && prefixIsGpioDevice)
                        {
                            deviceId = prefix;
                            name = suffix;
                        }
                        else if (prefixIsDriver)
                        {
                            if (string.IsNullOrWhiteSpace(driverId))
                            {
                                driverId = prefix;
                            }

                            name = suffix;
                        }
                        else if (string.IsNullOrWhiteSpace(deviceId))
                        {
                            deviceId = prefix;
                            name = suffix;
                        }
                    }
                }

                if (string.IsNullOrWhiteSpace(deviceId))
                {
                    deviceId = _setting.Devices
                        .FirstOrDefault(d => string.Equals(d.Type, "gpio", StringComparison.OrdinalIgnoreCase))
                        ?.Id
                        ?? "gpio-main";
                }

                var port = Cell(r, "Port", "Address", "address", "port");
                var route = Cell(r, "Route", "route");
                if (string.IsNullOrWhiteSpace(port) && !string.IsNullOrWhiteSpace(route))
                {
                    if (GpioDeviceParameterSet.TryParsePointValue(route, driverId, out var drv, out var addr, out _)
                        || GpioDeviceParameterSet.TryParsePointRoute(route, out drv, out addr))
                    {
                        if (string.IsNullOrWhiteSpace(driverId))
                        {
                            driverId = drv;
                        }

                        port = addr;
                    }
                    else
                    {
                        port = route;
                    }
                }

                return new
                {
                    DeviceId = deviceId,
                    Alias = name,
                    Direction = NormalizeDirection(Cell(r, "Type", "Direction", "Dir", "direction")),
                    Label = Cell(r, "Desc", "Label", "label"),
                    Enabled = Cell(r, "Enabled", "Enable", "enabled"),
                    DriverId = driverId,
                    Port = port,
                };
            })
            .Where(r => !string.IsNullOrWhiteSpace(r.DeviceId) && !string.IsNullOrWhiteSpace(r.Alias))
            .GroupBy(r => r.DeviceId, StringComparer.OrdinalIgnoreCase);

        if (replace)
        {
            foreach (var device in _setting.Devices.Where(d =>
                         string.Equals(d.Type, "gpio", StringComparison.OrdinalIgnoreCase)))
            {
                var keys = device.Parameters.Keys
                    .Where(k => k.StartsWith("in.", StringComparison.OrdinalIgnoreCase)
                                || k.StartsWith("out.", StringComparison.OrdinalIgnoreCase)
                                || k.StartsWith("desc.", StringComparison.OrdinalIgnoreCase))
                    .ToList();
                foreach (var k in keys)
                {
                    device.Parameters.Remove(k);
                }
            }
        }

        foreach (var group in byDevice)
        {
            var device = _setting.Devices.FirstOrDefault(d =>
                string.Equals(d.Id, group.Key, StringComparison.OrdinalIgnoreCase));
            if (device is null)
            {
                device = new MdkSetting.DeviceConfig
                {
                    Id = group.Key,
                    Name = group.Key,
                    Type = "gpio",
                    Enabled = true,
                    Parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
                };
                _setting.Devices.Add(device);
            }

            if (string.Equals(device.Type, "vio", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"设备 '{device.Id}' 类型为 vio，请在 VIOs 模块导入；Gpios 仅支持 gpio。");
            }

            device.Type = "gpio";
            var drivers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var row in group)
            {
                if (!string.IsNullOrWhiteSpace(row.Enabled)
                    && bool.TryParse(row.Enabled, out var enabled))
                {
                    device.Enabled = enabled;
                }

                var paramKeyGpio = $"{row.Direction}.{row.Alias}";
                var drv = string.IsNullOrWhiteSpace(row.DriverId) ? device.DriverId : row.DriverId.Trim();
                var addr = (row.Port ?? "").Trim();
                if (string.IsNullOrWhiteSpace(addr))
                {
                    throw new InvalidOperationException($"GPIO '{row.Alias}' 缺少 Port。");
                }

                if (string.IsNullOrWhiteSpace(drv))
                {
                    throw new InvalidOperationException($"GPIO '{row.Alias}' 缺少 DriverId（或设备默认 DriverId）。");
                }

                device.Parameters[paramKeyGpio] = GpioDeviceParameterSet.FormatPointValue(
                    drv, addr, row.Label, device.DriverId);
                drivers.Add(drv);
            }

            if (string.IsNullOrWhiteSpace(device.DriverId) && drivers.Count == 1)
            {
                device.DriverId = drivers.First();
            }
        }
    }

    private void ExportViosExcel(string path)
    {
        var headers = new[] { "DeviceId", "Name", "Type", "Desc", "Enabled", "DriverId" };
        var rows = BuildVioItems().Select(i =>
        {
            var v = (VioEditTarget)i.Source!;
            return (IReadOnlyList<string>)new[]
            {
                v.Device.Id,
                v.Alias,
                "vio",
                i.Col4,
                v.Device.Enabled ? "True" : "False",
                v.Device.DriverId ?? "",
            };
        });
        ExcelSheetIo.WriteSheet(path, "Vios", headers, rows);
    }

    private void ImportViosExcel(string path, bool replace)
    {
        var (_, rows) = ExcelSheetIo.ReadSheet(path);
        if (rows.Count == 0)
        {
            throw new InvalidOperationException("Excel 中没有 VIO 行。");
        }

        var byDevice = rows
            .Select(r =>
            {
                var name = Cell(r, "Name", "Alias", "alias");
                var deviceId = Cell(r, "DeviceId", "Device", "device");
                if (string.IsNullOrWhiteSpace(deviceId) && name.Contains('.', StringComparison.Ordinal))
                {
                    var dot = name.IndexOf('.');
                    deviceId = name[..dot];
                    name = name[(dot + 1)..];
                }

                return new
                {
                    DeviceId = deviceId,
                    Alias = name.Trim(),
                    Label = Cell(r, "Desc", "Label", "label"),
                    Enabled = Cell(r, "Enabled", "Enable", "enabled"),
                    DriverId = Cell(r, "DriverId", "driverId", "driver"),
                };
            })
            .Where(r => !string.IsNullOrWhiteSpace(r.DeviceId) && !string.IsNullOrWhiteSpace(r.Alias))
            .GroupBy(r => r.DeviceId, StringComparer.OrdinalIgnoreCase);

        if (replace)
        {
            foreach (var device in _setting.Devices.Where(d =>
                         string.Equals(d.Type, "vio", StringComparison.OrdinalIgnoreCase)))
            {
                var keys = device.Parameters.Keys
                    .Where(k => k.StartsWith("in.", StringComparison.OrdinalIgnoreCase)
                                || k.StartsWith("out.", StringComparison.OrdinalIgnoreCase)
                                || k.StartsWith("desc.", StringComparison.OrdinalIgnoreCase)
                                || VioDeviceParameterSet.IsUndirectedBitKey(k))
                    .ToList();
                foreach (var k in keys)
                {
                    device.Parameters.Remove(k);
                }
            }
        }

        foreach (var group in byDevice)
        {
            var device = _setting.Devices.FirstOrDefault(d =>
                string.Equals(d.Id, group.Key, StringComparison.OrdinalIgnoreCase));
            if (device is null)
            {
                device = new MdkSetting.DeviceConfig
                {
                    Id = group.Key,
                    Name = group.Key,
                    Type = "vio",
                    Enabled = true,
                    Parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
                };
                _setting.Devices.Add(device);
            }

            if (!string.Equals(device.Type, "vio", StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(device.Type)
                && !string.Equals(device.Type, "gpio", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"设备 '{device.Id}' 类型为 '{device.Type}'，不能导入为 VIO 点位。");
            }

            device.Type = "vio";
            foreach (var row in group)
            {
                if (!string.IsNullOrWhiteSpace(row.Enabled)
                    && bool.TryParse(row.Enabled, out var enabled))
                {
                    device.Enabled = enabled;
                }

                if (!string.IsNullOrWhiteSpace(row.DriverId))
                {
                    device.DriverId = row.DriverId.Trim();
                }

                var alias = row.Alias.Trim();
                // Canonical undirected key: prefer vio.bN; otherwise keep alias as-is.
                var paramKey = alias;
                if (!VioDeviceParameterSet.IsUndirectedBitKey(paramKey)
                    && alias.StartsWith("b", StringComparison.OrdinalIgnoreCase)
                    && int.TryParse(alias.AsSpan(1), out _))
                {
                    paramKey = $"vio.{alias}";
                }

                device.Parameters[paramKey] = string.IsNullOrWhiteSpace(row.Label)
                    ? "virtual"
                    : $"virtual|{row.Label.Trim()}";
            }
        }
    }

    private void ExportAxisExcel(string path)
    {
        var headers = new[]
        {
            "Id", "Name", "Type", "DriverId", "Enabled",
            "kind", "axis", "model", "homeVel", "pulsePerUnit", "maxVel", "accel",
            "negLimit", "posLimit", "homeSensor", "softNeg", "softPos", "unit", "continuous", "note",
        };
        var axes = _setting.Axes;
        var rows = axes.Select(d =>
        {
            var p = d.Parameters;
            return (IReadOnlyList<string>)new[]
            {
                d.Id,
                d.Name,
                d.Type,
                d.DriverId,
                d.Enabled ? "True" : "False",
                p.GetValueOrDefault("kind", AxisDeviceParameterSet.GetKindToken(p, d.Type)),
                p.GetValueOrDefault("axis", AxisDeviceParameterSet.ParseAxisIndex(p).ToString()),
                p.GetValueOrDefault("model", AxisDeviceParameterSet.GetModel(p)),
                p.GetValueOrDefault("homeVel", ""),
                p.GetValueOrDefault("pulsePerUnit", ""),
                p.GetValueOrDefault("maxVel", ""),
                p.GetValueOrDefault("accel", ""),
                p.GetValueOrDefault("negLimit", ""),
                p.GetValueOrDefault("posLimit", ""),
                p.GetValueOrDefault("homeSensor", ""),
                p.GetValueOrDefault("softNeg", ""),
                p.GetValueOrDefault("softPos", ""),
                p.GetValueOrDefault("unit", ""),
                p.GetValueOrDefault("continuous", ""),
                p.GetValueOrDefault("note", ""),
            };
        });
        ExcelSheetIo.WriteSheet(path, "Axis", headers, rows);
    }

    private void ImportAxisExcel(string path, bool replace)
    {
        var (_, rows) = ExcelSheetIo.ReadSheet(path);
        var devices = rows
            .Select(ParseAxisRow)
            .Where(d => d is not null)
            .Cast<MdkSetting.DeviceConfig>()
            .ToList();
        if (devices.Count == 0)
        {
            throw new InvalidOperationException("Excel 中没有有效的 Axis 行。");
        }

        MergeDevices(_setting.Axes, devices, replace);
    }

    private static MdkSetting.DeviceConfig? ParseAxisRow(Dictionary<string, string> r)
    {
        var id = Cell(r, "Id", "id");
        if (string.IsNullOrWhiteSpace(id))
        {
            return null;
        }

        var type = Cell(r, "Type", "type");
        if (string.IsNullOrWhiteSpace(type))
        {
            type = "linear";
        }

        var kind = Cell(r, "kind", "Kind");
        if (string.IsNullOrWhiteSpace(kind))
        {
            kind = AxisDeviceParameterSet.TryKindFromDeviceType(type, out _)
                ? type
                : "linear";
        }

        var defaults = AxisDeviceParameterSet.DefaultParameters(kind);
        var parameters = new Dictionary<string, string>(defaults, StringComparer.OrdinalIgnoreCase);
        parameters[AxisDeviceParameterSet.KeyKind] = AxisDeviceParameterSet.GetKindToken(parameters, kind);
        foreach (var key in new[]
                 {
                     "axis", "model", "homeVel", "pulsePerUnit", "maxVel", "accel",
                     "negLimit", "posLimit", "homeSensor", "softNeg", "softPos", "unit", "continuous", "note",
                 })
        {
            var v = Cell(r, key);
            if (!string.IsNullOrWhiteSpace(v))
            {
                parameters[key] = v.Trim();
            }
        }

        // Prefer geometry shorthand as Type when recognized; keep "axis" for legacy rows.
        if (!AxisDeviceParameterSet.IsAxisFamilyType(type))
        {
            type = AxisDeviceParameterSet.TryKindFromDeviceType(kind, out var k)
                ? k.ToConfigToken()
                : "linear";
        }

        return new MdkSetting.DeviceConfig
        {
            Id = id.Trim(),
            Name = Cell(r, "Name", "name").Trim().Length > 0 ? Cell(r, "Name", "name").Trim() : id.Trim(),
            Type = type.Trim(),
            DriverId = Cell(r, "DriverId", "driverId").Trim(),
            Enabled = ParseBool(Cell(r, "Enabled", "enabled"), true),
            Parameters = parameters,
        };
    }

    private void ExportPlatformExcel(string path)
    {
        var headers = new[]
        {
            "Id", "Name", "Type", "Enabled", "note",
            "axis.X", "axis.Y", "axis.Z", "axis.U", "axis.V", "axis.W",
        };
        var plats = _setting.Platforms;
        var rows = plats.Select(d =>
        {
            var p = PlatformDeviceParameterSet.NormalizeParameters(d.Type, d.Parameters);
            return (IReadOnlyList<string>)new[]
            {
                d.Id,
                d.Name,
                d.Type,
                d.Enabled ? "True" : "False",
                p.GetValueOrDefault("note", ""),
                p.GetValueOrDefault("axis.X", ""),
                p.GetValueOrDefault("axis.Y", ""),
                p.GetValueOrDefault("axis.Z", ""),
                p.GetValueOrDefault("axis.U", ""),
                p.GetValueOrDefault("axis.V", ""),
                p.GetValueOrDefault("axis.W", ""),
            };
        });
        ExcelSheetIo.WriteSheet(path, "Platform", headers, rows);
    }

    private void ImportPlatformExcel(string path, bool replace)
    {
        var (_, rows) = ExcelSheetIo.ReadSheet(path);
        var devices = rows
            .Select(ParsePlatformRow)
            .Where(d => d is not null)
            .Cast<MdkSetting.DeviceConfig>()
            .ToList();
        if (devices.Count == 0)
        {
            throw new InvalidOperationException("Excel 中没有有效的 Platform 行。");
        }

        MergeDevices(_setting.Platforms, devices, replace, IsPlatformModuleEntry);
    }

    private static MdkSetting.DeviceConfig? ParsePlatformRow(Dictionary<string, string> r)
    {
        var id = Cell(r, "Id", "id");
        if (string.IsNullOrWhiteSpace(id))
        {
            return null;
        }

        var type = Cell(r, "Type", "type");
        if (string.IsNullOrWhiteSpace(type))
        {
            type = "xyz";
        }

        var kind = Cell(r, "kind", "Kind");
        if (string.IsNullOrWhiteSpace(kind))
        {
            kind = type;
        }

        var parameters = PlatformDeviceParameterSet.DefaultParameters(kind);
        var note = Cell(r, "note", "Note");
        if (!string.IsNullOrWhiteSpace(note))
        {
            parameters["note"] = note.Trim();
        }

        foreach (var letter in new[] { "X", "Y", "Z", "U", "V", "W" })
        {
            var axisId = Cell(r, $"axis.{letter}");
            if (!string.IsNullOrWhiteSpace(axisId))
            {
                parameters[$"axis.{letter}"] = axisId.Trim();
            }
        }

        parameters = PlatformDeviceParameterSet.NormalizeParameters(type, parameters);

        return new MdkSetting.DeviceConfig
        {
            Id = id.Trim(),
            Name = Cell(r, "Name", "name").Trim().Length > 0 ? Cell(r, "Name", "name").Trim() : id.Trim(),
            Type = type.Trim(),
            DriverId = "",
            Enabled = ParseBool(Cell(r, "Enabled", "enabled"), true),
            Parameters = parameters,
        };
    }

    private static string Cell(Dictionary<string, string> row, params string[] names)
    {
        foreach (var name in names)
        {
            if (row.TryGetValue(name, out var v) && v is not null)
            {
                return v;
            }
        }

        return "";
    }

    private static string NormalizeDirection(string raw)
    {
        var t = (raw ?? "").Trim().ToLowerInvariant();
        if (t is "out" or "do" or "output")
        {
            return "out";
        }

        return "in";
    }

    private static bool ParseBool(string raw, bool fallback)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return fallback;
        }

        if (bool.TryParse(raw, out var b))
        {
            return b;
        }

        return raw.Trim() switch
        {
            "1" or "yes" or "y" or "是" => true,
            "0" or "no" or "n" or "否" => false,
            _ => fallback,
        };
    }

    public void ImportModule(string path, bool replace)
    {
        var full = System.IO.Path.GetFullPath(path);
        var json = System.IO.File.ReadAllText(full);

        switch (_module)
        {
            case ConfigModule.Drivers:
            {
                var rows = JsonSerializer.Deserialize<List<MdkSetting.DriverConfig>>(json, JsonOptions) ?? [];
                if (replace)
                {
                    _setting.Drivers = rows;
                }
                else
                {
                    foreach (var row in rows)
                    {
                        var existing = _setting.Drivers.FirstOrDefault(d =>
                            string.Equals(d.Id, row.Id, StringComparison.OrdinalIgnoreCase));
                        if (existing is null)
                        {
                            _setting.Drivers.Add(row);
                        }
                        else
                        {
                            existing.Name = row.Name;
                            existing.Type = row.Type;
                            existing.Enabled = row.Enabled;
                            existing.Parameters = row.Parameters;
                        }
                    }
                }

                break;
            }
            case ConfigModule.Devices:
            {
                var rows = (JsonSerializer.Deserialize<List<MdkSetting.DeviceConfig>>(json, JsonOptions) ?? [])
                    .Where(IsDevicesModuleEntry)
                    .ToList();
                MergeDevices(_setting.Devices, rows, replace, IsDevicesModuleEntry);
                break;
            }
            case ConfigModule.Axis:
            {
                var rows = JsonSerializer.Deserialize<List<MdkSetting.DeviceConfig>>(json, JsonOptions) ?? [];
                foreach (var row in rows)
                {
                    row.Parameters ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    if (!AxisDeviceParameterSet.IsAxisFamilyType(row.Type))
                    {
                        row.Type = AxisDeviceParameterSet.TryKindFromDeviceType(
                            row.Parameters.GetValueOrDefault(AxisDeviceParameterSet.KeyKind),
                            out var k)
                            ? k.ToConfigToken()
                            : "linear";
                    }

                    AxisDeviceParameterSet.SyncKindParameter(row.Parameters, row.Type);
                }

                MergeDevices(_setting.Axes, rows, replace);
                break;
            }
            case ConfigModule.Platform:
            {
                var rows = (JsonSerializer.Deserialize<List<MdkSetting.DeviceConfig>>(json, JsonOptions) ?? [])
                    .Where(IsPlatformModuleEntry)
                    .ToList();
                MergeDevices(_setting.Platforms, rows, replace, IsPlatformModuleEntry);
                break;
            }
            case ConfigModule.Tasks:
            {
                var rows = JsonSerializer.Deserialize<List<MdkSetting.TaskConfig>>(json, JsonOptions) ?? [];
                _setting.Tasks = replace ? rows : MergeByKey(_setting.Tasks, rows, t => t.Name, (a, b) =>
                {
                    a.Type = b.Type;
                    a.DriverId = b.DriverId;
                    a.IntervalMs = b.IntervalMs;
                    a.Parameters = b.Parameters;
                });
                break;
            }
            case ConfigModule.Recipes:
            {
                var rows = JsonSerializer.Deserialize<List<MdkSetting.RecipeConfig>>(json, JsonOptions) ?? [];
                _setting.Recipes = replace ? rows : MergeByKey(_setting.Recipes, rows, r => r.Id, (a, b) =>
                {
                    a.Name = b.Name;
                    a.Description = b.Description;
                    a.Vars = b.Vars;
                });
                break;
            }
            case ConfigModule.Visions:
            {
                var rows = JsonSerializer.Deserialize<List<MdkSetting.VisionConfig>>(json, JsonOptions) ?? [];
                _setting.Visions = replace ? rows : MergeByKey(_setting.Visions, rows, v => v.Id, (a, b) =>
                {
                    a.Name = b.Name;
                    a.Description = b.Description;
                    a.CameraDeviceId = b.CameraDeviceId;
                    a.Pipeline = b.Pipeline;
                });
                break;
            }
            case ConfigModule.Vars:
            {
                var rows = JsonSerializer.Deserialize<Dictionary<string, object?>>(json, JsonOptions)
                           ?? new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                if (replace)
                {
                    _setting.Vars = new Dictionary<string, object?>(rows, StringComparer.OrdinalIgnoreCase);
                }
                else
                {
                    foreach (var kv in rows)
                    {
                        _setting.Vars[kv.Key] = kv.Value;
                    }
                }

                break;
            }
            default:
                throw new InvalidOperationException("当前模块不支持导入（GPIO/SysConfig/Database 请用整包或对应模块）。");
        }

        SelectModule(_module, null);
        StatusLine = $"已导入模块 {ModuleTitle} ← {full}";
    }

    private void MergeDevices(
        List<MdkSetting.DeviceConfig> target,
        List<MdkSetting.DeviceConfig> rows,
        bool replace,
        Func<MdkSetting.DeviceConfig, bool>? filter = null)
    {
        if (replace && filter is null)
        {
            target.Clear();
            target.AddRange(rows);
            return;
        }

        if (replace && filter is not null)
        {
            target.RemoveAll(d => filter(d));
        }

        foreach (var row in rows)
        {
            var existing = target.FirstOrDefault(d =>
                string.Equals(d.Id, row.Id, StringComparison.OrdinalIgnoreCase));
            if (existing is null)
            {
                target.Add(row);
            }
            else
            {
                existing.Name = row.Name;
                existing.Type = row.Type;
                existing.DriverId = row.DriverId;
                existing.Enabled = row.Enabled;
                existing.Parameters = row.Parameters;
            }
        }
    }

    private static List<T> MergeByKey<T>(
        List<T> existing,
        List<T> incoming,
        Func<T, string> keySelector,
        Action<T, T> update)
    {
        foreach (var row in incoming)
        {
            var key = keySelector(row);
            var found = existing.FirstOrDefault(x =>
                string.Equals(keySelector(x), key, StringComparison.OrdinalIgnoreCase));
            if (found is null)
            {
                existing.Add(row);
            }
            else
            {
                update(found, row);
            }
        }

        return existing;
    }

    private ComponentItem CommitDriver(CreateComponentRequest req)
    {
        if (_setting.Drivers.Any(d => string.Equals(d.Id, req.Id, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException($"驱动 Id 已存在: {req.Id}");
        }

        var d = new MdkSetting.DriverConfig
        {
            Id = req.Id,
            Name = string.IsNullOrWhiteSpace(req.Name) ? req.Id : req.Name,
            Type = string.IsNullOrWhiteSpace(req.Type) ? "sim" : req.Type,
            Enabled = req.Enabled,
            Parameters = new Dictionary<string, string>(req.Parameters, StringComparer.OrdinalIgnoreCase),
        };
        _setting.Drivers.Add(d);
        return new ComponentItem { Key = d.Id, Source = d, Module = ConfigModule.Drivers, Title = d.Id };
    }

    private ComponentItem CommitDevice(CreateComponentRequest req)
    {
        if (DeviceIdExists(req.Id))
        {
            throw new InvalidOperationException($"设备 Id 已存在: {req.Id}");
        }

        var type = string.IsNullOrWhiteSpace(req.Type) ? ConfigTypeCatalog.DefaultType(_module) : req.Type;
        EnsureDeviceTypeForModule(_module, type);
        var parameters = new Dictionary<string, string>(req.Parameters, StringComparer.OrdinalIgnoreCase);
        if (_module == ConfigModule.Platform)
        {
            parameters = PlatformDeviceParameterSet.NormalizeParameters(type, parameters);
        }
        else if (_module == ConfigModule.Axis)
        {
            AxisDeviceParameterSet.SyncKindParameter(parameters, type);
        }

        var d = new MdkSetting.DeviceConfig
        {
            Id = req.Id,
            Name = string.IsNullOrWhiteSpace(req.Name) ? req.Id : req.Name,
            Type = type,
            DriverId = _module == ConfigModule.Platform ? "" : req.DriverId,
            Enabled = req.Enabled,
            Parameters = parameters,
        };
        DeviceBucket(_module).Add(d);
        return new ComponentItem { Key = d.Id, Source = d, Module = _module, Title = d.Id };
    }

    private ComponentItem CommitTask(CreateComponentRequest req)
    {
        if (_setting.Tasks.Any(t => string.Equals(t.Name, req.Name, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException($"任务 Name 已存在: {req.Name}");
        }

        var t = new MdkSetting.TaskConfig
        {
            Name = req.Name,
            Type = string.IsNullOrWhiteSpace(req.Type) ? "pollDriver" : req.Type,
            DriverId = req.DriverId,
            IntervalMs = req.IntervalMs <= 0 ? 100 : req.IntervalMs,
            Parameters = new Dictionary<string, string>(req.Parameters, StringComparer.OrdinalIgnoreCase),
        };
        _setting.Tasks.Add(t);
        return new ComponentItem { Key = t.Name, Source = t, Module = ConfigModule.Tasks, Title = t.Name };
    }

    private ComponentItem CommitVar(CreateComponentRequest req)
    {
        if (_setting.Vars.ContainsKey(req.Id))
        {
            throw new InvalidOperationException($"变量 Key 已存在: {req.Id}");
        }

        object? value = req.Value;
        if (bool.TryParse(req.Value, out var b))
        {
            value = b;
        }
        else if (long.TryParse(req.Value, out var l))
        {
            value = l;
        }

        _setting.Vars[req.Id] = value;
        return new ComponentItem
        {
            Key = req.Id,
            Module = ConfigModule.Vars,
            Title = req.Id,
            Source = new KeyValuePair<string, object?>(req.Id, value),
        };
    }

    private ComponentItem CommitRecipe(CreateComponentRequest req)
    {
        if (_setting.Recipes.Any(r => string.Equals(r.Id, req.Id, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException($"配方 Id 已存在: {req.Id}");
        }

        var r = new MdkSetting.RecipeConfig
        {
            Id = req.Id,
            Name = string.IsNullOrWhiteSpace(req.Name) ? req.Id : req.Name,
            Description = string.IsNullOrWhiteSpace(req.Description) ? null : req.Description,
            Vars = new Dictionary<string, object?>(req.Vars, StringComparer.OrdinalIgnoreCase),
        };
        _setting.Recipes.Add(r);
        return new ComponentItem { Key = r.Id, Source = r, Module = ConfigModule.Recipes, Title = r.Id };
    }

    private ComponentItem CommitVision(CreateComponentRequest req)
    {
        if (_setting.Visions.Any(v => string.Equals(v.Id, req.Id, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException($"视觉流程 Id 已存在: {req.Id}");
        }

        var cameraId = req.Parameters.GetValueOrDefault("cameraDeviceId", "") ?? "";
        var v = new MdkSetting.VisionConfig
        {
            Id = req.Id,
            Name = string.IsNullOrWhiteSpace(req.Name) ? req.Id : req.Name,
            Description = string.IsNullOrWhiteSpace(req.Description) ? null : req.Description,
            CameraDeviceId = cameraId,
            Pipeline = MDKOSS.Core.Vision.VisionDocument.CreateBasicInspectPipeline(),
        };
        _setting.Visions.Add(v);
        return new ComponentItem { Key = v.Id, Source = v, Module = ConfigModule.Visions, Title = v.Id };
    }

    public void DuplicateSelected()
    {
        if (_selected is null)
        {
            throw new InvalidOperationException("请先选择组件。");
        }

        switch (_module)
        {
            case ConfigModule.Drivers when _selected.Source is MdkSetting.DriverConfig d:
                var nd = CloneDriver(d);
                nd.Id = UniqueId(_setting.Drivers.Select(x => x.Id), d.Id + "_copy");
                _setting.Drivers.Add(nd);
                SelectModule(_module, nd.Id);
                break;
            case ConfigModule.Devices or ConfigModule.Axis or ConfigModule.Platform
                when _selected.Source is MdkSetting.DeviceConfig dev:
                var ndev = CloneDevice(dev);
                ndev.Id = UniqueId(AllDeviceIds(), dev.Id + "_copy");
                DeviceBucket(_module).Add(ndev);
                SelectModule(_module, ndev.Id);
                break;
            case ConfigModule.Tasks when _selected.Source is MdkSetting.TaskConfig t:
                var nt = CloneTask(t);
                nt.Name = UniqueId(_setting.Tasks.Select(x => x.Name), t.Name + "_copy");
                _setting.Tasks.Add(nt);
                SelectModule(_module, nt.Name);
                break;
            case ConfigModule.Recipes when _selected.Source is MdkSetting.RecipeConfig r:
                var nr = CloneRecipe(r);
                nr.Id = UniqueId(_setting.Recipes.Select(x => x.Id), r.Id + "_copy");
                _setting.Recipes.Add(nr);
                SelectModule(_module, nr.Id);
                break;
            case ConfigModule.Visions when _selected.Source is MdkSetting.VisionConfig v:
                var nv = CloneVision(v);
                nv.Id = UniqueId(_setting.Visions.Select(x => x.Id), v.Id + "_copy");
                _setting.Visions.Add(nv);
                SelectModule(_module, nv.Id);
                break;
            case ConfigModule.Vars when _selected.Source is KeyValuePair<string, object?> kv:
                var newKey = UniqueId(_setting.Vars.Keys, kv.Key + "_copy");
                _setting.Vars[newKey] = kv.Value;
                SelectModule(_module, newKey);
                break;
            default:
                throw new InvalidOperationException("当前项不支持复制。");
        }
    }

    public void DeleteSelected()
    {
        if (_selected is null)
        {
            throw new InvalidOperationException("请先选择组件。");
        }

        switch (_module)
        {
            case ConfigModule.Drivers when _selected.Source is MdkSetting.DriverConfig d:
                _setting.Drivers.Remove(d);
                break;
            case ConfigModule.Devices or ConfigModule.Axis or ConfigModule.Platform
                when _selected.Source is MdkSetting.DeviceConfig dev:
                DeviceBucket(_module).Remove(dev);
                break;
            case ConfigModule.Tasks when _selected.Source is MdkSetting.TaskConfig t:
                _setting.Tasks.Remove(t);
                break;
            case ConfigModule.Recipes when _selected.Source is MdkSetting.RecipeConfig r:
                _setting.Recipes.Remove(r);
                break;
            case ConfigModule.Visions when _selected.Source is MdkSetting.VisionConfig v:
                _setting.Visions.Remove(v);
                break;
            case ConfigModule.Vars:
                _setting.Vars.Remove(_selected.Key);
                break;
            case ConfigModule.SysConfig:
                throw new InvalidOperationException("系统配置项不可删除。");
            default:
                throw new InvalidOperationException("当前项不支持删除。");
        }

        SelectModule(_module, null);
    }

    public void MoveSelected(int delta)
    {
        if (_selected is null)
        {
            throw new InvalidOperationException("请先选择组件。");
        }

        switch (_module)
        {
            case ConfigModule.Drivers:
                MoveInList(_setting.Drivers, _selected.Source as MdkSetting.DriverConfig, delta);
                break;
            case ConfigModule.Devices:
                MoveAmongFiltered(
                    _setting.Devices,
                    _selected.Source as MdkSetting.DeviceConfig,
                    delta,
                    IsDevicesModuleEntry);
                break;
            case ConfigModule.Axis:
                MoveInList(_setting.Axes, _selected.Source as MdkSetting.DeviceConfig, delta);
                break;
            case ConfigModule.Platform:
                MoveInList(_setting.Platforms, _selected.Source as MdkSetting.DeviceConfig, delta);
                break;
            case ConfigModule.Tasks:
                MoveInList(_setting.Tasks, _selected.Source as MdkSetting.TaskConfig, delta);
                break;
            case ConfigModule.Recipes:
                MoveInList(_setting.Recipes, _selected.Source as MdkSetting.RecipeConfig, delta);
                break;
            case ConfigModule.Visions:
                MoveInList(_setting.Visions, _selected.Source as MdkSetting.VisionConfig, delta);
                break;
            default:
                throw new InvalidOperationException("当前模块不支持排序。");
        }

        var key = _selected.Key;
        SelectModule(_module, key);
    }

    public void ApplyDraft()
    {
        if (IsBrowsingDbTable)
        {
            ApplyDbRow();
            return;
        }

        if (_selected is null || Draft.IsReadOnly)
        {
            throw new InvalidOperationException("没有可应用的属性。");
        }

        switch (_module)
        {
            case ConfigModule.Machine:
                ApplyMachine();
                break;
            case ConfigModule.Drivers when _selected.Source is MdkSetting.DriverConfig d:
                ApplyDriver(d);
                break;
            case ConfigModule.Devices or ConfigModule.Axis or ConfigModule.Platform
                when _selected.Source is MdkSetting.DeviceConfig dev:
                ApplyDevice(dev);
                break;
            case ConfigModule.Tasks when _selected.Source is MdkSetting.TaskConfig t:
                ApplyTask(t);
                break;
            case ConfigModule.Recipes when _selected.Source is MdkSetting.RecipeConfig r:
                ApplyRecipe(r);
                break;
            case ConfigModule.Visions when _selected.Source is MdkSetting.VisionConfig v:
                ApplyVision(v);
                break;
            case ConfigModule.Vars:
                ApplyVar(_selected.Key);
                break;
            case ConfigModule.SysConfig:
                ApplySysConfig(_selected.Key);
                break;
            case ConfigModule.Gpios when _selected.Source is GpioEditTarget gpio:
                ApplyGpio(gpio);
                break;
            case ConfigModule.Vios when _selected.Source is VioEditTarget vio:
                ApplyVio(vio);
                break;
            default:
                throw new InvalidOperationException("当前项不可编辑。");
        }

        var key = _module switch
        {
            ConfigModule.Machine => "machine",
            ConfigModule.SysConfig =>
                _selected?.Key
                ?? Draft.CollectStringParameters().GetValueOrDefault("key")
                ?? Draft.FieldId,
            ConfigModule.Vars => Draft.FieldId,
            ConfigModule.Tasks => Draft.FieldName,
            ConfigModule.Gpios when _selected.Source is GpioEditTarget g =>
                $"{g.Device.Id}:{(Draft.FieldType ?? g.Direction).Trim().ToLowerInvariant()}:{Draft.FieldId.Trim()}",
            ConfigModule.Vios when _selected.Source is VioEditTarget v =>
                $"{v.Device.Id}:{Draft.FieldId.Trim()}",
            _ => Draft.FieldId,
        };
        Draft.ClearDirty();
        SelectModule(_module, keepSelectionKey: key);
        StatusLine = $"已应用属性 · {key}";
    }

    // ── private ──────────────────────────────────────────────────────────

    private void RebuildItems()
    {
        Items.Clear();
        foreach (var item in BuildItemsFor(_module))
        {
            Items.Add(item);
        }

        RefreshListFilter();
    }

    private void RefreshListFilter()
    {
        ItemsView.Refresh();
        OnPropertyChanged(nameof(ListFilterHint));
    }

    private int VisibleItemCount()
    {
        var n = 0;
        foreach (var _ in ItemsView)
        {
            n++;
        }

        return n;
    }

    private bool MatchesListFilter(object obj)
    {
        if (obj is not ComponentItem item)
        {
            return false;
        }

        var q = _listFilter.Trim();
        if (q.Length == 0)
        {
            return true;
        }

        if (ContainsIgnoreCase(item.Key, q)
            || ContainsIgnoreCase(item.Title, q)
            || ContainsIgnoreCase(item.Subtitle, q)
            || ContainsIgnoreCase(item.Col1, q)
            || ContainsIgnoreCase(item.Col2, q)
            || ContainsIgnoreCase(item.Col3, q)
            || ContainsIgnoreCase(item.Col4, q)
            || ContainsIgnoreCase(item.Col5, q)
            || ContainsIgnoreCase(item.Col6, q)
            || ContainsIgnoreCase(item.Col7, q))
        {
            return true;
        }

        return item.Source switch
        {
            GpioEditTarget g =>
                ContainsIgnoreCase(g.Alias, q)
                || ContainsIgnoreCase(g.DriverId, q)
                || ContainsIgnoreCase(g.Port, q)
                || ContainsIgnoreCase(g.Label, q)
                || ContainsIgnoreCase(g.Direction, q)
                || ContainsIgnoreCase(g.Device.Id, q)
                || ContainsIgnoreCase(g.Device.Name, q),
            VioEditTarget v =>
                ContainsIgnoreCase(v.Alias, q)
                || ContainsIgnoreCase(v.Label, q)
                || ContainsIgnoreCase(v.Device.Id, q)
                || ContainsIgnoreCase(v.Device.DriverId, q),
            _ => false,
        };
    }

    private static bool ContainsIgnoreCase(string? haystack, string needle) =>
        !string.IsNullOrEmpty(haystack)
        && haystack.Contains(needle, StringComparison.OrdinalIgnoreCase);

    private List<ComponentItem> BuildItemsFor(ConfigModule module)
    {
        var items = module switch
        {
            ConfigModule.Machine =>
            [
                new ComponentItem
                {
                    Module = ConfigModule.Machine,
                    Source = _setting,
                    Key = "machine",
                    Title = string.IsNullOrWhiteSpace(_setting.ProjectName) ? "Machine" : _setting.ProjectName,
                    Subtitle = "machine",
                    Col2 = "machine",
                    Col3 = "machine",
                    Col4 = string.IsNullOrWhiteSpace(_setting.ProjectName) ? "整机" : _setting.ProjectName,
                    Col5 = "是",
                    Enabled = true,
                    HasEnabled = true,
                },
            ],
            ConfigModule.Drivers => _setting.Drivers.Select(d => new ComponentItem
            {
                Module = module,
                Source = d,
                Key = d.Id,
                Title = string.IsNullOrWhiteSpace(d.Name) ? d.Id : $"{d.Id} · {d.Name}",
                Subtitle = d.Type,
                Col2 = d.Id,
                Col3 = d.Type,
                Col4 = string.IsNullOrWhiteSpace(d.Name) ? d.Id : d.Name,
                Col5 = d.Enabled ? "是" : "否",
                Enabled = d.Enabled,
                HasEnabled = true,
            }).ToList(),
            ConfigModule.Devices => _setting.Devices
                .Where(IsDevicesModuleEntry)
                .Select(d => ToDeviceItem(d, ConfigModule.Devices)).ToList(),
            ConfigModule.Axis => _setting.Axes.Select(d => ToDeviceItem(d, ConfigModule.Axis)).ToList(),
            ConfigModule.Platform => _setting.Platforms.Select(d => ToDeviceItem(d, ConfigModule.Platform)).ToList(),
            ConfigModule.Gpios => BuildGpioItems(),
            ConfigModule.Vios => BuildVioItems(),
            ConfigModule.Tasks => _setting.Tasks.Select(t =>
            {
                var note = t.Parameters.GetValueOrDefault("note", "");
                var desc = string.IsNullOrWhiteSpace(note)
                    ? (string.IsNullOrWhiteSpace(t.DriverId) ? $"interval={t.IntervalMs}" : t.DriverId)
                    : note;
                return new ComponentItem
                {
                    Module = module,
                    Source = t,
                    Key = t.Name,
                    Title = t.Name,
                    Subtitle = t.Type,
                    Col2 = t.Name,
                    Col3 = t.Type,
                    Col4 = desc,
                    Col5 = "",
                };
            }).ToList(),
            ConfigModule.Vars => _setting.Vars.Select(kv => new ComponentItem
            {
                Module = module,
                Source = kv,
                Key = kv.Key,
                Title = kv.Key,
                Subtitle = kv.Value?.ToString() ?? "",
                Col2 = kv.Key,
                Col3 = "var",
                Col4 = kv.Value?.ToString() ?? "",
                Col5 = "",
            }).ToList(),
            ConfigModule.Recipes => _setting.Recipes.Select(r => new ComponentItem
            {
                Module = module,
                Source = r,
                Key = r.Id,
                Title = string.IsNullOrWhiteSpace(r.Name) ? r.Id : $"{r.Id} · {r.Name}",
                Subtitle = r.Description ?? "",
                Col2 = r.Id,
                Col3 = "recipe",
                Col4 = string.IsNullOrWhiteSpace(r.Name) ? (r.Description ?? "") : r.Name,
                Col5 = "",
            }).ToList(),
            ConfigModule.Visions => _setting.Visions.Select(v => new ComponentItem
            {
                Module = module,
                Source = v,
                Key = v.Id,
                Title = string.IsNullOrWhiteSpace(v.Name) ? v.Id : $"{v.Id} · {v.Name}",
                Subtitle = v.Description ?? "",
                Col2 = v.Id,
                Col3 = "vision",
                Col4 = string.IsNullOrWhiteSpace(v.Name) ? (v.Description ?? "") : v.Name,
                Col5 = string.IsNullOrWhiteSpace(v.CameraDeviceId) ? "" : v.CameraDeviceId,
            }).ToList(),
            ConfigModule.SysConfig => BuildSysItems(),
            ConfigModule.Database => BuildDbItems(),
            _ => [],
        };

        return AssignRowNumbers(items);
    }

    private static List<ComponentItem> AssignRowNumbers(List<ComponentItem> items)
    {
        for (var i = 0; i < items.Count; i++)
        {
            items[i].Col1 = (i + 1).ToString();
        }

        return items;
    }

    private static ComponentItem ToDeviceItem(MdkSetting.DeviceConfig d, ConfigModule module)
    {
        var desc = string.IsNullOrWhiteSpace(d.Name) ? d.Id : d.Name;
        var typeLabel = module switch
        {
            ConfigModule.Axis =>
                $"{AxisDeviceParameterSet.GetKindToken(d.Parameters, d.Type)}:{AxisDeviceParameterSet.ParseAxisIndex(d.Parameters)}/{AxisDeviceParameterSet.GetModel(d.Parameters)}",
            ConfigModule.Platform =>
                $"{d.Type}/{d.Parameters.GetValueOrDefault("kind", d.Type)}",
            _ => d.Type,
        };
        return new ComponentItem
        {
            Module = module,
            Source = d,
            Key = d.Id,
            Title = string.IsNullOrWhiteSpace(d.Name) ? d.Id : $"{d.Id} · {d.Name}",
            Subtitle = typeLabel,
            Col2 = d.Id,
            Col3 = typeLabel,
            Col4 = desc,
            Col5 = d.Enabled ? "是" : "否",
            Enabled = d.Enabled,
            HasEnabled = true,
        };
    }

    private List<ComponentItem> BuildGpioItems()
    {
        var list = new List<ComponentItem>();
        foreach (var device in _setting.Devices)
        {
            var type = (device.Type ?? "").Trim().ToLowerInvariant();
            if (type != "gpio")
            {
                continue;
            }

            foreach (var b in GpioDeviceParameterSet.ParseBindings(device.Parameters, device.DriverId))
            {
                var direction = b.IsOutput ? "out" : "in";
                var key = $"{device.Id}:{direction}:{b.Alias}";
                var label = string.IsNullOrWhiteSpace(b.Label)
                    ? GpioDeviceParameterSet.ReadLabel(device.Parameters, b.Alias)
                    : b.Label;
                // Name / tree title: point alias (not driverId.*). Driver is Col6.
                var name = b.Alias;
                list.Add(new ComponentItem
                {
                    Module = ConfigModule.Gpios,
                    Source = new GpioEditTarget(device, direction, b.Alias, b.DriverId, b.Address, label),
                    Key = key,
                    Title = string.IsNullOrWhiteSpace(label) ? name : $"{name} · {label}",
                    Subtitle = string.IsNullOrWhiteSpace(b.DriverId) ? b.Address : $"{b.DriverId}:{b.Address}",
                    Col2 = name,
                    Col3 = direction,
                    Col4 = string.IsNullOrWhiteSpace(label) ? "" : label,
                    Col5 = device.Enabled ? "是" : "否",
                    Col6 = FormatDriverDisplayName(b.DriverId),
                    Col7 = b.Address,
                    Enabled = device.Enabled,
                    HasEnabled = true,
                });
            }
        }

        return list;
    }

    /// <summary>Table/combo caption: <c>id · name</c> when driver has a display name; otherwise id.</summary>
    private string FormatDriverDisplayName(string? driverId)
    {
        var id = (driverId ?? "").Trim();
        if (string.IsNullOrWhiteSpace(id))
        {
            return "";
        }

        var driver = _setting.Drivers.FirstOrDefault(d =>
            string.Equals(d.Id, id, StringComparison.OrdinalIgnoreCase));
        if (driver is null || string.IsNullOrWhiteSpace(driver.Name)
            || string.Equals(driver.Name, id, StringComparison.OrdinalIgnoreCase))
        {
            return id;
        }

        return $"{id} · {driver.Name.Trim()}";
    }

    /// <summary>Accepts raw id or <c>id · name</c> from the driver combo / table.</summary>
    private static string NormalizeDriverId(string? raw)
    {
        var s = (raw ?? "").Trim();
        if (string.IsNullOrWhiteSpace(s))
        {
            return "";
        }

        var sep = s.IndexOf(" · ", StringComparison.Ordinal);
        return sep > 0 ? s[..sep].Trim() : s;
    }

    private List<ComponentItem> BuildVioItems()
    {
        var list = new List<ComponentItem>();
        foreach (var device in _setting.Devices)
        {
            if (!string.Equals(device.Type, "vio", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            foreach (var b in VioDeviceParameterSet.ParseVirtualBindings(device.Parameters))
            {
                var raw = b.IsBidirectional
                    ? device.Parameters.GetValueOrDefault(b.Alias, "virtual")
                    : device.Parameters.GetValueOrDefault(
                        $"{(b.IsOutput ? "out" : "in")}.{b.Alias}", "virtual");
                var label = GpioDeviceParameterSet.ReadLabel(device.Parameters, b.Alias, raw);
                list.Add(new ComponentItem
                {
                    Module = ConfigModule.Vios,
                    Source = new VioEditTarget(device, b.Alias, label),
                    Key = $"{device.Id}:{b.Alias}",
                    Title = string.IsNullOrWhiteSpace(label) ? b.Alias : $"{b.Alias} · {label}",
                    Subtitle = device.Id,
                    Col2 = b.Alias,
                    Col3 = "vio",
                    Col4 = string.IsNullOrWhiteSpace(label) ? "" : label,
                    Col5 = device.Enabled ? "是" : "否",
                    Col6 = device.Id,
                    Col7 = FormatDriverDisplayName(device.DriverId),
                    Enabled = device.Enabled,
                    HasEnabled = true,
                });
            }
        }

        return list.OrderBy(i => i.Col6, StringComparer.OrdinalIgnoreCase)
            .ThenBy(i => i.Col2, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string ReadPointLabel(IReadOnlyDictionary<string, string> parameters, string alias)
    {
        foreach (var prefix in new[] { "in.", "out." })
        {
            if (parameters.TryGetValue(prefix + alias, out var raw))
            {
                var label = GpioDeviceParameterSet.ReadLabel(parameters, alias, raw);
                if (!string.IsNullOrWhiteSpace(label))
                {
                    return label;
                }
            }
        }

        return GpioDeviceParameterSet.ReadLabel(parameters, alias);
    }

    private List<ComponentItem> BuildSysItems()
    {
        return BuildSysConfigEntries().Select(r => new ComponentItem
        {
            Module = ConfigModule.SysConfig,
            Source = r,
            Key = r.Key,
            Title = r.Key,
            Subtitle = string.IsNullOrWhiteSpace(r.Remark) ? r.Value : $"{r.Remark} · {r.Value}",
            Col2 = r.Key,
            Col3 = r.Value,
            Col4 = r.Group,
            Col5 = r.Remark,
            Col6 = r.CreateTime,
            Col7 = r.UpdateTime,
        }).ToList();
    }

    private List<SysConfigEntry> BuildSysConfigEntries()
    {
        if (_documentKind == ConfigDocumentKind.Database && !string.IsNullOrWhiteSpace(_dbPath))
        {
            try
            {
                using var store = new MdkConfigStore(_dbPath);
                var snap = store.QueryTable("sysconfigs");
                if (snap.Rows.Count > 0)
                {
                    return snap.Rows.Select(row => new SysConfigEntry
                    {
                        Key = SysCell(row, "key"),
                        Value = SysCell(row, "value"),
                        Group = SysCell(row, "group"),
                        Remark = SysCell(row, "remark"),
                        CreateTime = SysCell(row, "createtime"),
                        UpdateTime = SysCell(row, "updatetime"),
                    }).Where(e => !string.IsNullOrWhiteSpace(e.Key)).ToList();
                }
            }
            catch
            {
                // fall through to setting-derived rows
            }
        }

        return
        [
            SysEntry("projectName", _setting.ProjectName, "general", "工程名称"),
            SysEntry("cycleMs", _setting.CycleMs.ToString(), "general", "主循环周期(ms)"),
            SysEntry("monitoringPrefix", _setting.MonitoringPrefix ?? "", "general", "监控 API 前缀"),
            SysEntry("startPage", _setting.StartPage ?? "", "general", "启动页面"),
            SysEntry("databasePath", _setting.DatabasePath ?? "", "general", "数据库路径"),
            SysEntry("activeRecipeId", _setting.ActiveRecipeId ?? "", "recipe", "当前配方 Id"),
            SysEntry("recipeVarKeys", JsonSerializer.Serialize(_setting.RecipeVarKeys, JsonOptions), "recipe", "配方变量键列表"),
            SysEntry("activeVisionId", _setting.ActiveVisionId ?? "", "vision", "当前视觉流程 Id"),
        ];
    }

    private static SysConfigEntry SysEntry(string key, string value, string group, string remark) => new()
    {
        Key = key,
        Value = value,
        Group = group,
        Remark = remark,
    };

    private static string SysCell(IReadOnlyDictionary<string, string> row, string name) =>
        row.TryGetValue(name, out var v) ? v ?? "" : "";

    private sealed class SysConfigEntry
    {
        public string Key { get; set; } = "";
        public string Value { get; set; } = "";
        public string Group { get; set; } = "general";
        public string Remark { get; set; } = "";
        public string CreateTime { get; set; } = "";
        public string UpdateTime { get; set; } = "";

        public Dictionary<string, string> ToParameterBook() => new(StringComparer.OrdinalIgnoreCase)
        {
            ["key"] = Key,
            ["value"] = Value,
            ["group"] = Group,
            ["remark"] = Remark,
            ["createtime"] = CreateTime,
            ["updatetime"] = UpdateTime,
        };
    }

    private List<ComponentItem> BuildDbItems()
    {
        EnsureDbCounts();
        var counts = _dbCounts;
        return MdkConfigStore.EditableTableNames.Select(table =>
        {
            long count = 0;
            if (counts is not null)
            {
                count = table switch
                {
                    "drivers" => counts.Drivers,
                    "devices" => counts.Devices,
                    "gpios" => counts.Gpios,
                    "axis" => counts.Axis,
                    "platform" => counts.Platform,
                    "positions" => counts.Positions,
                    "sysconfigs" => counts.SysConfigs,
                    "recipes" => counts.Recipes,
                    "logs" => counts.Logs,
                    "langs" => counts.Langs,
                    _ => 0,
                };
            }

            return new ComponentItem
            {
                Module = ConfigModule.Database,
                Source = table,
                Key = table,
                Title = table,
                Col2 = table,
                Col3 = "table",
                Col4 = counts is null ? "?" : $"{count} 行",
                Col5 = MdkConfigStore.GetPrimaryKeyColumn(table) ?? "",
            };
        }).ToList();
    }

    private void LoadModuleQuickAddDraft()
    {
        var types = ConfigTypeCatalog.TypesForModule(_module);
        if (CanEditList && types.Count > 0)
        {
            Draft.Clear($"模块 {ModuleTitle} · 点击类型快速新建组件");
            Draft.SetQuickAddTypes(types);
            return;
        }

        if (CanEditList)
        {
            Draft.Clear($"模块 {ModuleTitle} · 使用「新建组件」添加，或选中列表项编辑");
            return;
        }

        Draft.Clear($"模块 {ModuleTitle} · 选择列表中的组件以编辑");
    }

    private void LoadDraft(ComponentItem? item)
    {
        if (item is null)
        {
            LoadModuleQuickAddDraft();
            return;
        }

        using (Draft.SuppressDirtyScope())
        {
            Draft.IsReadOnly = false;
            Draft.ShowQuickAddTypes = false;
            Draft.ShowComposeAxes = false;
            Draft.ShowPickRecipeVars = false;
            Draft.ShowEditVisionPipeline = false;
            Draft.QuickAddTypes.Clear();
            Draft.ResetFieldLabels();
            Draft.Headline = $"{ModuleDisplayName(item.Module)} / {item.Title}";
            Draft.SetTypeOptions(ConfigTypeCatalog.TypesForModule(item.Module));
            Draft.SetDriverOptions(_setting.Drivers
                .Where(d => !string.IsNullOrWhiteSpace(d.Id))
                .Select(d => FormatDriverDisplayName(d.Id)));

            switch (item.Module)
            {
                case ConfigModule.Machine:
                    LoadMachineDraft();
                    break;
                case ConfigModule.Drivers when item.Source is MdkSetting.DriverConfig d:
                    SetDraftVisibility(id: true, name: true, type: true, enabled: true, parameters: true);
                    Draft.FieldId = d.Id;
                    Draft.FieldName = string.IsNullOrWhiteSpace(d.Name) ? d.Id : d.Name;
                    Draft.FieldType = d.Type;
                    Draft.FieldEnabled = d.Enabled;
                    Draft.LoadStringParameters(FillMissingTypeParameters(ConfigModule.Drivers, d.Type, null, d.Parameters));
                    RefreshParamKeySuggestions(ConfigModule.Drivers, d.Type, null);
                    break;
                case ConfigModule.Devices or ConfigModule.Axis or ConfigModule.Platform
                    when item.Source is MdkSetting.DeviceConfig d:
                    var showDriver = item.Module != ConfigModule.Platform;
                    SetDraftVisibility(
                        id: true,
                        name: true,
                        type: true,
                        driverId: showDriver,
                        enabled: true,
                        parameters: true);
                    Draft.FieldId = d.Id;
                    Draft.FieldName = d.Name;
                    Draft.FieldType = d.Type;
                    Draft.FieldDriverId = FormatDriverDisplayName(d.DriverId);
                    Draft.FieldEnabled = d.Enabled;
                    var loadedParams = FillMissingTypeParameters(item.Module, d.Type, d.DriverId, d.Parameters);
                    if (item.Module == ConfigModule.Platform)
                    {
                        loadedParams = PlatformDeviceParameterSet.NormalizeParameters(d.Type, loadedParams);
                    }

                    Draft.LoadStringParameters(loadedParams);
                    RefreshParamKeySuggestions(item.Module, d.Type, d.DriverId);
                    RefreshParamValueSuggestions(item.Module);
                    Draft.ShowComposeAxes = item.Module == ConfigModule.Platform;
                    break;
                case ConfigModule.Tasks when item.Source is MdkSetting.TaskConfig t:
                    SetDraftVisibility(name: true, type: true, driverId: true, interval: true, parameters: true);
                    Draft.ShowId = false;
                    Draft.FieldName = t.Name;
                    Draft.FieldType = t.Type;
                    Draft.FieldDriverId = FormatDriverDisplayName(t.DriverId);
                    Draft.FieldInterval = t.IntervalMs.ToString();
                    Draft.LoadStringParameters(FillMissingTypeParameters(ConfigModule.Tasks, t.Type, t.DriverId, t.Parameters));
                    RefreshParamKeySuggestions(ConfigModule.Tasks, t.Type, t.DriverId);
                    break;
                case ConfigModule.Recipes when item.Source is MdkSetting.RecipeConfig r:
                    SetDraftVisibility(id: true, name: true, description: true, parameters: true);
                    Draft.ShowType = false;
                    Draft.ShowEnabled = false;
                    Draft.ShowPickRecipeVars = true;
                    Draft.FieldId = r.Id;
                    Draft.FieldName = r.Name;
                    Draft.FieldDescription = r.Description ?? "";
                    Draft.LoadObjectParameters(r.Vars);
                    RefreshRecipeParamSuggestions();
                    break;
                case ConfigModule.Visions when item.Source is MdkSetting.VisionConfig v:
                    SetDraftVisibility(id: true, name: true, description: true, parameters: true);
                    Draft.ShowType = false;
                    Draft.ShowEnabled = false;
                    Draft.ShowEditVisionPipeline = true;
                    Draft.FieldId = v.Id;
                    Draft.FieldName = v.Name;
                    Draft.FieldDescription = v.Description ?? "";
                    Draft.LoadStringParameters(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["cameraDeviceId"] = v.CameraDeviceId ?? "",
                        ["pipeline"] = $"(nodes={v.Pipeline?.Nodes.Count ?? 0})",
                    });
                    Draft.SetParamKeySuggestions(["cameraDeviceId"]);
                    break;
                case ConfigModule.Vars:
                    SetDraftVisibility(id: true, value: true);
                    Draft.ShowType = Draft.ShowEnabled = Draft.ShowParameters = false;
                    Draft.FieldId = item.Key;
                    Draft.FieldValue = item.Col2;
                    Draft.ParamKeySuggestions.Clear();
                    break;
                case ConfigModule.SysConfig:
                    SetDraftVisibility(parameters: true);
                    Draft.ShowId = Draft.ShowValue = Draft.ShowType = Draft.ShowEnabled = false;
                    {
                        var entry = item.Source as SysConfigEntry
                            ?? new SysConfigEntry
                            {
                                Key = item.Key,
                                Value = item.Col3,
                                Group = item.Col4,
                                Remark = item.Col5,
                                CreateTime = item.Col6,
                                UpdateTime = item.Col7,
                            };
                        LoadSysConfigParameterBook(entry);
                        Draft.SetParamKeySuggestions(
                        [
                            "key", "value", "group", "remark", "createtime", "updatetime",
                        ]);
                    }
                    break;
                case ConfigModule.Gpios when item.Source is GpioEditTarget g:
                    // Name(Alias), Desc, Type/Direction, DriverId, Port, Enabled
                    SetDraftVisibility(
                        id: true,
                        name: true,
                        type: true,
                        driverId: true,
                        value: true,
                        enabled: true);
                    Draft.ShowParameters = false;
                    Draft.ApplyGpioFieldLabels();
                    Draft.SetTypeOptions(ConfigTypeCatalog.GpioDirections);
                    Draft.Headline = $"{ModuleDisplayName(item.Module)} / {g.Device.Id} / {item.Title}";
                    Draft.FieldId = g.Alias;
                    Draft.FieldName = string.IsNullOrWhiteSpace(g.Label)
                        ? GpioDeviceParameterSet.ReadLabel(
                            g.Device.Parameters,
                            g.Alias,
                            g.Device.Parameters.GetValueOrDefault($"{g.Direction}.{g.Alias}"))
                        : g.Label;
                    Draft.FieldType = g.Direction;
                    Draft.FieldDriverId = FormatDriverDisplayName(
                        string.IsNullOrWhiteSpace(g.DriverId)
                            ? (g.Device.DriverId ?? "")
                            : g.DriverId);
                    Draft.FieldValue = g.Port;
                    Draft.FieldEnabled = g.Device.Enabled;
                    Draft.FieldDescription = "";
                    Draft.LoadStringParameters(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
                    Draft.ParamKeySuggestions.Clear();
                    break;
                case ConfigModule.Vios when item.Source is VioEditTarget v:
                    // Name(Alias), Desc, Type=vio, DriverId, DeviceId in Value, Enabled
                    SetDraftVisibility(
                        id: true,
                        name: true,
                        type: true,
                        driverId: true,
                        value: true,
                        enabled: true);
                    Draft.ShowParameters = false;
                    Draft.ApplyVioFieldLabels();
                    Draft.SetTypeOptions(["vio"]);
                    Draft.Headline = $"{ModuleDisplayName(item.Module)} / {v.Device.Id} / {item.Title}";
                    Draft.FieldId = v.Alias;
                    Draft.FieldName = v.Label;
                    Draft.FieldType = "vio";
                    Draft.FieldDriverId = FormatDriverDisplayName(v.Device.DriverId ?? "");
                    Draft.FieldValue = v.Device.Id;
                    Draft.FieldEnabled = v.Device.Enabled;
                    Draft.FieldDescription = "";
                    Draft.LoadStringParameters(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
                    Draft.ParamKeySuggestions.Clear();
                    break;
                case ConfigModule.Database:
                    // Selecting a table opens row browser (handled in SelectItem).
                    Draft.Clear($"Database · 选择左侧表节点浏览/编辑行");
                    Draft.IsReadOnly = true;
                    break;
            }
        }

        Draft.ClearDirty();
    }

    private void RefreshParamKeySuggestions(ConfigModule module, string? type, string? driverId)
    {
        IEnumerable<string> keys = ConfigTypeCatalog.DefaultParameters(module, type, driverId).Keys;
        if (module == ConfigModule.Platform)
        {
            // Only template keys (axis.* / note[/kind]); drop obsolete axisIndex/model from suggestions.
            keys = keys.Concat(Draft.ParameterRows.Select(r => r.Key)
                .Where(k => IsPlatformEditableParamKey(k)));
        }
        else
        {
            keys = keys.Concat(Draft.ParameterRows.Select(r => r.Key));
        }

        Draft.SetParamKeySuggestions(keys.Where(k => !string.IsNullOrWhiteSpace(k)));
    }

    private static bool IsPlatformEditableParamKey(string? key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return false;
        }

        var k = key.Trim();
        if (string.Equals(k, "note", StringComparison.OrdinalIgnoreCase)
            || string.Equals(k, "kind", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return k.StartsWith("axis.", StringComparison.OrdinalIgnoreCase)
               && !k.StartsWith("axisIndex.", StringComparison.OrdinalIgnoreCase)
               && !k.StartsWith("axisNo.", StringComparison.OrdinalIgnoreCase);
    }

    private void RefreshParamValueSuggestions(ConfigModule module)
    {
        // Default pool; BeginningEdit refines by the active Key.
        if (module == ConfigModule.Platform)
        {
            Draft.SetParamValueSuggestions(
                _setting.Axes.Select(a => a.Id).Where(id => !string.IsNullOrWhiteSpace(id)));
            return;
        }

        if (module is ConfigModule.Devices or ConfigModule.Axis or ConfigModule.Tasks or ConfigModule.Drivers)
        {
            Draft.SetParamValueSuggestions(
                _setting.Drivers.Select(d => d.Id).Where(id => !string.IsNullOrWhiteSpace(id)));
            return;
        }

        if (module == ConfigModule.Recipes)
        {
            Draft.SetParamValueSuggestions(
                _setting.Vars.Values
                    .Select(FormatRecipeVarValue)
                    .Where(v => !string.IsNullOrWhiteSpace(v)));
            return;
        }

        Draft.ParamValueSuggestions.Clear();
    }

    /// <summary>Refine Value ComboBox items for the parameter key currently being edited.</summary>
    public void RefreshParamValueSuggestionsForKey(string? key)
    {
        var k = (key ?? "").Trim();
        if (_module == ConfigModule.Platform)
        {
            if (k.StartsWith("axis.", StringComparison.OrdinalIgnoreCase)
                && !k.StartsWith("axisIndex.", StringComparison.OrdinalIgnoreCase))
            {
                Draft.SetParamValueSuggestions(
                    _setting.Axes.Select(a => a.Id).Where(id => !string.IsNullOrWhiteSpace(id)));
                return;
            }

            if (string.Equals(k, "kind", StringComparison.OrdinalIgnoreCase))
            {
                Draft.SetParamValueSuggestions(
                    ConfigTypeCatalog.PlatformTypes.Where(t =>
                        !string.Equals(t, "platform", StringComparison.OrdinalIgnoreCase)));
                return;
            }

            Draft.ParamValueSuggestions.Clear();
            return;
        }

        if (_module is ConfigModule.Drivers or ConfigModule.Devices or ConfigModule.Axis or ConfigModule.Tasks)
        {
            // Driver-related values only when the key looks like a driver reference.
            if (k.Contains("driver", StringComparison.OrdinalIgnoreCase)
                || string.Equals(k, "driverId", StringComparison.OrdinalIgnoreCase)
                || k.StartsWith("axis.", StringComparison.OrdinalIgnoreCase))
            {
                Draft.SetParamValueSuggestions(
                    _setting.Drivers.Select(d => d.Id).Where(id => !string.IsNullOrWhiteSpace(id)));
                return;
            }

            Draft.ParamValueSuggestions.Clear();
            return;
        }

        if (_module == ConfigModule.Recipes)
        {
            RefreshRecipeValueSuggestionsForKey(k);
            return;
        }

        if (_module == ConfigModule.SysConfig)
        {
            RefreshSysConfigValueSuggestionsForKey(k);
            return;
        }

        Draft.ParamValueSuggestions.Clear();
    }

    /// <summary>Key suggestions for recipe vars: all Vars ∪ all SysConfig ∪ other recipes.</summary>
    public void RefreshRecipeParamSuggestions()
    {
        Draft.SetParamKeySuggestions(EnumerateRecipeKeyCandidates().Select(c => c.Key));
        RefreshParamValueSuggestions(ConfigModule.Recipes);
    }

    /// <summary>Candidates for the recipe vars picker (all Vars + all SysConfig entries + existing recipe keys).</summary>
    public IReadOnlyList<RecipeVarCandidate> GetRecipeVarCandidates() =>
        EnumerateRecipeKeyCandidates()
            .OrderBy(c => c.Key, StringComparer.OrdinalIgnoreCase)
            .ToList();

    /// <summary>
    /// Merge selected keys into the current recipe draft.
    /// Existing keys keep their draft values; new keys take Vars / SysConfig current values when available.
    /// </summary>
    public void ApplyRecipeVarSelection(IEnumerable<string> keys)
    {
        if (_module != ConfigModule.Recipes || Draft.IsReadOnly || !Draft.ShowParameters)
        {
            throw new InvalidOperationException("仅 Recipe 组件支持从 Vars / SysConfig 选择键。");
        }

        var selected = keys
            .Where(k => !string.IsNullOrWhiteSpace(k))
            .Select(k => k.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (selected.Count == 0)
        {
            throw new InvalidOperationException("请至少选择一个变量键。");
        }

        var sysValues = BuildSysConfigEntries()
            .Where(e => !string.IsNullOrWhiteSpace(e.Key))
            .GroupBy(e => e.Key.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().Value ?? "", StringComparer.OrdinalIgnoreCase);

        var book = Draft.CollectObjectParameters();
        foreach (var key in selected)
        {
            if (book.ContainsKey(key))
            {
                continue;
            }

            book[key] = ResolveRecipeFillValue(key, sysValues);
        }

        Draft.LoadObjectParameters(book);
        Draft.MarkDirty();
        RefreshRecipeParamSuggestions();
        StatusLine = $"已加入 {selected.Count} 个配方变量键";
    }

    private IEnumerable<RecipeVarCandidate> EnumerateRecipeKeyCandidates()
    {
        var sources = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        void AddSource(string key, string source, string? valuePreview = null)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                return;
            }

            var k = key.Trim();
            if (!sources.TryGetValue(k, out var set))
            {
                set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                sources[k] = set;
            }

            set.Add(source);
            if (valuePreview is not null
                && !string.IsNullOrWhiteSpace(valuePreview)
                && !values.ContainsKey(k))
            {
                values[k] = valuePreview;
            }
        }

        // All Vars entries.
        foreach (var kv in _setting.Vars)
        {
            AddSource(kv.Key, "vars", FormatRecipeVarValue(kv.Value));
        }

        // All SysConfig parameters (not only recipeVarKeys).
        foreach (var entry in BuildSysConfigEntries())
        {
            var tag = string.IsNullOrWhiteSpace(entry.Group)
                ? "sysconfig"
                : $"sysconfig.{entry.Group.Trim()}";
            AddSource(entry.Key, tag, entry.Value);
        }

        // Named keys listed in recipeVarKeys (may not yet exist in Vars).
        foreach (var key in _setting.RecipeVarKeys)
        {
            AddSource(key, "sysconfig.recipeVarKeys");
        }

        // Keys already used by other recipes.
        foreach (var recipe in _setting.Recipes)
        {
            foreach (var kv in recipe.Vars)
            {
                AddSource(kv.Key, $"recipe:{recipe.Id}", FormatRecipeVarValue(kv.Value));
            }
        }

        foreach (var (key, tags) in sources)
        {
            var ordered = tags
                .OrderBy(t => t.Equals("vars", StringComparison.OrdinalIgnoreCase) ? 0
                    : t.StartsWith("sysconfig", StringComparison.OrdinalIgnoreCase) ? 1 : 2)
                .ThenBy(t => t, StringComparer.OrdinalIgnoreCase);
            yield return new RecipeVarCandidate
            {
                Key = key,
                Source = string.Join(" · ", ordered),
                ValuePreview = values.TryGetValue(key, out var preview) ? preview : "",
            };
        }
    }

    private object? ResolveRecipeFillValue(string key, IReadOnlyDictionary<string, string> sysValues)
    {
        if (_setting.Vars.TryGetValue(key, out var fromVars))
        {
            return fromVars;
        }

        if (sysValues.TryGetValue(key, out var fromSys))
        {
            return fromSys;
        }

        return "";
    }

    private void RefreshRecipeValueSuggestionsForKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            Draft.ParamValueSuggestions.Clear();
            return;
        }

        var suggestions = new List<string>();
        if (_setting.Vars.TryGetValue(key, out var current))
        {
            var text = FormatRecipeVarValue(current);
            if (!string.IsNullOrWhiteSpace(text))
            {
                suggestions.Add(text);
            }
        }

        foreach (var entry in BuildSysConfigEntries())
        {
            if (string.Equals(entry.Key, key, StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(entry.Value))
            {
                suggestions.Add(entry.Value);
            }
        }

        foreach (var recipe in _setting.Recipes)
        {
            if (recipe.Vars.TryGetValue(key, out var fromRecipe))
            {
                var text = FormatRecipeVarValue(fromRecipe);
                if (!string.IsNullOrWhiteSpace(text))
                {
                    suggestions.Add(text);
                }
            }
        }

        Draft.SetParamValueSuggestions(suggestions);
    }

    private void RefreshSysConfigValueSuggestionsForKey(string key)
    {
        var entryKey = _selected?.Key ?? "";
        if (string.IsNullOrWhiteSpace(entryKey))
        {
            entryKey = Draft.ParameterRows
                .FirstOrDefault(r => string.Equals(r.Key, "key", StringComparison.OrdinalIgnoreCase))
                ?.Value?.Trim() ?? "";
        }

        if (string.Equals(key, "value", StringComparison.OrdinalIgnoreCase)
            && string.Equals(entryKey, "activeRecipeId", StringComparison.OrdinalIgnoreCase))
        {
            Draft.SetParamValueSuggestions(
                _setting.Recipes.Select(r => r.Id).Where(id => !string.IsNullOrWhiteSpace(id)));
            return;
        }

        if (string.Equals(key, "value", StringComparison.OrdinalIgnoreCase)
            && string.Equals(entryKey, "recipeVarKeys", StringComparison.OrdinalIgnoreCase))
        {
            Draft.SetParamValueSuggestions(_setting.Vars.Keys.Where(k => !string.IsNullOrWhiteSpace(k)));
            return;
        }

        Draft.ParamValueSuggestions.Clear();
    }

    private static string FormatRecipeVarValue(object? value) => value switch
    {
        null => "",
        JsonElement je => je.ValueKind == JsonValueKind.String
            ? je.GetString() ?? ""
            : je.ToString(),
        _ => Convert.ToString(value) ?? "",
    };

    /// <summary>
    /// Apply Axis device selections into draft parameters (<c>axis.X</c> = Axis id only).
    /// </summary>
    public void ApplyPlatformAxisComposition(IReadOnlyDictionary<string, string> letterToAxisId)
    {
        if (_module != ConfigModule.Platform || Draft.IsReadOnly || !Draft.ShowParameters)
        {
            throw new InvalidOperationException("仅 Platform 组件支持组合轴。");
        }

        if (letterToAxisId.Count == 0)
        {
            throw new InvalidOperationException("请至少选择一个 Axis。");
        }

        var type = (Draft.FieldType ?? "").Trim();
        var book = PlatformDeviceParameterSet.NormalizeParameters(type, Draft.CollectStringParameters());
        foreach (var (letter, axisId) in letterToAxisId)
        {
            if (string.IsNullOrWhiteSpace(letter) || string.IsNullOrWhiteSpace(axisId))
            {
                continue;
            }

            var axis = _setting.Axes.FirstOrDefault(a =>
                string.Equals(a.Id, axisId.Trim(), StringComparison.OrdinalIgnoreCase));
            if (axis is null)
            {
                throw new InvalidOperationException($"找不到 Axis 组件「{axisId}」。请先在 Axis 模块中创建。");
            }

            var L = letter.Trim().ToUpperInvariant();
            book[$"axis.{L}"] = axis.Id;
        }

        Draft.LoadStringParameters(book);
        RefreshParamKeySuggestions(ConfigModule.Platform, Draft.FieldType, Draft.FieldDriverId);
        RefreshParamValueSuggestions(ConfigModule.Platform);
        Draft.MarkDirty();
        StatusLine = $"已组合 {letterToAxisId.Count} 个轴到平台参数";
    }

    /// <summary>Resolve current platform kind letters for the compose-axes dialog.</summary>
    public (string KindToken, IReadOnlyList<string> Letters) GetPlatformComposeSlots()
    {
        var type = (Draft.FieldType ?? "").Trim().ToLowerInvariant();
        MPlatformKind? fromAlias = PlatformDeviceParameterSet.TryKindFromDeviceType(type, out var k)
            ? k
            : null;
        var book = Draft.CollectStringParameters();
        var kind = PlatformDeviceParameterSet.ParseKindOrDefault(book, fromAlias);
        return (kind.ToConfigToken(), kind.AxisLetters());
    }

    public IReadOnlyDictionary<string, string> GetPlatformCurrentAxisBindings()
    {
        var book = Draft.CollectStringParameters();
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var letter in new[] { "X", "Y", "Z", "U", "V", "W" })
        {
            var binding = PlatformDeviceParameterSet.TryGetAxisBinding(book, letter);
            if (!string.IsNullOrWhiteSpace(binding))
            {
                result[letter] = binding;
            }
        }

        return result;
    }

    private void SetDraftVisibility(
        bool id = false,
        bool name = false,
        bool type = false,
        bool driverId = false,
        bool interval = false,
        bool description = false,
        bool value = false,
        bool enabled = false,
        bool parameters = false)
    {
        Draft.ShowId = id;
        Draft.ShowName = name;
        Draft.ShowType = type;
        Draft.ShowDriverId = driverId;
        Draft.ShowInterval = interval;
        Draft.ShowDescription = description;
        Draft.ShowValue = value;
        Draft.ShowEnabled = enabled;
        Draft.ShowParameters = parameters;
    }

    private void LoadMachineDraft()
    {
        SetDraftVisibility(id: false, name: true, type: false, enabled: false, parameters: true);
        Draft.FieldId = "machine";
        Draft.FieldName = string.IsNullOrWhiteSpace(_setting.ProjectName) ? "Machine" : _setting.ProjectName;
        Draft.FieldType = "machine";
        Draft.FieldEnabled = true;
        Draft.LoadOrderedStringParameters(
        [
            ("projectName", _setting.ProjectName ?? ""),
            ("cycleMs", _setting.CycleMs.ToString()),
            ("monitoringPrefix", _setting.MonitoringPrefix ?? ""),
            ("startPage", _setting.StartPage ?? ""),
            ("databasePath", _setting.DatabasePath ?? ""),
            ("activeRecipeId", _setting.ActiveRecipeId ?? ""),
            ("recipeVarKeys", JsonSerializer.Serialize(_setting.RecipeVarKeys ?? [], JsonOptions)),
            ("activeVisionId", _setting.ActiveVisionId ?? ""),
        ]);
        Draft.SetParamKeySuggestions(
        [
            "projectName", "cycleMs", "monitoringPrefix", "startPage",
            "databasePath", "activeRecipeId", "recipeVarKeys", "activeVisionId",
        ]);
    }

    private void ApplyMachine()
    {
        var book = Draft.CollectStringParameters();
        var projectName = book.GetValueOrDefault("projectName", Draft.FieldName)?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(projectName))
        {
            projectName = string.IsNullOrWhiteSpace(Draft.FieldName) ? "MDKOSS" : Draft.FieldName.Trim();
        }

        _setting.ProjectName = projectName;
        OnPropertyChanged(nameof(ProjectName));

        var cycleRaw = book.GetValueOrDefault("cycleMs", _setting.CycleMs.ToString()) ?? "20";
        if (!int.TryParse(cycleRaw, out var cycle) || cycle <= 0)
        {
            throw new InvalidOperationException("cycleMs 必须为正整数。");
        }

        _setting.CycleMs = cycle;

        var monitoring = book.GetValueOrDefault("monitoringPrefix", "") ?? "";
        _setting.MonitoringPrefix = string.IsNullOrWhiteSpace(monitoring) ? null : monitoring.Trim();

        var startPage = book.GetValueOrDefault("startPage", "") ?? "";
        _setting.StartPage = string.IsNullOrWhiteSpace(startPage) ? null : startPage.Trim().TrimStart('/');

        var databasePath = book.GetValueOrDefault("databasePath", "") ?? "";
        _setting.DatabasePath = string.IsNullOrWhiteSpace(databasePath) ? null : databasePath.Trim();

        var activeRecipe = book.GetValueOrDefault("activeRecipeId", "") ?? "";
        _setting.ActiveRecipeId = string.IsNullOrWhiteSpace(activeRecipe) ? null : activeRecipe.Trim();

        var activeVision = book.GetValueOrDefault("activeVisionId", "") ?? "";
        _setting.ActiveVisionId = string.IsNullOrWhiteSpace(activeVision) ? null : activeVision.Trim();

        var recipeKeysRaw = book.GetValueOrDefault("recipeVarKeys", "[]") ?? "[]";
        try
        {
            _setting.RecipeVarKeys = JsonSerializer.Deserialize<List<string>>(recipeKeysRaw, JsonOptions) ?? [];
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"recipeVarKeys JSON 无效: {ex.Message}");
        }

        // Keep Desc field in sync with projectName for list display.
        if (_selected is not null)
        {
            _selected.Title = projectName;
            _selected.Col4 = projectName;
        }
    }

    private void ApplyDriver(MdkSetting.DriverConfig d)
    {
        var newId = RequireId(Draft.FieldId, "驱动 Id");
        if (!string.Equals(newId, d.Id, StringComparison.OrdinalIgnoreCase)
            && _setting.Drivers.Any(x => string.Equals(x.Id, newId, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException($"驱动 Id 已存在: {newId}");
        }

        d.Id = newId;
        d.Name = string.IsNullOrWhiteSpace(Draft.FieldName) ? newId : Draft.FieldName.Trim();
        d.Type = string.IsNullOrWhiteSpace(Draft.FieldType) ? "sim" : Draft.FieldType.Trim();
        d.Enabled = Draft.FieldEnabled;
        d.Parameters = Draft.CollectStringParameters();
    }

    private void ApplyDevice(MdkSetting.DeviceConfig d)
    {
        var newId = RequireId(Draft.FieldId, "设备 Id");
        if (!string.Equals(newId, d.Id, StringComparison.OrdinalIgnoreCase)
            && DeviceIdExists(newId))
        {
            throw new InvalidOperationException($"设备 Id 已存在: {newId}");
        }

        var type = string.IsNullOrWhiteSpace(Draft.FieldType)
            ? ConfigTypeCatalog.DefaultType(_module)
            : Draft.FieldType.Trim();
        EnsureDeviceTypeForModule(_module, type);

        d.Id = newId;
        d.Name = Draft.FieldName.Trim();
        d.Type = type;
        d.DriverId = _module == ConfigModule.Platform ? "" : NormalizeDriverId(Draft.FieldDriverId);
        d.Enabled = Draft.FieldEnabled;
        var parameters = Draft.CollectStringParameters();
        if (_module == ConfigModule.Platform)
        {
            parameters = PlatformDeviceParameterSet.NormalizeParameters(type, parameters);
        }
        else if (_module == ConfigModule.Axis)
        {
            AxisDeviceParameterSet.SyncKindParameter(parameters, type);
        }

        d.Parameters = parameters;
    }

    private void ApplyTask(MdkSetting.TaskConfig t)
    {
        var newName = RequireId(Draft.FieldName, "任务 Name");
        if (!string.Equals(newName, t.Name, StringComparison.OrdinalIgnoreCase)
            && _setting.Tasks.Any(x => string.Equals(x.Name, newName, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException($"任务 Name 已存在: {newName}");
        }

        if (!int.TryParse(Draft.FieldInterval, out var interval) || interval <= 0)
        {
            throw new InvalidOperationException("IntervalMs 必须为正整数。");
        }

        t.Name = newName;
        t.Type = string.IsNullOrWhiteSpace(Draft.FieldType) ? "pollDriver" : Draft.FieldType.Trim();
        t.DriverId = NormalizeDriverId(Draft.FieldDriverId);
        t.IntervalMs = interval;
        t.Parameters = Draft.CollectStringParameters();
    }

    private void ApplyRecipe(MdkSetting.RecipeConfig r)
    {
        var newId = RequireId(Draft.FieldId, "配方 Id");
        if (!string.Equals(newId, r.Id, StringComparison.OrdinalIgnoreCase)
            && _setting.Recipes.Any(x => string.Equals(x.Id, newId, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException($"配方 Id 已存在: {newId}");
        }

        r.Id = newId;
        r.Name = string.IsNullOrWhiteSpace(Draft.FieldName) ? newId : Draft.FieldName.Trim();
        r.Description = string.IsNullOrWhiteSpace(Draft.FieldDescription) ? null : Draft.FieldDescription.Trim();
        r.Vars = Draft.CollectObjectParameters();
    }

    private void ApplyVision(MdkSetting.VisionConfig v)
    {
        var newId = RequireId(Draft.FieldId, "视觉流程 Id");
        if (!string.Equals(newId, v.Id, StringComparison.OrdinalIgnoreCase)
            && _setting.Visions.Any(x => string.Equals(x.Id, newId, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException($"视觉流程 Id 已存在: {newId}");
        }

        var ps = Draft.CollectStringParameters();
        v.Id = newId;
        v.Name = string.IsNullOrWhiteSpace(Draft.FieldName) ? newId : Draft.FieldName.Trim();
        v.Description = string.IsNullOrWhiteSpace(Draft.FieldDescription) ? null : Draft.FieldDescription.Trim();
        if (ps.TryGetValue("cameraDeviceId", out var cam))
        {
            v.CameraDeviceId = cam?.Trim() ?? "";
        }

        // pipeline is edited via VisionEditorWindow; ignore placeholder display rows.
    }

    private void ApplyVar(string oldKey)
    {
        var newKey = RequireId(Draft.FieldId, "变量 Key");
        object? value = Draft.FieldValue;
        if (bool.TryParse(Draft.FieldValue, out var b))
        {
            value = b;
        }
        else if (long.TryParse(Draft.FieldValue, out var l))
        {
            value = l;
        }
        else if (double.TryParse(Draft.FieldValue, out var dbl))
        {
            value = dbl;
        }

        if (!string.Equals(oldKey, newKey, StringComparison.OrdinalIgnoreCase))
        {
            if (_setting.Vars.ContainsKey(newKey))
            {
                throw new InvalidOperationException($"变量 Key 已存在: {newKey}");
            }

            _setting.Vars.Remove(oldKey);
        }

        _setting.Vars[newKey] = value;
    }

    private void LoadSysConfigParameterBook(SysConfigEntry entry)
    {
        Draft.LoadOrderedStringParameters(
        [
            ("key", entry.Key),
            ("value", entry.Value),
            ("group", entry.Group),
            ("remark", entry.Remark),
            ("createtime", entry.CreateTime),
            ("updatetime", entry.UpdateTime),
        ]);
    }

    private void ApplySysConfig(string key)
    {
        var book = Draft.CollectStringParameters();
        var selectedKey = string.IsNullOrWhiteSpace(key) ? "" : key.Trim();
        var bookKey = book.GetValueOrDefault("key", selectedKey)?.Trim() ?? selectedKey;
        if (!string.IsNullOrWhiteSpace(bookKey)
            && !string.Equals(bookKey, selectedKey, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("系统配置 key 不可更改，请直接编辑现有项。");
        }

        var value = book.GetValueOrDefault("value", Draft.FieldValue) ?? "";
        var group = book.GetValueOrDefault("group", "general") ?? "general";
        var remark = book.GetValueOrDefault("remark", "") ?? "";
        var createTime = book.GetValueOrDefault("createtime", "") ?? "";
        var updateTime = book.GetValueOrDefault("updatetime", "") ?? "";

        switch (selectedKey)
        {
            case "projectName":
                _setting.ProjectName = value;
                OnPropertyChanged(nameof(ProjectName));
                break;
            case "cycleMs":
                if (!int.TryParse(value, out var cycle) || cycle <= 0)
                {
                    throw new InvalidOperationException("cycleMs 必须为正整数。");
                }

                _setting.CycleMs = cycle;
                break;
            case "monitoringPrefix":
                _setting.MonitoringPrefix = string.IsNullOrWhiteSpace(value) ? null : value;
                break;
            case "startPage":
                _setting.StartPage = string.IsNullOrWhiteSpace(value) ? null : value.Trim().TrimStart('/');
                break;
            case "databasePath":
                _setting.DatabasePath = string.IsNullOrWhiteSpace(value) ? null : value;
                break;
            case "activeRecipeId":
                _setting.ActiveRecipeId = string.IsNullOrWhiteSpace(value) ? null : value;
                break;
            case "activeVisionId":
                _setting.ActiveVisionId = string.IsNullOrWhiteSpace(value) ? null : value;
                break;
            case "recipeVarKeys":
                _setting.RecipeVarKeys = JsonSerializer.Deserialize<List<string>>(value, JsonOptions) ?? [];
                break;
            case "vars":
            case "tasks":
                // Persisted only in sysconfigs table / export blob; keep DB row editable.
                break;
            default:
                throw new InvalidOperationException($"未知系统配置键: {selectedKey}");
        }

        if (_documentKind == ConfigDocumentKind.Database && !string.IsNullOrWhiteSpace(_dbPath))
        {
            EnsureDbPathAvailable();
            using var store = new MdkConfigStore(_dbPath);
            if (string.IsNullOrWhiteSpace(createTime))
            {
                createTime = DateTime.UtcNow.ToString("O");
            }

            store.UpsertTableRow("sysconfigs", new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["key"] = selectedKey,
                ["value"] = value,
                ["group"] = string.IsNullOrWhiteSpace(group) ? "general" : group,
                ["remark"] = remark,
                ["createtime"] = createTime,
                ["updatetime"] = string.IsNullOrWhiteSpace(updateTime)
                    ? DateTime.UtcNow.ToString("O")
                    : updateTime,
            });
        }
    }

    private void ApplyGpio(GpioEditTarget target)
    {
        var alias = RequireId(Draft.FieldId, "Name");
        var direction = (Draft.FieldType ?? "in").Trim().ToLowerInvariant();
        if (direction is not ("in" or "out"))
        {
            throw new InvalidOperationException("Type 须为 in 或 out。");
        }

        var oldParamKey = $"{target.Direction}.{target.Alias}";
        var newParamKey = $"{direction}.{alias}";
        target.Device.Parameters.Remove(oldParamKey);
        target.Device.Parameters.Remove($"desc.{target.Alias}");
        target.Device.Parameters.Remove($"desc.{alias}");
        target.Device.Enabled = Draft.FieldEnabled;

        var portRaw = (Draft.FieldValue ?? "").Trim();
        var label = (Draft.FieldName ?? "").Trim();

        var driverId = NormalizeDriverId(Draft.FieldDriverId);
        if (string.IsNullOrWhiteSpace(driverId))
        {
            driverId = (target.Device.DriverId ?? "").Trim();
        }

        // Port may still accept legacy "driverId:address", unified "driverId|address", or "address|label" paste.
        if (GpioDeviceParameterSet.TryParsePointValue(
                portRaw.IndexOfAny([GpioDeviceParameterSet.LabelSeparator, '｜']) >= 0
                    ? portRaw
                    : $"{portRaw}{GpioDeviceParameterSet.LabelSeparator}{label}",
                driverId,
                out var parsedDriver,
                out var address,
                out var parsedLabel)
            || GpioDeviceParameterSet.TryParsePointValue(portRaw, driverId, out parsedDriver, out address, out parsedLabel))
        {
            if (!string.IsNullOrWhiteSpace(parsedDriver))
            {
                driverId = NormalizeDriverId(parsedDriver);
            }
        }
        else
        {
            if (string.IsNullOrWhiteSpace(portRaw))
            {
                throw new InvalidOperationException("Port 不能为空（端口号/地址，如 0）。");
            }

            address = portRaw;
            parsedLabel = label;
        }

        if (string.IsNullOrWhiteSpace(driverId))
        {
            throw new InvalidOperationException("DriverId 不能为空。");
        }

        if (!string.IsNullOrWhiteSpace(label))
        {
            parsedLabel = label;
        }

        target.Device.Parameters[newParamKey] = GpioDeviceParameterSet.FormatPointValue(
            driverId,
            address,
            parsedLabel,
            target.Device.DriverId);
    }

    private void ApplyVio(VioEditTarget target)
    {
        var alias = RequireId(Draft.FieldId, "Name");
        if (string.IsNullOrWhiteSpace(alias))
        {
            throw new InvalidOperationException("VIO Name 不能为空（如 vio.b1）。");
        }

        var oldKey = target.Alias;
        // Clear legacy in./out. forms of the same alias as well.
        target.Device.Parameters.Remove(oldKey);
        target.Device.Parameters.Remove($"in.{oldKey}");
        target.Device.Parameters.Remove($"out.{oldKey}");
        target.Device.Parameters.Remove($"desc.{oldKey}");
        target.Device.Parameters.Remove($"desc.{alias}");
        if (!string.Equals(oldKey, alias, StringComparison.OrdinalIgnoreCase))
        {
            target.Device.Parameters.Remove(alias);
            target.Device.Parameters.Remove($"in.{alias}");
            target.Device.Parameters.Remove($"out.{alias}");
        }

        target.Device.Enabled = Draft.FieldEnabled;
        var driverId = NormalizeDriverId(Draft.FieldDriverId);
        if (!string.IsNullOrWhiteSpace(driverId))
        {
            target.Device.DriverId = driverId;
        }

        var deviceId = (Draft.FieldValue ?? "").Trim();
        if (!string.IsNullOrWhiteSpace(deviceId)
            && !string.Equals(deviceId, target.Device.Id, StringComparison.OrdinalIgnoreCase))
        {
            // Moving point to another vio device is not supported in-place; keep on current device.
            throw new InvalidOperationException("DeviceId（Port/Value）不可更改，请在目标 vio 设备参数中编辑。");
        }

        var label = (Draft.FieldName ?? "").Trim();
        target.Device.Parameters[alias] = string.IsNullOrWhiteSpace(label)
            ? "virtual"
            : $"virtual|{label}";
    }

    /// <summary>
    /// Replace draft parameters with the template for the current Type (keeps unrelated keys only when merge=true).
    /// </summary>
    public void ApplyTypeDefaultParameters(bool replaceAll)
    {
        if (Draft.IsReadOnly || !Draft.ShowParameters)
        {
            throw new InvalidOperationException("当前选中项不支持参数模板。");
        }

        var type = Draft.FieldType;
        var driverId = Draft.FieldDriverId;
        var template = ConfigTypeCatalog.DefaultParameters(_module, type, driverId);
        if (template.Count == 0)
        {
            throw new InvalidOperationException($"类型 '{type}' 没有预置参数模板。");
        }

        var existing = Draft.CollectStringParameters();
        var next = replaceAll
            ? new Dictionary<string, string>(template, StringComparer.OrdinalIgnoreCase)
            : DeviceParameterPresets.ApplyTemplate(existing, template, overwriteEmptyOnly: true);
        if (_module == ConfigModule.Platform)
        {
            next = PlatformDeviceParameterSet.NormalizeParameters(type ?? "", next);
        }

        Draft.LoadStringParameters(next);
        RefreshParamKeySuggestions(_module, type, driverId);
        RefreshParamValueSuggestions(_module);
        Draft.MarkDirty();
        StatusLine = replaceAll
            ? $"已按类型 {type} 重置参数模板"
            : $"已补全类型 {type} 缺失参数";
    }

    private static Dictionary<string, string> FillMissingTypeParameters(
        ConfigModule module,
        string? type,
        string? driverId,
        IReadOnlyDictionary<string, string> existing)
    {
        // GPIO / VIO point templates belong in Gpios / Vios modules — do not inject sample IO
        // into an existing device draft when opening Devices.
        if (module == ConfigModule.Devices && IsGpioOrVioDeviceType(type))
        {
            return new Dictionary<string, string>(existing, StringComparer.OrdinalIgnoreCase);
        }

        var template = ConfigTypeCatalog.DefaultParameters(module, type, driverId);
        if (template.Count == 0)
        {
            return new Dictionary<string, string>(existing, StringComparer.OrdinalIgnoreCase);
        }

        return DeviceParameterPresets.ApplyTemplate(
            new Dictionary<string, string>(existing, StringComparer.OrdinalIgnoreCase),
            template,
            overwriteEmptyOnly: true);
    }

    private static bool IsGpioOrVioDeviceType(string? type) =>
        IsGpioDeviceType(type) || IsVioDeviceType(type);

    private static bool IsGpioDeviceType(string? type) =>
        string.Equals((type ?? "").Trim(), "gpio", StringComparison.OrdinalIgnoreCase);

    private static bool IsVioDeviceType(string? type) =>
        string.Equals((type ?? "").Trim(), "vio", StringComparison.OrdinalIgnoreCase);

    private void ValidateBeforeSave()
    {
        var driverIds = _setting.Drivers.Select(d => d.Id).Where(id => !string.IsNullOrWhiteSpace(id)).ToList();
        if (driverIds.Count != driverIds.Distinct(StringComparer.OrdinalIgnoreCase).Count())
        {
            throw new InvalidOperationException("存在重复的 driver id。");
        }

        var deviceIds = _setting.AllDeviceConfigs
            .Select(d => d.Id)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .ToList();
        if (deviceIds.Count != deviceIds.Distinct(StringComparer.OrdinalIgnoreCase).Count())
        {
            throw new InvalidOperationException("存在重复的 device / axis / platform id。");
        }
    }

    private void SetColumnHeaders(ConfigModule module)
    {
        // Unified: Id# / Name(原配置Id) / Type / Desc(描述名) / Enable
        ColHeader1 = "Id";
        ColHeader2 = "Name";
        ColHeader3 = "Type";
        ColHeader4 = "Desc";
        ColHeader5 = "Enable";
        ColHeader6 = "";
        ColHeader7 = "";

        switch (module)
        {
            case ConfigModule.Machine:
                ColHeader5 = "Enable";
                break;
            case ConfigModule.SysConfig:
                // Match parameter book: key / value / group / remark / createtime / updatetime
                ColHeader2 = "key";
                ColHeader3 = "value";
                ColHeader4 = "group";
                ColHeader5 = "remark";
                ColHeader6 = "createtime";
                ColHeader7 = "updatetime";
                break;
            case ConfigModule.Database:
                ColHeader2 = "Table";
                ColHeader4 = "Count";
                ColHeader5 = "PK";
                break;
            case ConfigModule.Gpios:
                ColHeader5 = "Enable";
                ColHeader6 = "驱动";
                ColHeader7 = "Port";
                break;
            case ConfigModule.Vios:
                ColHeader5 = "Enable";
                ColHeader6 = "DeviceId";
                ColHeader7 = "驱动";
                break;
            case ConfigModule.Vars:
            case ConfigModule.Recipes:
            case ConfigModule.Visions:
            case ConfigModule.Tasks:
                ColHeader5 = "";
                break;
        }
    }

    public static string ModuleDisplayName(ConfigModule m) => m switch
    {
        ConfigModule.Machine => "Machine",
        ConfigModule.Drivers => "Drivers",
        ConfigModule.Devices => "Devices",
        ConfigModule.Axis => "Axis",
        ConfigModule.Platform => "Platform",
        ConfigModule.Gpios => "GPIOs",
        ConfigModule.Vios => "VIOs",
        ConfigModule.Tasks => "Tasks",
        ConfigModule.Vars => "Vars",
        ConfigModule.Recipes => "Recipes",
        ConfigModule.Visions => "Visions",
        ConfigModule.SysConfig => "SysConfig",
        ConfigModule.Database => "Database",
        _ => m.ToString(),
    };

    /// <summary>Platform 族不归属 Devices 模块（独立 Platform 树节点）。</summary>
    private static bool IsPlatformModuleEntry(MdkSetting.DeviceConfig d) =>
        PlatformDeviceParameterSet.IsPlatformFamilyType((d.Type ?? "").ToLowerInvariant());

    private static bool IsAxisModuleEntry(MdkSetting.DeviceConfig d) =>
        AxisDeviceParameterSet.IsAxisFamilyType(d.Type);

    private static bool IsDevicesModuleEntry(MdkSetting.DeviceConfig d) =>
        !IsAxisModuleEntry(d) && !IsPlatformModuleEntry(d);

    private List<MdkSetting.DeviceConfig> DeviceBucket(ConfigModule module) => module switch
    {
        ConfigModule.Axis => _setting.Axes,
        ConfigModule.Platform => _setting.Platforms,
        _ => _setting.Devices,
    };

    private IEnumerable<string> AllDeviceIds() =>
        _setting.AllDeviceConfigs.Select(d => d.Id);

    private bool DeviceIdExists(string id) =>
        _setting.AllDeviceConfigs.Any(d => string.Equals(d.Id, id, StringComparison.OrdinalIgnoreCase));

    private static void EnsureDeviceTypeForModule(ConfigModule module, string type)
    {
        var lower = (type ?? "").ToLowerInvariant();
        var isPlatform = PlatformDeviceParameterSet.IsPlatformFamilyType(lower);
        var isAxis = AxisDeviceParameterSet.IsAxisFamilyType(lower);
        if (module == ConfigModule.Devices && (isPlatform || isAxis))
        {
            throw new InvalidOperationException(
                isAxis
                    ? "Axis 不属于 Devices 模块。请在左侧「Axis」下新建或编辑。"
                    : "Platform 不属于 Devices 模块。请在左侧「Platform」下新建或编辑。");
        }

        if (module == ConfigModule.Platform && !isPlatform)
        {
            throw new InvalidOperationException(
                "Platform 模块仅支持 platform / x / xy / xyz / xyzu / xyzuv / xyzuvw。");
        }

        if (module == ConfigModule.Axis && !isAxis)
        {
            throw new InvalidOperationException(
                "Axis 模块仅支持 type=linear / rotary / axis（直线轴 / 旋转轴）。");
        }
    }

    private static string RequireId(string? value, string label)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"{label} 不能为空。");
        }

        return value.Trim();
    }

    private static Dictionary<string, string> ParseStringDict(string json, string label)
    {
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, string>>(json, JsonOptions)
                   ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"{label} JSON 无效: {ex.Message}");
        }
    }

    private static Dictionary<string, object?> ParseObjectDict(string json, string label)
    {
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, object?>>(json, JsonOptions)
                   ?? new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"{label} JSON 无效: {ex.Message}");
        }
    }

    private static string UniqueId(IEnumerable<string> existing, string preferred)
    {
        var set = new HashSet<string>(existing, StringComparer.OrdinalIgnoreCase);
        if (!set.Contains(preferred))
        {
            return preferred;
        }

        for (var i = 2; i < 1000; i++)
        {
            var candidate = $"{preferred}_{i}";
            if (!set.Contains(candidate))
            {
                return candidate;
            }
        }

        return $"{preferred}_{Guid.NewGuid():N}"[..16];
    }

    private static void MoveInList<T>(List<T> list, T? item, int delta) where T : class
    {
        if (item is null)
        {
            return;
        }

        var index = list.IndexOf(item);
        if (index < 0)
        {
            return;
        }

        var target = index + delta;
        if (target < 0 || target >= list.Count)
        {
            return;
        }

        list.RemoveAt(index);
        list.Insert(target, item);
    }

    /// <summary>在完整列表中，仅与满足 <paramref name="include"/> 的相邻项交换位置（跳过被过滤的项）。</summary>
    private static void MoveAmongFiltered<T>(List<T> list, T? item, int delta, Func<T, bool> include)
        where T : class
    {
        if (item is null || !include(item))
        {
            return;
        }

        var index = list.IndexOf(item);
        if (index < 0)
        {
            return;
        }

        var step = delta < 0 ? -1 : 1;
        for (var i = index + step; i >= 0 && i < list.Count; i += step)
        {
            if (!include(list[i]))
            {
                continue;
            }

            list.RemoveAt(index);
            // 移除后：下移时邻居下标 -1，Insert(i) 落在邻居之后；上移时 Insert(i) 落在邻居之前。
            list.Insert(i, item);
            return;
        }
    }

    private static MdkSetting.DriverConfig CloneDriver(MdkSetting.DriverConfig d) => new()
    {
        Id = d.Id,
        Name = d.Name,
        Type = d.Type,
        Enabled = d.Enabled,
        Parameters = new Dictionary<string, string>(d.Parameters, StringComparer.OrdinalIgnoreCase),
    };

    private static MdkSetting.DeviceConfig CloneDevice(MdkSetting.DeviceConfig d) => new()
    {
        Id = d.Id,
        Name = d.Name,
        Type = d.Type,
        DriverId = d.DriverId,
        Enabled = d.Enabled,
        Parameters = new Dictionary<string, string>(d.Parameters, StringComparer.OrdinalIgnoreCase),
    };

    private static MdkSetting.TaskConfig CloneTask(MdkSetting.TaskConfig t) => new()
    {
        Name = t.Name,
        Type = t.Type,
        DriverId = t.DriverId,
        IntervalMs = t.IntervalMs,
        Parameters = new Dictionary<string, string>(t.Parameters, StringComparer.OrdinalIgnoreCase),
    };

    private static MdkSetting.RecipeConfig CloneRecipe(MdkSetting.RecipeConfig r) => new()
    {
        Id = r.Id,
        Name = r.Name,
        Description = r.Description,
        Vars = new Dictionary<string, object?>(r.Vars, StringComparer.OrdinalIgnoreCase),
    };

    private static MdkSetting.VisionConfig CloneVision(MdkSetting.VisionConfig v) => new()
    {
        Id = v.Id,
        Name = v.Name,
        Description = v.Description,
        CameraDeviceId = v.CameraDeviceId,
        Pipeline = CloneVisionPipeline(v.Pipeline),
    };

    private static MDKOSS.Core.Vision.VisionDocument? CloneVisionPipeline(MDKOSS.Core.Vision.VisionDocument? src)
    {
        if (src is null)
        {
            return null;
        }

        return MDKOSS.Core.Vision.VisionDocument.TryParse(src.ToJson(), out var copy, out _)
            ? copy
            : MDKOSS.Core.Vision.VisionDocument.CreateEmpty();
    }

    /// <summary>Status hint after an external editor (Flow / Vision) mutates the setting.</summary>
    public void NotifyExternalEdit(string message) => StatusLine = message;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

internal sealed record GpioEditTarget(
    MdkSetting.DeviceConfig Device,
    string Direction,
    string Alias,
    string DriverId,
    string Port,
    string Label = "");

/// <summary>Projected VIO point row for the VIOs module.</summary>
internal sealed record VioEditTarget(
    MdkSetting.DeviceConfig Device,
    string Alias,
    string Label);

/// <summary>One SQLite row shown in the Database table browser.</summary>
public sealed class DbRowItem : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    public DbRowItem(
        string rowKey,
        IReadOnlyDictionary<string, string> cells,
        IReadOnlyList<string>? columnOrder = null)
    {
        RowKey = rowKey;
        Cells = new Dictionary<string, string>(cells, StringComparer.OrdinalIgnoreCase);
        Columns = columnOrder is { Count: > 0 }
            ? columnOrder.ToList()
            : Cells.Keys.ToList();

        var ordered = Columns
            .Select(c => (Key: c, Value: Cells.TryGetValue(c, out var v) ? v : ""))
            .ToList();
        Col1 = ordered.Count > 0 ? ordered[0].Value : "";
        Col2 = ordered.Count > 1 ? ordered[1].Value : "";
        Col3 = ordered.Count > 2 ? ordered[2].Value : "";
        Col4 = ordered.Count > 3 ? ordered[3].Value : "";
        Preview = string.Join(" | ", ordered.Take(6).Select(kv => $"{kv.Key}={Truncate(kv.Value, 24)}"));
    }

    public string RowKey { get; }
    public Dictionary<string, string> Cells { get; }
    public IReadOnlyList<string> Columns { get; }
    public string Col1 { get; }
    public string Col2 { get; }
    public string Col3 { get; }
    public string Col4 { get; }
    public string Preview { get; }

    /// <summary>Cell value by column name (WPF indexer binding: <c>[{column}]</c>).</summary>
    public string this[string columnName] =>
        Cells.TryGetValue(columnName, out var value) ? value : "";

    public string GetCell(string columnName) => this[columnName];

    private static string Truncate(string? value, int max)
    {
        var v = value ?? "";
        return v.Length <= max ? v : v[..max] + "…";
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
