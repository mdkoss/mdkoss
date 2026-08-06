using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;
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
    Drivers,
    Devices,
    Axis,
    Platform,
    Gpios,
    Tasks,
    Vars,
    Recipes,
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
    private string _headline = "未选择组件";

    public ObservableCollection<KvPairRow> ParameterRows { get; } = [];
    public ObservableCollection<string> TypeOptions { get; } = [];
    public ObservableCollection<string> DriverOptions { get; } = [];

    public string Headline { get => _headline; set { _headline = value; OnPropertyChanged(); } }
    public string FieldId { get => _fieldId; set { _fieldId = value; OnPropertyChanged(); } }
    public string FieldName { get => _fieldName; set { _fieldName = value; OnPropertyChanged(); } }
    public string FieldType { get => _fieldType; set { _fieldType = value; OnPropertyChanged(); } }
    public string FieldDriverId { get => _fieldDriverId; set { _fieldDriverId = value; OnPropertyChanged(); } }
    public string FieldInterval { get => _fieldInterval; set { _fieldInterval = value; OnPropertyChanged(); } }
    public string FieldDescription { get => _fieldDescription; set { _fieldDescription = value; OnPropertyChanged(); } }
    public string FieldValue { get => _fieldValue; set { _fieldValue = value; OnPropertyChanged(); } }
    public string FieldParameters { get => _fieldParameters; set { _fieldParameters = value; OnPropertyChanged(); } }
    public bool FieldEnabled { get => _fieldEnabled; set { _fieldEnabled = value; OnPropertyChanged(); } }

    public bool ShowId { get => _showId; set { _showId = value; OnPropertyChanged(); } }
    public bool ShowName { get => _showName; set { _showName = value; OnPropertyChanged(); } }
    public bool ShowType { get => _showType; set { _showType = value; OnPropertyChanged(); } }
    public bool ShowDriverId { get => _showDriverId; set { _showDriverId = value; OnPropertyChanged(); } }
    public bool ShowInterval { get => _showInterval; set { _showInterval = value; OnPropertyChanged(); } }
    public bool ShowDescription { get => _showDescription; set { _showDescription = value; OnPropertyChanged(); } }
    public bool ShowValue { get => _showValue; set { _showValue = value; OnPropertyChanged(); } }
    public bool ShowEnabled { get => _showEnabled; set { _showEnabled = value; OnPropertyChanged(); } }
    public bool ShowParameters { get => _showParameters; set { _showParameters = value; OnPropertyChanged(); } }
    public bool IsReadOnly { get => _isReadOnly; set { _isReadOnly = value; OnPropertyChanged(); } }
    public bool ParametersAsObject { get => _parametersAsObject; set { _parametersAsObject = value; OnPropertyChanged(); } }

    public void Clear(string message = "未选择组件")
    {
        Headline = message;
        IsReadOnly = true;
        ShowId = ShowName = ShowType = ShowDriverId = ShowInterval = ShowDescription = ShowValue = ShowEnabled = ShowParameters = false;
        FieldId = FieldName = FieldType = FieldDriverId = FieldDescription = FieldValue = string.Empty;
        FieldParameters = "{}";
        FieldInterval = "100";
        FieldEnabled = true;
        ParametersAsObject = false;
        ParameterRows.Clear();
        TypeOptions.Clear();
        DriverOptions.Clear();
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

    public void LoadStringParameters(IReadOnlyDictionary<string, string> dict)
    {
        ParametersAsObject = false;
        KvTableHelper.LoadStringDict(ParameterRows, dict);
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
    }

    public void SyncRowsFromJson()
    {
        KvTableHelper.LoadFromJsonObject(ParameterRows, FieldParameters);
    }

    public Dictionary<string, string> CollectStringParameters() =>
        KvTableHelper.ToStringDict(ParameterRows);

    public Dictionary<string, object?> CollectObjectParameters() =>
        KvTableHelper.ToObjectDict(ParameterRows);

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed class ConfigWorkspace : INotifyPropertyChanged
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };

    private MdkSetting _setting = new();
    private string _jsonPath = string.Empty;
    private string _dbPath = string.Empty;
    private ConfigDocumentKind _documentKind = ConfigDocumentKind.None;
    private ConfigModule _module = ConfigModule.Drivers;
    private ComponentItem? _selected;
    private string _statusLine = "未打开配置";
    private string _moduleTitle = "Drivers";
    private string _colHeader1 = "Id";
    private string _colHeader2 = "Type";
    private string _colHeader3 = "Enabled";
    private string _colHeader4 = "";
    private ConfigTableCounts? _dbCounts;
    private List<ConfigLogRecord> _logs = [];
    private string? _selectedDbTable;
    private bool _isBrowsingDbTable;
    private string? _dbPrimaryKey;
    private DbRowItem? _selectedDbRow;

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<ComponentItem> Items { get; } = [];
    public ObservableCollection<DbRowItem> DbRows { get; } = [];
    /// <summary>Column names of the currently browsed SQLite table (middle pane headers).</summary>
    public ObservableCollection<string> DbColumns { get; } = [];
    public PropertyDraft Draft { get; } = new();

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

    public bool CanEditList => _module is not (ConfigModule.Database or ConfigModule.Gpios);

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
        _module = module;
        OnPropertyChanged(nameof(CurrentModule));
        OnPropertyChanged(nameof(CanEditList));
        SetColumnHeaders(module);
        ModuleTitle = ModuleDisplayName(module);

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

        SelectItem(next);
        StatusLine = $"{ModuleTitle} · {Items.Count} 项 · [{DocumentKindLabel}] {DocumentPath}";
    }

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

    public CreateComponentRequest PrepareCreateRequest()
    {
        if (!CanEditList)
        {
            throw new InvalidOperationException("当前模块不支持新建（GPIO 请在 Devices 中编辑 parameters；Database 只读）。");
        }

        if (_module == ConfigModule.SysConfig)
        {
            throw new InvalidOperationException("系统配置键为固定集合，请直接编辑现有项。");
        }

        var req = new CreateComponentRequest
        {
            TypeOptions = ConfigTypeCatalog.TypesForModule(_module),
            DriverOptions = _setting.Drivers.Select(d => d.Id).Where(id => !string.IsNullOrWhiteSpace(id)).ToList(),
            Type = ConfigTypeCatalog.DefaultType(_module),
            Enabled = true,
            IntervalMs = 100,
        };

        switch (_module)
        {
            case ConfigModule.Drivers:
                req.Id = UniqueId(_setting.Drivers.Select(d => d.Id), "drv-new");
                break;
            case ConfigModule.Devices:
            case ConfigModule.Axis:
            case ConfigModule.Platform:
                req.Id = UniqueId(_setting.Devices.Select(d => d.Id), "dev-new");
                req.Name = req.Id;
                req.DriverId = req.DriverOptions.FirstOrDefault() ?? "";
                break;
            case ConfigModule.Tasks:
                req.Name = UniqueId(_setting.Tasks.Select(t => t.Name), "task-new");
                req.DriverId = req.DriverOptions.FirstOrDefault() ?? "";
                break;
            case ConfigModule.Recipes:
                req.Id = UniqueId(_setting.Recipes.Select(r => r.Id), "recipe-new");
                req.Name = req.Id;
                break;
            case ConfigModule.Vars:
                req.Id = UniqueId(_setting.Vars.Keys, "var.new");
                req.Value = "";
                break;
            default:
                throw new InvalidOperationException("当前模块不支持新建。");
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
            ConfigModule.Axis => _setting.Devices
                .Where(d => string.Equals(d.Type, "axis", StringComparison.OrdinalIgnoreCase)).ToList(),
            ConfigModule.Platform => _setting.Devices
                .Where(IsPlatformModuleEntry).ToList(),
            ConfigModule.Tasks => _setting.Tasks,
            ConfigModule.Recipes => _setting.Recipes,
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
            },
            ConfigModule.Gpios => BuildGpioItems().Select(i => new
            {
                i.Col1,
                Alias = i.Col2,
                Direction = i.Col3,
                Route = i.Col4,
            }).ToList(),
            _ => throw new InvalidOperationException("当前模块不支持导出。"),
        };

        var json = JsonSerializer.Serialize(payload, JsonOptions);
        System.IO.File.WriteAllText(full, json);
        StatusLine = $"已导出模块 {ModuleTitle} → {full}";
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
                // 替换时只动 Devices 条目，保留 Platform 族
                MergeDevices(rows, replace, IsDevicesModuleEntry);
                break;
            }
            case ConfigModule.Axis:
            {
                var rows = JsonSerializer.Deserialize<List<MdkSetting.DeviceConfig>>(json, JsonOptions) ?? [];
                MergeDevices(rows, replace, d => string.Equals(d.Type, "axis", StringComparison.OrdinalIgnoreCase));
                break;
            }
            case ConfigModule.Platform:
            {
                var rows = (JsonSerializer.Deserialize<List<MdkSetting.DeviceConfig>>(json, JsonOptions) ?? [])
                    .Where(IsPlatformModuleEntry)
                    .ToList();
                MergeDevices(rows, replace, IsPlatformModuleEntry);
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
        List<MdkSetting.DeviceConfig> rows,
        bool replace,
        Func<MdkSetting.DeviceConfig, bool>? filter)
    {
        if (replace && filter is null)
        {
            _setting.Devices = rows;
            return;
        }

        if (replace && filter is not null)
        {
            _setting.Devices.RemoveAll(d => filter(d));
        }

        foreach (var row in rows)
        {
            var existing = _setting.Devices.FirstOrDefault(d =>
                string.Equals(d.Id, row.Id, StringComparison.OrdinalIgnoreCase));
            if (existing is null)
            {
                _setting.Devices.Add(row);
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
            Type = string.IsNullOrWhiteSpace(req.Type) ? "sim" : req.Type,
            Enabled = req.Enabled,
            Parameters = new Dictionary<string, string>(req.Parameters, StringComparer.OrdinalIgnoreCase),
        };
        _setting.Drivers.Add(d);
        return new ComponentItem { Key = d.Id, Source = d, Module = ConfigModule.Drivers, Title = d.Id };
    }

    private ComponentItem CommitDevice(CreateComponentRequest req)
    {
        if (_setting.Devices.Any(d => string.Equals(d.Id, req.Id, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException($"设备 Id 已存在: {req.Id}");
        }

        var type = string.IsNullOrWhiteSpace(req.Type) ? ConfigTypeCatalog.DefaultType(_module) : req.Type;
        EnsureDeviceTypeForModule(_module, type);
        var d = new MdkSetting.DeviceConfig
        {
            Id = req.Id,
            Name = string.IsNullOrWhiteSpace(req.Name) ? req.Id : req.Name,
            Type = type,
            DriverId = req.DriverId,
            Enabled = req.Enabled,
            Parameters = new Dictionary<string, string>(req.Parameters, StringComparer.OrdinalIgnoreCase),
        };
        _setting.Devices.Add(d);
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
                ndev.Id = UniqueId(_setting.Devices.Select(x => x.Id), dev.Id + "_copy");
                _setting.Devices.Add(ndev);
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
                _setting.Devices.Remove(dev);
                break;
            case ConfigModule.Tasks when _selected.Source is MdkSetting.TaskConfig t:
                _setting.Tasks.Remove(t);
                break;
            case ConfigModule.Recipes when _selected.Source is MdkSetting.RecipeConfig r:
                _setting.Recipes.Remove(r);
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
            case ConfigModule.Tasks:
                MoveInList(_setting.Tasks, _selected.Source as MdkSetting.TaskConfig, delta);
                break;
            case ConfigModule.Recipes:
                MoveInList(_setting.Recipes, _selected.Source as MdkSetting.RecipeConfig, delta);
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
            case ConfigModule.Vars:
                ApplyVar(_selected.Key);
                break;
            case ConfigModule.SysConfig:
                ApplySysConfig(_selected.Key);
                break;
            case ConfigModule.Gpios when _selected.Source is GpioEditTarget gpio:
                ApplyGpio(gpio);
                break;
            default:
                throw new InvalidOperationException("当前项不可编辑。");
        }

        var key = _module == ConfigModule.Vars || _module == ConfigModule.SysConfig
            ? Draft.FieldId
            : _module == ConfigModule.Tasks
                ? Draft.FieldName
                : Draft.FieldId;
        SelectModule(_module, key);
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
    }

    private List<ComponentItem> BuildItemsFor(ConfigModule module) => module switch
    {
        ConfigModule.Drivers => _setting.Drivers.Select(d => new ComponentItem
        {
            Module = module,
            Source = d,
            Key = d.Id,
            Title = d.Id,
            Subtitle = d.Type,
            Col1 = d.Id,
            Col2 = d.Type,
            Col3 = d.Enabled ? "是" : "否",
            Enabled = d.Enabled,
            HasEnabled = true,
        }).ToList(),
        // Platform 不作为 Devices 子项：仅出现在 Platform 模块树下
        ConfigModule.Devices => _setting.Devices
            .Where(IsDevicesModuleEntry)
            .Select(d => ToDeviceItem(d, ConfigModule.Devices)).ToList(),
        ConfigModule.Axis => _setting.Devices
            .Where(d => string.Equals(d.Type, "axis", StringComparison.OrdinalIgnoreCase))
            .Select(d => ToDeviceItem(d, ConfigModule.Axis)).ToList(),
        ConfigModule.Platform => _setting.Devices
            .Where(IsPlatformModuleEntry)
            .Select(d =>
            {
                var item = ToDeviceItem(d, ConfigModule.Platform);
                item.Col3 = d.Parameters.GetValueOrDefault("kind", d.Type);
                item.Col4 = d.DriverId;
                return item;
            }).ToList(),
        ConfigModule.Gpios => BuildGpioItems(),
        ConfigModule.Tasks => _setting.Tasks.Select(t => new ComponentItem
        {
            Module = module,
            Source = t,
            Key = t.Name,
            Title = t.Name,
            Subtitle = t.Type,
            Col1 = t.Name,
            Col2 = t.Type,
            Col3 = t.DriverId,
            Col4 = t.IntervalMs.ToString(),
        }).ToList(),
        ConfigModule.Vars => _setting.Vars.Select(kv => new ComponentItem
        {
            Module = module,
            Source = kv,
            Key = kv.Key,
            Title = kv.Key,
            Subtitle = kv.Value?.ToString() ?? "",
            Col1 = kv.Key,
            Col2 = kv.Value?.ToString() ?? "",
        }).ToList(),
        ConfigModule.Recipes => _setting.Recipes.Select(r => new ComponentItem
        {
            Module = module,
            Source = r,
            Key = r.Id,
            Title = r.Id,
            Subtitle = r.Name,
            Col1 = r.Id,
            Col2 = r.Name,
            Col3 = r.Description ?? "",
        }).ToList(),
        ConfigModule.SysConfig => BuildSysItems(),
        ConfigModule.Database => BuildDbItems(),
        _ => [],
    };

    private static ComponentItem ToDeviceItem(MdkSetting.DeviceConfig d, ConfigModule module) => new()
    {
        Module = module,
        Source = d,
        Key = d.Id,
        Title = d.Id,
        Subtitle = $"{d.Type} · {d.Name}",
        Col1 = d.Id,
        Col2 = d.Name,
        Col3 = d.Type,
        Col4 = d.DriverId,
        Enabled = d.Enabled,
        HasEnabled = true,
    };

    private List<ComponentItem> BuildGpioItems()
    {
        var list = new List<ComponentItem>();
        foreach (var device in _setting.Devices)
        {
            var type = (device.Type ?? "").Trim().ToLowerInvariant();
            if (type is not ("gpio" or "vio"))
            {
                continue;
            }

            foreach (var b in GpioDeviceParameterSet.ParseBindings(device.Parameters))
            {
                var direction = b.IsOutput ? "out" : "in";
                var key = $"{device.Id}:{direction}:{b.Alias}";
                var route = string.IsNullOrWhiteSpace(b.DriverId) ? b.Address : $"{b.DriverId}:{b.Address}";
                list.Add(new ComponentItem
                {
                    Module = ConfigModule.Gpios,
                    Source = new GpioEditTarget(device, direction, b.Alias, route),
                    Key = key,
                    Title = $"{device.Id}.{direction}.{b.Alias}",
                    Subtitle = route,
                    Col1 = device.Id,
                    Col2 = b.Alias,
                    Col3 = direction,
                    Col4 = route,
                });
            }
        }

        return list;
    }

    private List<ComponentItem> BuildSysItems()
    {
        (string Key, string Value)[] rows =
        [
            ("projectName", _setting.ProjectName),
            ("cycleMs", _setting.CycleMs.ToString()),
            ("monitoringPrefix", _setting.MonitoringPrefix ?? ""),
            ("startPage", _setting.StartPage ?? ""),
            ("databasePath", _setting.DatabasePath ?? ""),
            ("activeRecipeId", _setting.ActiveRecipeId ?? ""),
            ("recipeVarKeys", JsonSerializer.Serialize(_setting.RecipeVarKeys, JsonOptions)),
        ];

        return rows.Select(r => new ComponentItem
        {
            Module = ConfigModule.SysConfig,
            Source = r.Key,
            Key = r.Key,
            Title = r.Key,
            Subtitle = r.Value,
            Col1 = r.Key,
            Col2 = r.Value,
        }).ToList();
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
                Col1 = table,
                Col2 = counts is null ? "?" : count.ToString(),
                Col3 = MdkConfigStore.GetPrimaryKeyColumn(table) ?? "",
            };
        }).ToList();
    }

    private void LoadDraft(ComponentItem? item)
    {
        if (item is null)
        {
            Draft.Clear($"模块 {ModuleTitle} · 选择列表中的组件以编辑");
            return;
        }

        Draft.IsReadOnly = false;
        Draft.Headline = $"{ModuleDisplayName(item.Module)} / {item.Title}";
        Draft.SetTypeOptions(ConfigTypeCatalog.TypesForModule(item.Module));
        Draft.SetDriverOptions(_setting.Drivers.Select(d => d.Id).Where(id => !string.IsNullOrWhiteSpace(id)));

        switch (item.Module)
        {
            case ConfigModule.Drivers when item.Source is MdkSetting.DriverConfig d:
                SetDraftVisibility(id: true, type: true, enabled: true, parameters: true);
                Draft.FieldId = d.Id;
                Draft.FieldType = d.Type;
                Draft.FieldEnabled = d.Enabled;
                Draft.LoadStringParameters(d.Parameters);
                break;
            case ConfigModule.Devices or ConfigModule.Axis or ConfigModule.Platform
                when item.Source is MdkSetting.DeviceConfig d:
                SetDraftVisibility(id: true, name: true, type: true, driverId: true, enabled: true, parameters: true);
                Draft.FieldId = d.Id;
                Draft.FieldName = d.Name;
                Draft.FieldType = d.Type;
                Draft.FieldDriverId = d.DriverId;
                Draft.FieldEnabled = d.Enabled;
                Draft.LoadStringParameters(d.Parameters);
                break;
            case ConfigModule.Tasks when item.Source is MdkSetting.TaskConfig t:
                SetDraftVisibility(name: true, type: true, driverId: true, interval: true, parameters: true);
                Draft.ShowId = false;
                Draft.FieldName = t.Name;
                Draft.FieldType = t.Type;
                Draft.FieldDriverId = t.DriverId;
                Draft.FieldInterval = t.IntervalMs.ToString();
                Draft.LoadStringParameters(t.Parameters);
                break;
            case ConfigModule.Recipes when item.Source is MdkSetting.RecipeConfig r:
                SetDraftVisibility(id: true, name: true, description: true, parameters: true);
                Draft.ShowType = false;
                Draft.ShowEnabled = false;
                Draft.FieldId = r.Id;
                Draft.FieldName = r.Name;
                Draft.FieldDescription = r.Description ?? "";
                Draft.LoadObjectParameters(r.Vars);
                break;
            case ConfigModule.Vars:
                SetDraftVisibility(id: true, value: true);
                Draft.ShowType = Draft.ShowEnabled = Draft.ShowParameters = false;
                Draft.FieldId = item.Key;
                Draft.FieldValue = item.Col2;
                break;
            case ConfigModule.SysConfig:
                SetDraftVisibility(id: true, value: true);
                Draft.ShowType = Draft.ShowEnabled = Draft.ShowParameters = false;
                Draft.FieldId = item.Key;
                Draft.FieldValue = item.Col2;
                Draft.ShowId = true;
                break;
            case ConfigModule.Gpios when item.Source is GpioEditTarget g:
                SetDraftVisibility(id: true, name: true, type: true, value: true);
                Draft.ShowEnabled = Draft.ShowParameters = false;
                Draft.SetTypeOptions(ConfigTypeCatalog.GpioDirections);
                Draft.FieldId = g.Device.Id;
                Draft.FieldName = g.Alias;
                Draft.FieldType = g.Direction;
                Draft.FieldValue = g.Route;
                break;
            case ConfigModule.Database:
                // Selecting a table opens row browser (handled in SelectItem).
                Draft.Clear($"Database · 选择左侧表节点浏览/编辑行");
                Draft.IsReadOnly = true;
                break;
        }
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

    private void ApplyDriver(MdkSetting.DriverConfig d)
    {
        var newId = RequireId(Draft.FieldId, "驱动 Id");
        if (!string.Equals(newId, d.Id, StringComparison.OrdinalIgnoreCase)
            && _setting.Drivers.Any(x => string.Equals(x.Id, newId, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException($"驱动 Id 已存在: {newId}");
        }

        d.Id = newId;
        d.Type = string.IsNullOrWhiteSpace(Draft.FieldType) ? "sim" : Draft.FieldType.Trim();
        d.Enabled = Draft.FieldEnabled;
        d.Parameters = Draft.CollectStringParameters();
    }

    private void ApplyDevice(MdkSetting.DeviceConfig d)
    {
        var newId = RequireId(Draft.FieldId, "设备 Id");
        if (!string.Equals(newId, d.Id, StringComparison.OrdinalIgnoreCase)
            && _setting.Devices.Any(x => string.Equals(x.Id, newId, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException($"设备 Id 已存在: {newId}");
        }

        var type = string.IsNullOrWhiteSpace(Draft.FieldType)
            ? (_module == ConfigModule.Platform ? "xyz" : "gpio")
            : Draft.FieldType.Trim();
        EnsureDeviceTypeForModule(_module, type);

        d.Id = newId;
        d.Name = Draft.FieldName.Trim();
        d.Type = type;
        d.DriverId = Draft.FieldDriverId.Trim();
        d.Enabled = Draft.FieldEnabled;
        d.Parameters = Draft.CollectStringParameters();
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
        t.DriverId = Draft.FieldDriverId.Trim();
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

    private void ApplySysConfig(string key)
    {
        var value = Draft.FieldValue ?? "";
        switch (key)
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
            case "recipeVarKeys":
                _setting.RecipeVarKeys = JsonSerializer.Deserialize<List<string>>(value, JsonOptions) ?? [];
                break;
            default:
                throw new InvalidOperationException($"未知系统配置键: {key}");
        }
    }

    private void ApplyGpio(GpioEditTarget target)
    {
        var alias = RequireId(Draft.FieldName, "Alias");
        var direction = (Draft.FieldType ?? "in").Trim().ToLowerInvariant();
        if (direction is not ("in" or "out"))
        {
            throw new InvalidOperationException("Direction 须为 in 或 out。");
        }

        var oldParamKey = $"{target.Direction}.{target.Alias}";
        var newParamKey = $"{direction}.{alias}";
        target.Device.Parameters.Remove(oldParamKey);
        target.Device.Parameters[newParamKey] = (Draft.FieldValue ?? "").Trim();
    }

    private void ValidateBeforeSave()
    {
        var driverIds = _setting.Drivers.Select(d => d.Id).Where(id => !string.IsNullOrWhiteSpace(id)).ToList();
        if (driverIds.Count != driverIds.Distinct(StringComparer.OrdinalIgnoreCase).Count())
        {
            throw new InvalidOperationException("存在重复的 driver id。");
        }

        var deviceIds = _setting.Devices.Select(d => d.Id).Where(id => !string.IsNullOrWhiteSpace(id)).ToList();
        if (deviceIds.Count != deviceIds.Distinct(StringComparer.OrdinalIgnoreCase).Count())
        {
            throw new InvalidOperationException("存在重复的 device id。");
        }
    }

    private void SetColumnHeaders(ConfigModule module)
    {
        switch (module)
        {
            case ConfigModule.Drivers:
                ColHeader1 = "Id"; ColHeader2 = "Type"; ColHeader3 = "Enabled"; ColHeader4 = "";
                break;
            case ConfigModule.Devices:
                ColHeader1 = "Id"; ColHeader2 = "Name"; ColHeader3 = "Type"; ColHeader4 = "DriverId";
                break;
            case ConfigModule.Axis:
                ColHeader1 = "Id"; ColHeader2 = "Name"; ColHeader3 = "Type"; ColHeader4 = "DriverId";
                break;
            case ConfigModule.Platform:
                ColHeader1 = "Id"; ColHeader2 = "Name"; ColHeader3 = "Kind"; ColHeader4 = "DriverId";
                break;
            case ConfigModule.Gpios:
                ColHeader1 = "Device"; ColHeader2 = "Alias"; ColHeader3 = "Dir"; ColHeader4 = "Route";
                break;
            case ConfigModule.Tasks:
                ColHeader1 = "Name"; ColHeader2 = "Type"; ColHeader3 = "DriverId"; ColHeader4 = "Interval";
                break;
            case ConfigModule.Vars:
                ColHeader1 = "Key"; ColHeader2 = "Value"; ColHeader3 = ""; ColHeader4 = "";
                break;
            case ConfigModule.Recipes:
                ColHeader1 = "Id"; ColHeader2 = "Name"; ColHeader3 = "Description"; ColHeader4 = "";
                break;
            case ConfigModule.SysConfig:
                ColHeader1 = "Key"; ColHeader2 = "Value"; ColHeader3 = ""; ColHeader4 = "";
                break;
            case ConfigModule.Database:
                ColHeader1 = "Table"; ColHeader2 = "Count"; ColHeader3 = "PK"; ColHeader4 = "";
                break;
        }
    }

    public static string ModuleDisplayName(ConfigModule m) => m switch
    {
        ConfigModule.Drivers => "Drivers",
        ConfigModule.Devices => "Devices",
        ConfigModule.Axis => "Axis",
        ConfigModule.Platform => "Platform",
        ConfigModule.Gpios => "GPIOs",
        ConfigModule.Tasks => "Tasks",
        ConfigModule.Vars => "Vars",
        ConfigModule.Recipes => "Recipes",
        ConfigModule.SysConfig => "SysConfig",
        ConfigModule.Database => "Database",
        _ => m.ToString(),
    };

    /// <summary>Platform 族不归属 Devices 模块（独立 Platform 树节点）。</summary>
    private static bool IsPlatformModuleEntry(MdkSetting.DeviceConfig d) =>
        PlatformDeviceParameterSet.IsPlatformFamilyType((d.Type ?? "").ToLowerInvariant());

    private static bool IsDevicesModuleEntry(MdkSetting.DeviceConfig d) => !IsPlatformModuleEntry(d);

    private static void EnsureDeviceTypeForModule(ConfigModule module, string type)
    {
        var lower = (type ?? "").ToLowerInvariant();
        var isPlatform = PlatformDeviceParameterSet.IsPlatformFamilyType(lower);
        if (module == ConfigModule.Devices && isPlatform)
        {
            throw new InvalidOperationException(
                "Platform 不属于 Devices 模块。请在左侧「Platform」下新建或编辑。");
        }

        if (module == ConfigModule.Platform && !isPlatform)
        {
            throw new InvalidOperationException(
                "Platform 模块仅支持 platform / xy / xyz / xyzu / xyzuv / xyzuvw。");
        }

        if (module == ConfigModule.Axis && !string.Equals(lower, "axis", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Axis 模块仅支持 type=axis。");
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

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

internal sealed record GpioEditTarget(
    MdkSetting.DeviceConfig Device,
    string Direction,
    string Alias,
    string Route);

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
