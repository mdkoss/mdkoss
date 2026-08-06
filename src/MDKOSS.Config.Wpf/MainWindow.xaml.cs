using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using Microsoft.Win32;
using MDKOSS.Config.Wpf.Debug;
using MDKOSS.Config.Wpf.Debug.Flow;
using MDKOSS.Core;

namespace MDKOSS.Config.Wpf;

public static class MainWindowCommands
{
    public static readonly RoutedUICommand Open = new("Open", nameof(Open), typeof(MainWindowCommands));
    public static readonly RoutedUICommand Save = new("Save", nameof(Save), typeof(MainWindowCommands));
    public static readonly RoutedUICommand Add = new("Add", nameof(Add), typeof(MainWindowCommands));
    public static readonly RoutedUICommand Duplicate = new("Duplicate", nameof(Duplicate), typeof(MainWindowCommands));
    public static readonly RoutedUICommand Delete = new("Delete", nameof(Delete), typeof(MainWindowCommands));
}

public partial class MainWindow : Window
{
    private readonly ConfigWorkspace _workspace = new();
    private bool _suppressGridSelection;
    private bool _suppressTreeSelection;

    public MainWindow(string? settingPath = null)
    {
        InitializeComponent();
        DataContext = _workspace;

        CommandBindings.Add(new CommandBinding(MainWindowCommands.Open, (_, _) => OpenSetting()));
        CommandBindings.Add(new CommandBinding(MainWindowCommands.Save, (_, _) => SaveSetting()));
        CommandBindings.Add(new CommandBinding(MainWindowCommands.Add, (_, _) => AddComponent()));
        CommandBindings.Add(new CommandBinding(MainWindowCommands.Duplicate, (_, _) => DuplicateComponent()));
        CommandBindings.Add(new CommandBinding(MainWindowCommands.Delete, (_, _) => DeleteComponent()));

        Loaded += (_, _) =>
        {
            if (!string.IsNullOrWhiteSpace(settingPath) && System.IO.File.Exists(settingPath))
            {
                try
                {
                    _workspace.Open(settingPath!);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, ex.Message, "打开失败", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            else
            {
                _workspace.SelectModule(ConfigModule.Drivers, null);
            }

            RebuildNavTree(selectModule: ConfigModule.Drivers, selectKey: null);
            UpdateGridHeaders();
            SyncTitle();
        };
    }

    private void SyncTitle()
    {
        var kind = _workspace.DocumentKindLabel;
        var path = string.IsNullOrWhiteSpace(_workspace.DocumentPath)
            ? "(未打开)"
            : System.IO.Path.GetFileName(_workspace.DocumentPath);
        Title = $"MDKOSS.Config.Wpf [{kind}] — {_workspace.ProjectName} — {path}";
    }

    private void UpdateGridHeaders()
    {
        if (_workspace.IsBrowsingDbTable)
        {
            SyncDbTableBrowser();
            return;
        }

        if (CenterGrid.Columns.Count < 4)
        {
            return;
        }

        CenterGrid.Columns[0].Header = _workspace.ColHeader1;
        CenterGrid.Columns[1].Header = _workspace.ColHeader2;
        CenterGrid.Columns[2].Header = string.IsNullOrEmpty(_workspace.ColHeader3) ? " " : _workspace.ColHeader3;
        CenterGrid.Columns[3].Header = string.IsNullOrEmpty(_workspace.ColHeader4) ? " " : _workspace.ColHeader4;
        CenterGrid.Columns[2].Visibility = string.IsNullOrEmpty(_workspace.ColHeader3) ? Visibility.Collapsed : Visibility.Visible;
        CenterGrid.Columns[3].Visibility = string.IsNullOrEmpty(_workspace.ColHeader4) ? Visibility.Collapsed : Visibility.Visible;
    }

    private void SyncDbTableBrowser()
    {
        DbTableBrowser.SelectRow(_workspace.SelectedDbRow);
    }

    private void RebuildNavTree(ConfigModule? selectModule, string? selectKey)
    {
        _suppressTreeSelection = true;
        NavTree.Items.Clear();

        var project = new TreeViewItem
        {
            Header = string.IsNullOrWhiteSpace(_workspace.ProjectName) ? "Project" : _workspace.ProjectName,
            IsExpanded = true,
            Tag = new NavTag(NavKind.Project, null, null),
        };
        NavTree.Items.Add(project);

        TreeViewItem? toSelect = null;
        foreach (var (module, title, components) in BuildModuleEntries())
        {
            var moduleNode = new TreeViewItem
            {
                Header = $"{title} ({components.Count})",
                IsExpanded = module is ConfigModule.Drivers or ConfigModule.Devices or ConfigModule.Tasks or ConfigModule.Database,
                Tag = new NavTag(NavKind.Module, module, null),
            };
            project.Items.Add(moduleNode);

            if (selectModule == module && string.IsNullOrEmpty(selectKey))
            {
                toSelect = moduleNode;
            }

            foreach (var (key, compTitle) in components)
            {
                var leaf = new TreeViewItem
                {
                    Header = compTitle,
                    Tag = new NavTag(NavKind.Component, module, key),
                };
                moduleNode.Items.Add(leaf);
                if (selectModule == module && string.Equals(selectKey, key, StringComparison.OrdinalIgnoreCase))
                {
                    toSelect = leaf;
                }
            }
        }

        if (toSelect is not null)
        {
            toSelect.IsSelected = true;
            toSelect.BringIntoView();
        }

        _suppressTreeSelection = false;
    }

    private List<(ConfigModule Module, string Title, List<(string Key, string Title)> Components)> BuildModuleEntries()
    {
        var result = new List<(ConfigModule, string, List<(string, string)>)>();
        var current = _workspace.CurrentModule;
        var selectedKey = _workspace.IsBrowsingDbTable
            ? _workspace.SelectedDbTable
            : _workspace.SelectedItem?.Key;

        foreach (ConfigModule m in Enum.GetValues<ConfigModule>())
        {
            _workspace.SelectModule(m, null);
            var comps = _workspace.Items.Select(i => (i.Key, i.Title)).ToList();
            result.Add((m, ConfigWorkspace.ModuleDisplayName(m), comps));
        }

        _workspace.SelectModule(current, selectedKey);
        return result;
    }

    private void NavTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (_suppressTreeSelection || e.NewValue is not TreeViewItem { Tag: NavTag tag })
        {
            return;
        }

        switch (tag.Kind)
        {
            case NavKind.Project:
                break;
            case NavKind.Module when tag.Module is { } module:
                _workspace.SelectModule(module, null);
                UpdateGridHeaders();
                SyncGridSelection(null);
                break;
            case NavKind.Component when tag.Module is { } module && tag.Key is not null:
                _workspace.SelectModule(module, tag.Key);
                UpdateGridHeaders();
                if (_workspace.IsBrowsingDbTable)
                {
                    SyncDbGridSelection(_workspace.SelectedDbRow);
                }
                else
                {
                    SyncGridSelection(_workspace.SelectedItem);
                }

                break;
        }

        SyncTitle();
    }

    private void CenterGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressGridSelection)
        {
            return;
        }

