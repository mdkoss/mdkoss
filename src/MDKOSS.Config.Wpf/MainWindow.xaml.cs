using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using MDKOSS.Core;
using MDKOSS.Core.Data;

namespace MDKOSS.Config.Wpf;

public partial class MainWindow : Window
{
    private readonly ConfigWorkspace _workspace = new();

    public MainWindow(string? settingPath = null)
    {
        InitializeComponent();
        DataContext = _workspace;
        NavTree.SelectedItemChanged += OnNavSelected;
        Loaded += (_, _) =>
        {
            if (!string.IsNullOrWhiteSpace(settingPath) && System.IO.File.Exists(settingPath))
            {
                _workspace.OpenSetting(settingPath!);
            }

            RefreshGrid();
        };
    }

    private void OnNavSelected(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (e.NewValue is TreeViewItem item && item.Tag is string key)
        {
            _workspace.SelectedSection = key;
            RefreshGrid();
        }
    }

    private void RefreshGrid()
    {
        CenterGrid.ItemsSource = _workspace.GetRowsForSelectedSection();
        PropertyBox.Text = _workspace.BuildPropertySummary();
        StatusText.Text = _workspace.StatusLine;
        Title = $"MDKOSS.Config.Wpf — {_workspace.ProjectName}";
    }

    private void OpenSetting_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Filter = "Setting JSON (*.setting.json;*.json)|*.setting.json;*.json|All files|*.*",
            Title = "打开配置 JSON",
        };
        if (dlg.ShowDialog(this) == true)
        {
            try
            {
                _workspace.OpenSetting(dlg.FileName);
                RefreshGrid();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "打开失败", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    private void SaveSetting_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            _workspace.SaveSetting();
            RefreshGrid();
            MessageBox.Show(this, $"已保存:\n{_workspace.SettingPath}", "保存", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "保存失败", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void SaveSettingAs_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new SaveFileDialog
        {
            Filter = "Setting JSON (*.setting.json)|*.setting.json|JSON (*.json)|*.json",
            FileName = System.IO.Path.GetFileName(_workspace.SettingPath) is { Length: > 0 } name ? name : "export.setting.json",
            Title = "另存为配置 JSON",
        };
        if (dlg.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            _workspace.SaveSettingAs(dlg.FileName);
            RefreshGrid();
            MessageBox.Show(this, $"已保存:\n{dlg.FileName}", "另存为", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "另存为失败", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ExportDb_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var result = _workspace.ExportToDatabase();
            RefreshGrid();
            MessageBox.Show(
                this,
                $"已导出到:\n{result.DatabasePath}\n\n{result}",
                "导出到 SQLite",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "导出失败", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ExportDbAs_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new SaveFileDialog
        {
            Filter = "SQLite DB (*.db)|*.db|All files|*.*",
            FileName = "mdk.db",
            Title = "导出配置到 SQLite",
        };
        if (dlg.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            var result = _workspace.ExportToDatabase(dlg.FileName);
            RefreshGrid();
            MessageBox.Show(
                this,
                $"已导出到:\n{result.DatabasePath}\n\n{result}",
                "导出到 SQLite",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "导出失败", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ImportDb_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Filter = "SQLite DB (*.db)|*.db|All files|*.*",
            Title = "从 SQLite 导入配置",
        };
        if (dlg.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            _workspace.ImportFromDatabase(dlg.FileName);
            RefreshGrid();
            MessageBox.Show(this, "已从数据库导入到内存模型。请另存为 JSON 以落盘。", "导入", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "导入失败", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ReloadDbPreview_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            _workspace.RefreshDbPreview();
            RefreshGrid();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "刷新失败", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Exit_Click(object sender, RoutedEventArgs e) => Close();
}

/// <summary>Offline setting editor + SQLite export workspace.</summary>
public sealed class ConfigWorkspace : INotifyPropertyChanged
{
    private static readonly JsonSerializerOptions PrettyJson = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };

    private MdkSetting _setting = new();
    private string _settingPath = string.Empty;
    private string _selectedSection = "Drivers";
    private string _statusLine = "未打开配置";
    private ConfigTableCounts? _dbCounts;
    private ObservableCollection<object> _logs = [];
    private ObservableCollection<object> _langs = [];

    public event PropertyChangedEventHandler? PropertyChanged;

    public string ProjectName => _setting.ProjectName;

    public string SettingPath => _settingPath;

    public string SelectedSection
    {
        get => _selectedSection;
        set
        {
            _selectedSection = value;
            OnPropertyChanged();
        }
    }

    public string StatusLine
    {
        get => _statusLine;
        private set
        {
            _statusLine = value;
            OnPropertyChanged();
        }
    }

    public void OpenSetting(string path)
    {
        _setting = MdkSetting.Load(path);
        _settingPath = System.IO.Path.GetFullPath(path);
        StatusLine = $"已打开 {_settingPath} | Drivers={_setting.Drivers.Count} Devices={_setting.Devices.Count} Recipes={_setting.Recipes.Count}";
        OnPropertyChanged(nameof(ProjectName));
        OnPropertyChanged(nameof(SettingPath));
    }

    public void SaveSetting()
    {
        if (string.IsNullOrWhiteSpace(_settingPath))
        {
            throw new InvalidOperationException("尚未指定配置文件路径，请使用「另存为」。");
        }

        _setting.Save(_settingPath);
        StatusLine = $"已保存 {_settingPath}";
    }

    public void SaveSettingAs(string path)
    {
        _setting.Save(path);
        _settingPath = System.IO.Path.GetFullPath(path);
        StatusLine = $"已另存为 {_settingPath}";
        OnPropertyChanged(nameof(SettingPath));
    }

    public ConfigExportResult ExportToDatabase(string? dbPath = null)
    {
        var path = ResolveDbPath(dbPath);
        using var store = new MdkConfigStore(path);
        var result = store.ExportSetting(_setting, _settingPath);
        _dbCounts = store.CountTables();
        _logs = new ObservableCollection<object>(store.ListLogs(100).Cast<object>());
        _langs = new ObservableCollection<object>(store.ListLangs().Cast<object>());
        StatusLine = $"已导出到 {result.DatabasePath} | {result}";
        return result;
    }

    public void ImportFromDatabase(string dbPath)
    {
        using var store = new MdkConfigStore(dbPath);
        _setting = store.ImportSetting();
        _dbCounts = store.CountTables();
        _logs = new ObservableCollection<object>(store.ListLogs(100).Cast<object>());
        _langs = new ObservableCollection<object>(store.ListLangs().Cast<object>());
        StatusLine = $"已从 {System.IO.Path.GetFullPath(dbPath)} 导入 | Drivers={_setting.Drivers.Count} Devices={_setting.Devices.Count}";
        OnPropertyChanged(nameof(ProjectName));
    }

    public void RefreshDbPreview()
    {
        var path = ResolveDbPath(null);
        if (!System.IO.File.Exists(path))
        {
            StatusLine = $"数据库不存在: {path}";
            return;
        }

        using var store = new MdkConfigStore(path);
        _dbCounts = store.CountTables();
        _logs = new ObservableCollection<object>(store.ListLogs(100).Cast<object>());
        _langs = new ObservableCollection<object>(store.ListLangs().Cast<object>());
        StatusLine = $"DB {_dbCounts.Drivers}d/{_dbCounts.Devices}dev/{_dbCounts.Gpios}gpio/{_dbCounts.Recipes}r — {path}";
    }

    public IEnumerable<object> GetRowsForSelectedSection() => SelectedSection switch
    {
        "Drivers" => _setting.Drivers.Select(d => new
        {
            d.Id,
            d.Type,
            d.Enabled,
            Parameters = JsonSerializer.Serialize(d.Parameters),
        }),
        "Devices" => _setting.Devices.Select(d => new
        {
            d.Id,
            d.Name,
            d.Type,
            d.DriverId,
            d.Enabled,
            Parameters = JsonSerializer.Serialize(d.Parameters),
        }),
        "GPIOs" => BuildGpioRows(),
        "Axis" => _setting.Devices
            .Where(d => string.Equals(d.Type, "axis", StringComparison.OrdinalIgnoreCase))
            .Select(d => new { d.Id, d.Name, d.DriverId, d.Enabled, Parameters = JsonSerializer.Serialize(d.Parameters) }),
        "Platform" => _setting.Devices
            .Where(d => PlatformDeviceParameterSet.IsPlatformFamilyType((d.Type ?? "").ToLowerInvariant()))
            .Select(d => new { d.Id, d.Name, d.Type, d.DriverId, d.Enabled, Kind = d.Parameters.GetValueOrDefault("kind", d.Type) }),
        "Positions" => PreviewPositions(),
        "Recipes" => _setting.Recipes.Select(r => new
        {
            r.Id,
            r.Name,
            r.Description,
            Vars = JsonSerializer.Serialize(r.Vars),
        }),
        "SysConfigs" => BuildSysConfigRows(),
        "Tasks" => _setting.Tasks.Select(t => new
        {
            t.Name,
            t.Type,
            t.DriverId,
            t.IntervalMs,
            Parameters = JsonSerializer.Serialize(t.Parameters),
        }),
        "Vars" => _setting.Vars.Select(kv => new { Key = kv.Key, Value = kv.Value?.ToString() ?? "" }),
        "Logs" => _logs,
        "Langs" => _langs,
        "DbCounts" => _dbCounts is null
            ? Array.Empty<object>()
            : new object[]
            {
                new { Table = "drivers", Count = _dbCounts.Drivers },
                new { Table = "devices", Count = _dbCounts.Devices },
                new { Table = "gpios", Count = _dbCounts.Gpios },
                new { Table = "axis", Count = _dbCounts.Axis },
                new { Table = "platform", Count = _dbCounts.Platform },
                new { Table = "positions", Count = _dbCounts.Positions },
                new { Table = "sysconfigs", Count = _dbCounts.SysConfigs },
                new { Table = "recipes", Count = _dbCounts.Recipes },
                new { Table = "logs", Count = _dbCounts.Logs },
                new { Table = "langs", Count = _dbCounts.Langs },
            },
        _ => Array.Empty<object>(),
    };

    public string BuildPropertySummary()
    {
        return SelectedSection switch
        {
            "Drivers" => $"Drivers: {_setting.Drivers.Count}",
            "Devices" => $"Devices: {_setting.Devices.Count}",
            "GPIOs" => $"GPIO/VIO points: {BuildGpioRows().Count()}",
            "Axis" => $"Axis devices: {_setting.Devices.Count(d => string.Equals(d.Type, "axis", StringComparison.OrdinalIgnoreCase))}",
            "Platform" => $"Platform devices: {_setting.Devices.Count(d => PlatformDeviceParameterSet.IsPlatformFamilyType((d.Type ?? "").ToLowerInvariant()))}",
            "Recipes" => $"Recipes: {_setting.Recipes.Count} | Active: {_setting.ActiveRecipeId ?? "(none)"}",
            "SysConfigs" => $"Project: {_setting.ProjectName}\nCycleMs: {_setting.CycleMs}\nDB: {_setting.DatabasePath ?? MdkSetting.DefaultDatabasePath}\nMonitoring: {_setting.MonitoringPrefix ?? "(default)"}",
            "DbCounts" => _dbCounts is null
                ? "尚未导出/刷新数据库"
                : $"drivers={_dbCounts.Drivers}, devices={_dbCounts.Devices}, gpios={_dbCounts.Gpios}, axis={_dbCounts.Axis}, platform={_dbCounts.Platform}, positions={_dbCounts.Positions}, sysconfigs={_dbCounts.SysConfigs}, recipes={_dbCounts.Recipes}, logs={_dbCounts.Logs}, langs={_dbCounts.Langs}",
            _ => $"Section: {SelectedSection}\nSetting: {_settingPath}",
        };
    }

    private IEnumerable<object> BuildGpioRows()
    {
        foreach (var device in _setting.Devices)
        {
            var type = (device.Type ?? "").Trim().ToLowerInvariant();
            if (type is not ("gpio" or "vio"))
            {
                continue;
            }

            foreach (var b in GpioDeviceParameterSet.ParseBindings(device.Parameters))
            {
                yield return new
                {
                    DeviceId = device.Id,
                    Alias = b.Alias,
                    Direction = b.IsOutput ? "out" : "in",
                    b.DriverId,
                    b.Address,
                };
            }

            if (type != "vio")
            {
                continue;
            }

            foreach (var kv in device.Parameters)
            {
                string? direction = null;
                string? alias = null;
                if (kv.Key.StartsWith("in.", StringComparison.OrdinalIgnoreCase))
                {
                    direction = "in";
                    alias = kv.Key[3..];
                }
                else if (kv.Key.StartsWith("out.", StringComparison.OrdinalIgnoreCase))
                {
                    direction = "out";
                    alias = kv.Key[4..];
                }

                if (direction is null || alias is null)
                {
                    continue;
                }

                if (GpioDeviceParameterSet.TryParsePointRoute(kv.Value, out _, out _))
                {
                    continue;
                }

                yield return new
                {
                    DeviceId = device.Id,
                    Alias = alias,
                    Direction = direction,
                    DriverId = device.DriverId,
                    Address = "virtual",
                };
            }
        }
    }

    private IEnumerable<object> BuildSysConfigRows()
    {
        yield return new { Key = "projectName", Value = _setting.ProjectName, Category = "general" };
        yield return new { Key = "cycleMs", Value = _setting.CycleMs.ToString(), Category = "general" };
        yield return new { Key = "monitoringPrefix", Value = _setting.MonitoringPrefix ?? "", Category = "general" };
        yield return new { Key = "databasePath", Value = _setting.DatabasePath ?? "", Category = "general" };
        yield return new { Key = "activeRecipeId", Value = _setting.ActiveRecipeId ?? "", Category = "recipe" };
        yield return new { Key = "recipeVarKeys", Value = JsonSerializer.Serialize(_setting.RecipeVarKeys), Category = "recipe" };
        yield return new { Key = "vars", Value = JsonSerializer.Serialize(_setting.Vars, PrettyJson), Category = "vars" };
        yield return new { Key = "tasks", Value = $"[{_setting.Tasks.Count} tasks]", Category = "tasks" };
    }

    private static IEnumerable<object> PreviewPositions()
    {
        yield return new { Hint = "点位来自 teach_points；导出时镜像到 positions 表。可先示教再导出。" };
    }

    private string ResolveDbPath(string? overridePath)
    {
        if (!string.IsNullOrWhiteSpace(overridePath))
        {
            return System.IO.Path.GetFullPath(overridePath);
        }

        if (!string.IsNullOrWhiteSpace(_setting.DatabasePath))
        {
            var configured = _setting.DatabasePath!;
            return System.IO.Path.IsPathRooted(configured)
                ? configured
                : System.IO.Path.GetFullPath(System.IO.Path.Combine(AppContext.BaseDirectory, configured));
        }

        return MdkSetting.DefaultDatabasePath;
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