        if (CenterGrid.SelectedItem is ComponentItem item)
        {
            _workspace.SelectItem(item);
            if (_workspace.IsBrowsingDbTable)
            {
                UpdateGridHeaders();
                SyncDbGridSelection(_workspace.SelectedDbRow);
            }
            else
            {
                HighlightTreeComponent(item.Module, item.Key);
            }
        }
    }

    private void DbTableBrowser_RowSelectionChanged(object? sender, DbRowItem? row)
    {
        if (_suppressGridSelection)
        {
            return;
        }

        _workspace.SelectDbRow(row);
    }

    private void SyncDbGridSelection(DbRowItem? row)
    {
        _suppressGridSelection = true;
        DbTableBrowser.SelectRow(row);
        _suppressGridSelection = false;
    }

    private void CenterGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        // Focus property panel conceptually — draft already loaded.
        if (_workspace.SelectedItem is not null && !_workspace.Draft.IsReadOnly)
        {
            // no-op visual focus; Apply remains explicit
        }
    }

    private void SyncGridSelection(ComponentItem? item)
    {
        _suppressGridSelection = true;
        CenterGrid.SelectedItem = item;
        if (item is not null)
        {
            CenterGrid.ScrollIntoView(item);
        }

        _suppressGridSelection = false;
    }

    private void HighlightTreeComponent(ConfigModule module, string key)
    {
        _suppressTreeSelection = true;
        foreach (TreeViewItem project in NavTree.Items)
        {
            foreach (TreeViewItem moduleNode in project.Items)
            {
                if (moduleNode.Tag is not NavTag { Kind: NavKind.Module } mt || mt.Module != module)
                {
                    continue;
                }

                foreach (TreeViewItem leaf in moduleNode.Items)
                {
                    if (leaf.Tag is NavTag { Kind: NavKind.Component } ct
                        && string.Equals(ct.Key, key, StringComparison.OrdinalIgnoreCase))
                    {
                        leaf.IsSelected = true;
                        leaf.BringIntoView();
                        _suppressTreeSelection = false;
                        return;
                    }
                }
            }
        }

        _suppressTreeSelection = false;
    }

    private void RefreshTreeKeepingSelection()
    {
        var module = _workspace.CurrentModule;
        var key = _workspace.IsBrowsingDbTable
            ? _workspace.SelectedDbTable
            : _workspace.SelectedItem?.Key;
        RebuildNavTree(module, key);
        UpdateGridHeaders();
        if (_workspace.IsBrowsingDbTable)
        {
            SyncDbGridSelection(_workspace.SelectedDbRow);
        }
        else
        {
            SyncGridSelection(_workspace.SelectedItem);
        }

        SyncTitle();
    }

    private void OpenSetting()
    {
        var dlg = new OpenFileDialog
        {
            Filter =
                "配置文件 (*.setting.json;*.json;*.db)|*.setting.json;*.json;*.db|" +
                "Setting JSON (*.setting.json;*.json)|*.setting.json;*.json|" +
                "SQLite DB (*.db)|*.db|" +
                "All files|*.*",
            Title = "打开配置（JSON 或数据库）",
        };
        if (dlg.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            _workspace.Open(dlg.FileName);
            RebuildNavTree(ConfigModule.Drivers, null);
            UpdateGridHeaders();
            SyncTitle();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "打开失败", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void SaveSetting()
    {
        try
        {
            if (_workspace.DocumentKind == ConfigDocumentKind.None)
            {
                // No primary doc yet — ask user which format to create.
                var pick = MessageBox.Show(
                    this,
                    "尚未打开文档。\n是 = 另存为 JSON\n否 = 另存为数据库\n取消 = 取消",
                    "保存",
                    MessageBoxButton.YesNoCancel,
                    MessageBoxImage.Question);
                if (pick == MessageBoxResult.Yes)
                {
                    SaveAsJson_Click(this, new RoutedEventArgs());
                }
                else if (pick == MessageBoxResult.No)
                {
                    SaveAsDb_Click(this, new RoutedEventArgs());
                }

                return;
            }

            _workspace.Save();
            RefreshTreeKeepingSelection();
            MessageBox.Show(
                this,
                $"已保存 [{_workspace.DocumentKindLabel}]:\n{_workspace.DocumentPath}",
                "保存",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "保存失败", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Reload_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            _workspace.Reload();
            RebuildNavTree(_workspace.CurrentModule, null);
            UpdateGridHeaders();
            SyncTitle();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "重新加载失败", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void SaveAsJson_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new SaveFileDialog
        {
            Filter = "Setting JSON (*.setting.json)|*.setting.json|JSON (*.json)|*.json",
            FileName = SuggestJsonFileName(),
            Title = "另存为 JSON（将成为当前文档）",
        };
        if (dlg.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            _workspace.SaveAsJson(dlg.FileName);
            RefreshTreeKeepingSelection();
            MessageBox.Show(this, $"已另存为 JSON:\n{dlg.FileName}", "另存为 JSON",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "另存为 JSON 失败", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void SaveAsDb_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new SaveFileDialog
        {
            Filter = "SQLite DB (*.db)|*.db|All files|*.*",
            FileName = SuggestDbFileName(),
            Title = "另存为数据库（将成为当前文档）",
        };
        if (dlg.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            _workspace.SaveAsDatabase(dlg.FileName);
            RefreshTreeKeepingSelection();
            MessageBox.Show(this, $"已另存为数据库:\n{dlg.FileName}", "另存为数据库",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "另存为数据库失败", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ExportJson_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new SaveFileDialog
        {
            Filter = "Setting JSON (*.setting.json)|*.setting.json|JSON (*.json)|*.json",
            FileName = SuggestJsonFileName(),
            Title = "导出为 JSON（不切换当前文档）",
        };
        if (dlg.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            _workspace.ExportJson(dlg.FileName);
            RefreshTreeKeepingSelection();
            MessageBox.Show(this, $"已导出 JSON:\n{dlg.FileName}", "导出为 JSON",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "导出 JSON 失败", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ExportDb_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new SaveFileDialog
        {
            Filter = "SQLite DB (*.db)|*.db|All files|*.*",
            FileName = SuggestDbFileName(),
            Title = "导出为数据库（不切换当前文档）",
        };
        if (dlg.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            var result = _workspace.ExportDatabase(dlg.FileName);
            RefreshTreeKeepingSelection();
            MessageBox.Show(
                this,
                $"已导出数据库:\n{result.DatabasePath}\n\n{result}",
                "导出为数据库",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "导出数据库失败", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private string SuggestJsonFileName()
    {
        if (!string.IsNullOrWhiteSpace(_workspace.JsonPath))
        {
            return System.IO.Path.GetFileName(_workspace.JsonPath);
        }

        if (!string.IsNullOrWhiteSpace(_workspace.DatabasePath))
        {
            var name = System.IO.Path.GetFileNameWithoutExtension(_workspace.DatabasePath);
            return $"{name}.setting.json";
        }

        return "export.setting.json";
    }

    private string SuggestDbFileName()
    {
        if (!string.IsNullOrWhiteSpace(_workspace.DatabasePath))
        {
            return System.IO.Path.GetFileName(_workspace.DatabasePath);
        }

        if (!string.IsNullOrWhiteSpace(_workspace.JsonPath))
        {
            var name = System.IO.Path.GetFileNameWithoutExtension(_workspace.JsonPath);
            if (name.EndsWith(".setting", StringComparison.OrdinalIgnoreCase))
            {
                name = name[..^".setting".Length];
            }

            return string.IsNullOrWhiteSpace(name) ? "mdk.db" : $"{name}.db";
        }

        return "mdk.db";
    }

    private void AddComponent()
    {
        try
        {
            if (_workspace.IsBrowsingDbTable)
            {
                _workspace.AddDbRow();
                return;
            }

            var req = _workspace.PrepareCreateRequest();
            var dlg = new ComponentEditorDialog(_workspace.CurrentModule, req) { Owner = this };
            if (dlg.ShowDialog() != true)
            {
                return;
            }

            _workspace.CommitCreate(req);
            RefreshTreeKeepingSelection();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "新建失败", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void AddToolbar_Click(object sender, RoutedEventArgs e) => AddComponent();

    private void AddParamRow_Click(object sender, RoutedEventArgs e)
    {
        if (_workspace.Draft.IsReadOnly)
        {
            return;
        }

        _workspace.Draft.ParameterRows.Add(new KvPairRow());
    }

    private void RemoveParamRow_Click(object sender, RoutedEventArgs e)
    {
        if (_workspace.Draft.IsReadOnly)
        {
            return;
        }

        if (ParamGrid.SelectedItem is KvPairRow row)
        {
            _workspace.Draft.ParameterRows.Remove(row);
        }
        else if (_workspace.Draft.ParameterRows.Count > 0)
        {
            _workspace.Draft.ParameterRows.RemoveAt(_workspace.Draft.ParameterRows.Count - 1);
        }
    }

    private void SyncParamsJson_Click(object sender, RoutedEventArgs e)
    {
        if (_workspace.Draft.IsReadOnly)
        {
            return;
        }

        _workspace.Draft.SyncJsonFromRows();
    }

    private void ParamsJson_LostFocus(object sender, RoutedEventArgs e)
    {
        if (_workspace.Draft.IsReadOnly)
        {
            return;
        }

        try
        {
            _workspace.Draft.SyncRowsFromJson();
        }
        catch
        {
            // ignore parse errors while typing
        }
    }

    private void ExportModule_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new SaveFileDialog
        {
            Filter = "JSON (*.json)|*.json|All files|*.*",
            FileName = $"{_workspace.ModuleTitle.ToLowerInvariant()}.json",
            Title = $"导出模块 {_workspace.ModuleTitle}",
        };
        if (dlg.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            _workspace.ExportModule(dlg.FileName);
            MessageBox.Show(this, $"已导出:\n{dlg.FileName}", "导出模块", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "导出模块失败", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ImportModuleMerge_Click(object sender, RoutedEventArgs e) => ImportModule(replace: false);

    private void ImportModuleReplace_Click(object sender, RoutedEventArgs e)
    {
        var confirm = MessageBox.Show(
            this,
            $"替换当前模块「{_workspace.ModuleTitle}」全部数据？",
            "确认替换导入",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.Yes)
        {
            return;
        }

        ImportModule(replace: true);
    }

    private void ImportModule(bool replace)
    {
        var dlg = new OpenFileDialog
        {
            Filter = "JSON (*.json)|*.json|All files|*.*",
            Title = replace ? $"导入并替换 {_workspace.ModuleTitle}" : $"导入并合并 {_workspace.ModuleTitle}",
        };
        if (dlg.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            _workspace.ImportModule(dlg.FileName, replace);
            RefreshTreeKeepingSelection();
            MessageBox.Show(this, $"已导入:\n{dlg.FileName}", "导入模块", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "导入模块失败", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void DuplicateComponent()
    {
        try
        {
            _workspace.DuplicateSelected();
            RefreshTreeKeepingSelection();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "复制失败", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void DeleteComponent()
    {
        if (_workspace.IsBrowsingDbTable)
        {
            DeleteDbRow_Click(this, new RoutedEventArgs());
            return;
        }

        if (_workspace.SelectedItem is null)
        {
            return;
        }

        var confirm = MessageBox.Show(
            this,
            $"删除组件「{_workspace.SelectedItem.Title}」？",
            "确认删除",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            _workspace.DeleteSelected();
            RefreshTreeKeepingSelection();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "删除失败", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void RefreshDbTable_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            _workspace.RefreshDbTable();
            UpdateGridHeaders();
            SyncDbGridSelection(_workspace.SelectedDbRow);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "刷新表失败", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void DeleteDbRow_Click(object sender, RoutedEventArgs e)
    {
        if (!_workspace.IsBrowsingDbTable || _workspace.SelectedDbRow is null)
        {
            MessageBox.Show(this, "请先在 Database 表中选择一行。", "删除", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var confirm = MessageBox.Show(
            this,
            $"删除表 {_workspace.SelectedDbTable} 中主键为「{_workspace.SelectedDbRow.RowKey}」的行？",
            "确认删除",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            _workspace.DeleteDbRow();
            UpdateGridHeaders();
            SyncDbGridSelection(null);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "删除失败", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void MoveUp_Click(object sender, RoutedEventArgs e) => Move(-1);
    private void MoveDown_Click(object sender, RoutedEventArgs e) => Move(1);

    private void Move(int delta)
    {
        try
        {
            _workspace.MoveSelected(delta);
            RefreshTreeKeepingSelection();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "排序失败", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void Apply_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (!_workspace.Draft.IsReadOnly && _workspace.Draft.ShowParameters)
            {
                _workspace.Draft.SyncJsonFromRows();
            }

            _workspace.ApplyDraft();
            RefreshTreeKeepingSelection();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "应用属性失败", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void CtxEdit_Click(object sender, RoutedEventArgs e)
    {
        // Selection already drives the property panel.
        if (_workspace.SelectedItem is null && CenterGrid.SelectedItem is ComponentItem item)
        {
            _workspace.SelectItem(item);
        }
    }

    private void RefreshDb_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            _workspace.RefreshDbPreview();
            if (_workspace.CurrentModule == ConfigModule.Database)
            {
                RefreshTreeKeepingSelection();
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "刷新失败", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void NavDrivers_Click(object sender, RoutedEventArgs e) => JumpTo(ConfigModule.Drivers);
    private void NavDevices_Click(object sender, RoutedEventArgs e) => JumpTo(ConfigModule.Devices);
    private void NavTasks_Click(object sender, RoutedEventArgs e) => JumpTo(ConfigModule.Tasks);
    private void NavRecipes_Click(object sender, RoutedEventArgs e) => JumpTo(ConfigModule.Recipes);

    private void JumpTo(ConfigModule module)
    {
        _workspace.SelectModule(module, null);
        RebuildNavTree(module, null);
        UpdateGridHeaders();
    }

    private void Help_Click(object sender, RoutedEventArgs e)
    {
        MessageBox.Show(
            this,
            "打开 JSON 或 DB 均可编辑。\n" +
            "· 保存：写回当前打开的格式（JSON→JSON，DB→原 DB）\n" +
            "· 另存为 / 导出：切换或写出另一种格式\n" +
            "· 新建：弹窗快速配置；Type/DriverId 可下拉选择\n" +
            "· Parameters：右侧 Key/Value 表编辑；模块菜单可导入导出\n" +
            "· 调试：Driver / Axis / Platform / CameraDev / Task / Flow 独立窗（见 Debug/*.md）\n" +
            "左树选模块/组件；中部列表右键编辑；右侧改属性后点「应用属性」。",
            "界面说明",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private void DebugDriver_Click(object sender, RoutedEventArgs e) =>
        OpenDebugWindow(new DriverDebugWindow(_workspace, PreferKeyIfModule(ConfigModule.Drivers)));

    private void DebugAxis_Click(object sender, RoutedEventArgs e) =>
        OpenDebugWindow(new AxisDebugWindow(_workspace, PreferKeyIfModule(ConfigModule.Axis)));

    private void DebugPlatform_Click(object sender, RoutedEventArgs e) =>
        OpenDebugWindow(new PlatformDebugWindow(_workspace, PreferKeyIfModule(ConfigModule.Platform)));

    private void DebugCamera_Click(object sender, RoutedEventArgs e)
    {
        string? prefer = null;
        if (_workspace.SelectedItem is { } item)
        {
            var type = ResolveDeviceType(item);
            if (type is "cameradev" or "extcamera")
            {
                prefer = item.Key;
            }
        }

        OpenDebugWindow(new CameraDevDebugWindow(_workspace, prefer));
    }

    private void DebugTask_Click(object sender, RoutedEventArgs e) =>
        OpenDebugWindow(new TaskDebugWindow(
            _workspace,
            PreferKeyIfModule(ConfigModule.Tasks),
            RefreshTreeKeepingSelection));

    private void DebugFlow_Click(object sender, RoutedEventArgs e) =>
        OpenDebugWindow(new FlowEditorWindow(
            _workspace,
            PreferKeyIfModule(ConfigModule.Tasks),
            RefreshTreeKeepingSelection));

    private void DebugSelected_Click(object sender, RoutedEventArgs e)
    {
        var item = _workspace.SelectedItem;
        if (item is null)
        {
            MessageBox.Show(this, "请先选中一个组件。", "调试", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        switch (item.Module)
        {
            case ConfigModule.Drivers:
                OpenDebugWindow(new DriverDebugWindow(_workspace, item.Key));
                return;
            case ConfigModule.Axis:
                OpenDebugWindow(new AxisDebugWindow(_workspace, item.Key));
                return;
            case ConfigModule.Platform:
                OpenDebugWindow(new PlatformDebugWindow(_workspace, item.Key));
                return;
            case ConfigModule.Tasks:
            {
                if (item.Source is MdkSetting.TaskConfig task
                    && string.Equals(task.Type, "flow", StringComparison.OrdinalIgnoreCase))
                {
                    OpenDebugWindow(new FlowEditorWindow(_workspace, item.Key, RefreshTreeKeepingSelection));
                }
                else
                {
                    OpenDebugWindow(new TaskDebugWindow(_workspace, item.Key, RefreshTreeKeepingSelection));
                }

                return;
            }
            case ConfigModule.Devices:
            {
                var type = ResolveDeviceType(item);
                if (type == "axis")
                {
                    OpenDebugWindow(new AxisDebugWindow(_workspace, item.Key));
                    return;
                }

                if (PlatformDeviceParameterSet.IsPlatformFamilyType(type))
                {
                    OpenDebugWindow(new PlatformDebugWindow(_workspace, item.Key));
                    return;
                }

                if (type is "cameradev" or "extcamera")
                {
                    OpenDebugWindow(new CameraDevDebugWindow(_workspace, item.Key));
                    return;
                }

                MessageBox.Show(
                    this,
                    $"设备类型「{type}」暂无专用调试窗。\n可用：axis / platform 族 / cameradev / extcamera，或 Drivers / Tasks / Flow。",
                    "调试",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }
            default:
                MessageBox.Show(
                    this,
                    $"模块「{item.Module}」无专用调试窗。\n请从菜单「调试」打开。",
                    "调试",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
        }
    }

    private string? PreferKeyIfModule(ConfigModule module) =>
        _workspace.CurrentModule == module ? _workspace.SelectedItem?.Key : null;

    private string ResolveDeviceType(ComponentItem item)
    {
        if (item.Source is MdkSetting.DeviceConfig dev)
        {
            return (dev.Type ?? "").ToLowerInvariant();
        }

        var found = _workspace.Setting.Devices.FirstOrDefault(d =>
            string.Equals(d.Id, item.Key, StringComparison.OrdinalIgnoreCase));
        return (found?.Type ?? "").ToLowerInvariant();
    }

    private void OpenDebugWindow(Window window)
    {
        window.Owner = this;
        window.Show();
    }

    private void Exit_Click(object sender, RoutedEventArgs e) => Close();
}

internal enum NavKind
{
    Project,
    Module,
    Component,
}

internal sealed record NavTag(NavKind Kind, ConfigModule? Module, string? Key);

public sealed class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is true ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is Visibility.Visible;
}

public sealed class InvertBoolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is bool b && !b;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is bool b && !b;
}
