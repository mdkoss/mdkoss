using MDKOSS.Core;
using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace MDKOSS.Gui;

public sealed class ComponentConfigForm : Form
{
    private readonly string _settingPath;
    private readonly WorkspaceShell _shell = new();
    private readonly BindingSource _driversBinding = new();
    private readonly BindingSource _devicesBinding = new();
    private readonly BindingSource _tasksBinding = new();
    private readonly BindingSource _varsBinding = new();
    private readonly BindingSource _ioLabelsBinding = new();
    private readonly BindingSource _recipeVarKeysBinding = new();
    private readonly BindingSource _recipesBinding = new();
    private readonly ProjectSettings _projectSettings = new();
    // Kept for tests / BuildSettingFromRows compatibility.
    private readonly TextBox _projectNameBox = new();
    private readonly NumericUpDown _cycleMsBox = new() { Minimum = 1, Maximum = 600000, Value = 20 };
    private readonly TextBox _monitoringPrefixBox = new();
    private readonly ComboBox _activeRecipeCombo = new() { DropDownStyle = ComboBoxStyle.DropDownList };

    private readonly Dictionary<string, PageState> _pages = new(StringComparer.OrdinalIgnoreCase);
    private readonly StructureDiagramPanel _diagram = new();
    private readonly Panel _projectSummary = new() { Dock = DockStyle.Fill, Padding = new Padding(16) };
    private readonly Label _projectSummaryLabel = new() { Dock = DockStyle.Fill, AutoSize = false };
    private readonly Panel _helpPanel = new() { Dock = DockStyle.Fill, Padding = new Padding(16) };
    private readonly TextBox _searchBox = new() { Width = 220, PlaceholderText = "Search current table" };
    private readonly ToolStripMenuItem _viewTableItem = new() { Text = "Table", Checked = true, CheckOnClick = true };
    private readonly ToolStripMenuItem _viewDiagramItem = new() { Text = "Structure Diagram", CheckOnClick = true };
    private readonly ToolStripMenuItem _viewPropertiesItem = new() { Text = "Properties", Checked = true, CheckOnClick = true };

    private string _currentPage = "Project";
    private bool _showDiagram;

    public ComponentConfigForm(string settingPath)
    {
        _settingPath = settingPath;
        Text = "Component Config Manager";
        Width = 1280;
        Height = 760;
        MinimumSize = new Size(960, 600);
        StartPosition = FormStartPosition.CenterParent;
        KeyPreview = true;

        BuildPages();
        BuildMenus();
        BuildCenterToolbar();
        BuildProjectSummary();
        BuildHelpPanel();
        BuildNavigationTree();

        _shell.ModeLabel.Text = "Mode: Offline";
        _shell.AttachToForm(this);
        _shell.NavigationTree.AfterSelect += (_, e) => SelectPage(e.Node?.Tag as string);
        _shell.PropertyGrid.PropertyValueChanged += (_, _) => OnPropertyChanged();
        _diagram.SelectionChanged += (_, payload) => OnDiagramSelection(payload);

        KeyDown += (_, e) =>
        {
            if (e.Control && e.KeyCode == Keys.S)
            {
                ApplyChanges();
                e.SuppressKeyPress = true;
            }
        };

        LoadSetting();
        SelectPage("Project");
    }

    private void BuildMenus()
    {
        var file = new ToolStripMenuItem("File");
        file.DropDownItems.Add(CreateMenuItem("Reload", (_, _) => LoadSetting()));
        file.DropDownItems.Add(CreateMenuItem("Save", (_, _) => ApplyChanges(), Keys.Control | Keys.S));
        file.DropDownItems.Add(CreateMenuItem("Backup", (_, _) => BackupSetting()));
        file.DropDownItems.Add(new ToolStripSeparator());
        file.DropDownItems.Add(CreateMenuItem("Import Setting…", (_, _) => ImportSetting()));
        file.DropDownItems.Add(CreateMenuItem("Export Setting…", (_, _) => ExportSetting()));
        file.DropDownItems.Add(CreateMenuItem("Import Current…", (_, _) => ImportCurrentTab()));
        file.DropDownItems.Add(CreateMenuItem("Export Current…", (_, _) => ExportCurrentTab()));
        file.DropDownItems.Add(new ToolStripSeparator());
        file.DropDownItems.Add(CreateMenuItem("Close", (_, _) => Close()));

        var edit = new ToolStripMenuItem("Edit");
        edit.DropDownItems.Add(CreateMenuItem("Add", (_, _) => AddCurrentRow(), Keys.Control | Keys.N));
        edit.DropDownItems.Add(CreateMenuItem("Duplicate", (_, _) => DuplicateCurrentRow(), Keys.Control | Keys.D));
        edit.DropDownItems.Add(CreateMenuItem("Delete", (_, _) => DeleteCurrentRows(), Keys.Delete));
        edit.DropDownItems.Add(CreateMenuItem("Move Up", (_, _) => MoveCurrentRow(-1), Keys.Alt | Keys.Up));
        edit.DropDownItems.Add(CreateMenuItem("Move Down", (_, _) => MoveCurrentRow(1), Keys.Alt | Keys.Down));
        edit.DropDownItems.Add(new ToolStripSeparator());
        edit.DropDownItems.Add(CreateMenuItem("Apply Param Preset", (_, _) => ApplyParameterPreset()));

        var view = new ToolStripMenuItem("View");
        _viewTableItem.Click += (_, _) => SetViewMode(diagram: false);
        _viewDiagramItem.Click += (_, _) => SetViewMode(diagram: true);
        _viewPropertiesItem.Click += (_, _) =>
        {
            _shell.SetPropertiesVisible(_viewPropertiesItem.Checked);
        };
        view.DropDownItems.Add(_viewTableItem);
        view.DropDownItems.Add(_viewDiagramItem);
        view.DropDownItems.Add(new ToolStripSeparator());
        view.DropDownItems.Add(_viewPropertiesItem);

        _shell.Menu.Items.Add(file);
        _shell.Menu.Items.Add(edit);
        _shell.Menu.Items.Add(view);
        var help = new ToolStripMenuItem("Help");
        help.DropDownItems.Add(CreateMenuItem("About Config Manager", (_, _) =>
            MessageBox.Show(this,
                "Offline configuration editor.\nLayout: Menu / Tree / Table|Diagram / Properties / Status.",
                "About",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information)));
        _shell.Menu.Items.Add(help);
    }

    private static ToolStripMenuItem CreateMenuItem(string text, EventHandler onClick, Keys shortcut = Keys.None)
    {
        var item = new ToolStripMenuItem(text, null, onClick);
        if (shortcut != Keys.None)
        {
            item.ShortcutKeys = shortcut;
        }

        return item;
    }

    private void BuildCenterToolbar()
    {
        var bar = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            WrapContents = false
        };
        var clear = new Button { Text = "Clear", Width = 64, Height = 26 };
        _searchBox.TextChanged += (_, _) => ApplyCurrentSearch();
        clear.Click += (_, _) =>
        {
            _searchBox.Clear();
            ApplyCurrentSearch();
        };
        bar.Controls.Add(new Label { Text = "Filter:", AutoSize = true, Padding = new Padding(0, 6, 4, 0) });
        bar.Controls.Add(_searchBox);
        bar.Controls.Add(clear);
        _shell.CenterToolbarHost.Controls.Add(bar);
    }

    private void BuildProjectSummary()
    {
        _projectSummaryLabel.Font = new Font(Font.FontFamily, 10f);
        _projectSummary.Controls.Add(_projectSummaryLabel);
    }

    private void BuildHelpPanel()
    {
        var label = new Label
        {
            Dock = DockStyle.Fill,
            Text =
                "Import / Export\n\n" +
                "• File → Import/Export Setting — whole MdkSetting JSON\n" +
                "• File → Import/Export Current — rows for the selected tree node\n" +
                "• File → Backup — timestamped copy beside the setting file\n\n" +
                "Edit rows in the center table; detailed fields and parameters appear in Properties."
        };
        _helpPanel.Controls.Add(label);
    }

    private void BuildPages()
    {
        _pages["Project"] = new PageState("Project", null, null);
        _pages["Drivers"] = new PageState("Drivers", _driversBinding, CreateDriverGrid());
        _pages["Devices"] = new PageState("Devices", _devicesBinding, CreateDeviceGrid());
        _pages["I/O Labels"] = new PageState("I/O Labels", _ioLabelsBinding, CreateIoLabelGrid());
        _pages["Tasks"] = new PageState("Tasks", _tasksBinding, CreateTaskGrid());
        _pages["Vars"] = new PageState("Vars", _varsBinding, CreateVarsGrid());
        _pages["Recipe Keys"] = new PageState("Recipe Keys", _recipeVarKeysBinding, CreateRecipeVarKeysGrid());
        _pages["Recipes"] = new PageState("Recipes", _recipesBinding, CreateRecipesGrid());
        _pages["Import / Export"] = new PageState("Import / Export", null, null);

        foreach (var page in _pages.Values.Where(p => p.Grid is not null))
        {
            page.Grid!.DataSource = page.Binding;
            page.Grid.SelectionChanged += (_, _) => BindPropertyFromGrid(page);
            page.Grid.KeyDown += (_, e) => HandleGridShortcuts(page, e);
        }

        foreach (var binding in new[]
                 {
                     _driversBinding, _devicesBinding, _tasksBinding, _varsBinding,
                     _ioLabelsBinding, _recipeVarKeysBinding, _recipesBinding
                 })
        {
            binding.ListChanged += (_, _) => UpdateStatus();
        }
    }

    private void BuildNavigationTree()
    {
        var tree = _shell.NavigationTree;
        tree.Nodes.Clear();
        var project = CreateNavigationNode("Project", "Project");
        var components = CreateNavigationNode("Components", "Drivers");
        components.Nodes.Add(CreateNavigationNode("Drivers", "Drivers"));
        components.Nodes.Add(CreateNavigationNode("Devices", "Devices"));
        components.Nodes.Add(CreateNavigationNode("I/O Labels", "I/O Labels"));
        components.Nodes.Add(CreateNavigationNode("Tasks", "Tasks"));
        components.Nodes.Add(CreateNavigationNode("Variables", "Vars"));
        var recipes = CreateNavigationNode("Recipes", "Recipe Keys");
        recipes.Nodes.Add(CreateNavigationNode("Recipe Keys", "Recipe Keys"));
        recipes.Nodes.Add(CreateNavigationNode("Presets", "Recipes"));
        var exchange = CreateNavigationNode("Import / Export", "Import / Export");

        tree.Nodes.Add(project);
        tree.Nodes.Add(components);
        tree.Nodes.Add(recipes);
        tree.Nodes.Add(exchange);
        tree.ExpandAll();
        tree.SelectedNode = project;
    }

    private static TreeNode CreateNavigationNode(string text, string pageName) => new(text) { Tag = pageName };

    private void SelectPage(string? pageName)
    {
        if (string.IsNullOrWhiteSpace(pageName) || !_pages.ContainsKey(pageName))
        {
            return;
        }

        _currentPage = pageName;
        RefreshCenterWorkspace();
        UpdateStatus();
    }

    private void SetViewMode(bool diagram)
    {
        _showDiagram = diagram;
        _viewTableItem.Checked = !diagram;
        _viewDiagramItem.Checked = diagram;
        RefreshCenterWorkspace();
    }

    private void RefreshCenterWorkspace()
    {
        _shell.CenterHost.Controls.Clear();
        SyncProjectSettingsFromControls();
        RefreshProjectSummary();

        if (_showDiagram && _currentPage is "Project" or "Drivers" or "Devices" or "Tasks" or "Components")
        {
            RefreshDiagram();
            _shell.CenterHost.Controls.Add(_diagram);
            _shell.CenterToolbarHost.Enabled = false;
            return;
        }

        _shell.CenterToolbarHost.Enabled = _pages[_currentPage].Grid is not null;

        if (_currentPage == "Project")
        {
            _shell.CenterHost.Controls.Add(_projectSummary);
            _shell.PropertyGrid.SelectedObject = _projectSettings;
            _shell.SelectionLabel.Text = "Selected: Project";
            return;
        }

        if (_currentPage == "Import / Export")
        {
            _shell.CenterHost.Controls.Add(_helpPanel);
            _shell.PropertyGrid.SelectedObject = null;
            _shell.SelectionLabel.Text = "Selected: Import / Export";
            return;
        }

        var page = _pages[_currentPage];
        if (page.Grid is null)
        {
            return;
        }

        _shell.CenterHost.Controls.Add(page.Grid);
        ApplyCurrentSearch();
        BindPropertyFromGrid(page);
    }

    private void RefreshDiagram()
    {
        _diagram.Bind(
            _driversBinding.List.Cast<DriverRow>().Select(r => (r.Id, r.Type, r.Enabled)),
            _devicesBinding.List.Cast<DeviceRow>().Select(r => (r.Id, r.Type, r.DriverId, r.Enabled)),
            _tasksBinding.List.Cast<TaskRow>().Select(r => (r.Name, r.Type, r.DriverId)));
    }

    private void OnDiagramSelection(object? payload)
    {
        _shell.PropertyGrid.SelectedObject = payload switch
        {
            DriverRow or DeviceRow or TaskRow => payload,
            ValueTuple<string, string, bool> driver =>
                _driversBinding.List.Cast<DriverRow>().FirstOrDefault(r => r.Id == driver.Item1),
            ValueTuple<string, string, string, bool> device =>
                _devicesBinding.List.Cast<DeviceRow>().FirstOrDefault(r => r.Id == device.Item1),
            ValueTuple<string, string, string> task =>
                _tasksBinding.List.Cast<TaskRow>().FirstOrDefault(r => r.Name == task.Item1),
            _ => null
        };
        _shell.SelectionLabel.Text = _shell.PropertyGrid.SelectedObject is null
            ? "Selected: -"
            : $"Selected: {_shell.PropertyGrid.SelectedObject}";
    }

    private void BindPropertyFromGrid(PageState page)
    {
        if (page.Grid?.CurrentRow?.DataBoundItem is { } item && !page.Grid.CurrentRow.IsNewRow)
        {
            _shell.PropertyGrid.SelectedObject = item;
            _shell.SelectionLabel.Text = $"Selected: {item}";
        }
        else
        {
            _shell.PropertyGrid.SelectedObject = null;
            _shell.SelectionLabel.Text = $"Selected: {_currentPage}";
        }
    }

    private void OnPropertyChanged()
    {
        if (_shell.PropertyGrid.SelectedObject is ProjectSettings)
        {
            SyncProjectControlsFromSettings();
            RefreshProjectSummary();
        }

        if (_pages.TryGetValue(_currentPage, out var page) && page.Binding is not null)
        {
            page.Binding.ResetBindings(false);
        }

        UpdateStatus();
    }

    private void SyncProjectControlsFromSettings()
    {
        _projectNameBox.Text = _projectSettings.ProjectName;
        _cycleMsBox.Value = Math.Clamp(_projectSettings.CycleMs, (int)_cycleMsBox.Minimum, (int)_cycleMsBox.Maximum);
        _monitoringPrefixBox.Text = _projectSettings.MonitoringPrefix;
        var active = _projectSettings.ActiveRecipeId ?? string.Empty;
        if (!_activeRecipeCombo.Items.Cast<object>().Any(i => string.Equals(Convert.ToString(i), active, StringComparison.OrdinalIgnoreCase)))
        {
            _activeRecipeCombo.Items.Add(active);
        }

        _activeRecipeCombo.SelectedItem = active;
        if (_activeRecipeCombo.SelectedIndex < 0 && _activeRecipeCombo.Items.Count > 0)
        {
            _activeRecipeCombo.SelectedIndex = 0;
        }
    }

    private void SyncProjectSettingsFromControls()
    {
        _projectSettings.ProjectName = _projectNameBox.Text;
        _projectSettings.CycleMs = (int)_cycleMsBox.Value;
        _projectSettings.MonitoringPrefix = _monitoringPrefixBox.Text;
        _projectSettings.ActiveRecipeId = _activeRecipeCombo.Text;
    }

    private void RefreshProjectSummary()
    {
        SyncProjectSettingsFromControls();
        _projectSummaryLabel.Text =
            $"Project: {_projectSettings.ProjectName}\n" +
            $"Cycle: {_projectSettings.CycleMs} ms\n" +
            $"Monitoring: {(_projectSettings.MonitoringPrefix is { Length: > 0 } p ? p : "(default)")}\n" +
            $"Active Recipe: {(_projectSettings.ActiveRecipeId is { Length: > 0 } r ? r : "(none)")}\n\n" +
            "Select Properties on the right to edit project fields.\n" +
            "Use View → Structure Diagram to inspect Driver ↔ Device ↔ Task links.";
    }

    private void HandleGridShortcuts(PageState page, KeyEventArgs e)
    {
        if (e.Control && e.KeyCode == Keys.N)
        {
            AddGridRow(page);
            e.SuppressKeyPress = true;
        }
        else if (e.Control && e.KeyCode == Keys.D)
        {
            DuplicateCurrentRow(page);
            e.SuppressKeyPress = true;
        }
        else if (e.Alt && e.KeyCode == Keys.Up)
        {
            MoveCurrentRow(page, -1);
            e.SuppressKeyPress = true;
        }
        else if (e.Alt && e.KeyCode == Keys.Down)
        {
            MoveCurrentRow(page, 1);
            e.SuppressKeyPress = true;
        }
    }

    private PageState? CurrentGridPage() =>
        _pages.TryGetValue(_currentPage, out var page) && page.Grid is not null ? page : null;

    private void AddCurrentRow()
    {
        if (CurrentGridPage() is { } page)
        {
            AddGridRow(page);
        }
    }

    private void DuplicateCurrentRow()
    {
        if (CurrentGridPage() is { } page)
        {
            DuplicateCurrentRow(page);
        }
    }

    private void DeleteCurrentRows()
    {
        if (CurrentGridPage()?.Grid is { } grid)
        {
            DeleteSelectedRows(grid);
            UpdateStatus();
        }
    }

    private void MoveCurrentRow(int direction)
    {
        if (CurrentGridPage() is { } page)
        {
            MoveCurrentRow(page, direction);
        }
    }

    private void AddGridRow(PageState page)
    {
        if (page.Binding is null || page.Grid is null)
        {
            return;
        }

        object? row;
        if (TryCreateRowFromDialog(page.Name, this, out row))
        {
            if (row is null)
            {
                return;
            }

            if (page.Binding.List is System.Collections.IList list)
            {
                list.Add(row);
                page.Binding.ResetBindings(false);
            }
        }
        else
        {
            row = page.Binding.AddNew();
            page.Binding.EndEdit();
        }

        page.Binding.Position = page.Binding.IndexOf(row);
        FocusCurrentGridRow(page.Grid);
        BindPropertyFromGrid(page);
        UpdateStatus();
    }

    private static bool TryCreateRowFromDialog(string title, IWin32Window? owner, out object? row)
    {
        row = title switch
        {
            "Drivers" => DriverRowDialog.Create(owner),
            "Devices" => DeviceRowDialog.Create(owner),
            "Tasks" => TaskRowDialog.Create(owner),
            _ => null
        };

        return title is "Drivers" or "Devices" or "Tasks";
    }

    private void DuplicateCurrentRow(PageState page)
    {
        if (page.Binding?.Current is null || page.Grid is null || page.Binding.List is not System.Collections.IList list)
        {
            return;
        }

        var copy = CloneRow(page.Binding.Current);
        var insertIndex = Math.Min(page.Binding.Position + 1, page.Binding.Count);
        list.Insert(insertIndex, copy);
        page.Binding.Position = insertIndex;
        page.Binding.ResetBindings(false);
        FocusCurrentGridRow(page.Grid);
        BindPropertyFromGrid(page);
        UpdateStatus();
    }

    private static object CloneRow(object source)
    {
        var type = source.GetType();
        var copy = Activator.CreateInstance(type)
            ?? throw new InvalidOperationException($"Cannot create row type '{type.Name}'.");
        foreach (var property in type.GetProperties().Where(p => p.CanRead && p.CanWrite))
        {
            property.SetValue(copy, property.GetValue(source));
        }

        return copy;
    }

    private static void DeleteSelectedRows(DataGridView grid)
    {
        foreach (DataGridViewRow row in grid.SelectedRows.Cast<DataGridViewRow>().OrderByDescending(r => r.Index))
        {
            if (!row.IsNewRow)
            {
                grid.Rows.Remove(row);
            }
        }
    }

    private void MoveCurrentRow(PageState page, int direction)
    {
        if (page.Binding?.Current is null || page.Grid is null || page.Binding.List is not System.Collections.IList list)
        {
            return;
        }

        var oldIndex = page.Binding.Position;
        var newIndex = oldIndex + direction;
        if (newIndex < 0 || newIndex >= list.Count)
        {
            return;
        }

        var item = list[oldIndex];
        list.RemoveAt(oldIndex);
        list.Insert(newIndex, item);
        page.Binding.Position = newIndex;
        page.Binding.ResetBindings(false);
        FocusCurrentGridRow(page.Grid);
        BindPropertyFromGrid(page);
    }

    private void ApplyCurrentSearch()
    {
        if (CurrentGridPage()?.Grid is not { } grid)
        {
            return;
        }

        ApplyGridSearch(grid, _searchBox.Text);
    }

    private static void ApplyGridSearch(DataGridView grid, string text)
    {
        var query = text.Trim();
        grid.CurrentCell = null;
        foreach (DataGridViewRow row in grid.Rows)
        {
            if (row.IsNewRow)
            {
                continue;
            }

            row.Visible = string.IsNullOrWhiteSpace(query) || RowContainsText(row, query);
        }
    }

    private static bool RowContainsText(DataGridViewRow row, string query)
    {
        foreach (DataGridViewCell cell in row.Cells)
        {
            if (cell.Value is not null
                && cell.Value.ToString()?.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }
        }

        return false;
    }

    private static void FocusCurrentGridRow(DataGridView grid)
    {
        if (grid.CurrentRow is null)
        {
            return;
        }

        grid.Focus();
        grid.CurrentRow.Selected = true;
    }

    private static DataGridView CreateDriverGrid()
    {
        var grid = CreateGrid();
        grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(DriverRow.Id), HeaderText = "Id", Width = 160 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(DriverRow.Type), HeaderText = "Type", Width = 140 });
        grid.Columns.Add(new DataGridViewCheckBoxColumn { DataPropertyName = nameof(DriverRow.Enabled), HeaderText = "Enabled", Width = 80 });
        return grid;
    }

    private static DataGridView CreateDeviceGrid()
    {
        var grid = CreateGrid();
        grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(DeviceRow.Id), HeaderText = "Id", Width = 140 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(DeviceRow.Name), HeaderText = "Name", Width = 160 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(DeviceRow.Type), HeaderText = "Type", Width = 120 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(DeviceRow.DriverId), HeaderText = "DriverId", Width = 140 });
        grid.Columns.Add(new DataGridViewCheckBoxColumn { DataPropertyName = nameof(DeviceRow.Enabled), HeaderText = "Enabled", Width = 80 });
        return grid;
    }

    private static DataGridView CreateTaskGrid()
    {
        var grid = CreateGrid();
        grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(TaskRow.Name), HeaderText = "Name", Width = 170 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(TaskRow.Type), HeaderText = "Type", Width = 140 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(TaskRow.DriverId), HeaderText = "DriverId", Width = 140 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(TaskRow.IntervalMs), HeaderText = "IntervalMs", Width = 100 });
        return grid;
    }

    private static DataGridView CreateIoLabelGrid()
    {
        var grid = CreateGrid();
        grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(IoLabelRow.DeviceId), HeaderText = "DeviceId", Width = 140 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(IoLabelRow.Alias), HeaderText = "Alias", Width = 150 });
        grid.Columns.Add(new DataGridViewComboBoxColumn { DataPropertyName = nameof(IoLabelRow.Direction), HeaderText = "Direction", Width = 90, DataSource = new[] { "in", "out" } });
        grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(IoLabelRow.DriverId), HeaderText = "DriverId", Width = 140 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(IoLabelRow.Address), HeaderText = "Address", Width = 140 });
        return grid;
    }

    private static DataGridView CreateVarsGrid()
    {
        var grid = CreateGrid();
        grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(VarRow.Key), HeaderText = "Key", Width = 220 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(VarRow.ValueJson), HeaderText = "Value JSON", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill });
        return grid;
    }

    private static DataGridView CreateRecipeVarKeysGrid()
    {
        var grid = CreateGrid();
        grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            DataPropertyName = nameof(RecipeVarKeyRow.Key),
            HeaderText = "Var Key (recipe-scoped)",
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill
        });
        return grid;
    }

    private static DataGridView CreateRecipesGrid()
    {
        var grid = CreateGrid();
        grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(RecipeRow.Id), HeaderText = "Id", Width = 140 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(RecipeRow.Name), HeaderText = "Name", Width = 160 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(RecipeRow.Description), HeaderText = "Description", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill });
        return grid;
    }

    private static DataGridView CreateGrid() => new()
    {
        Dock = DockStyle.Fill,
        AutoGenerateColumns = false,
        AllowUserToAddRows = false,
        AllowUserToDeleteRows = true,
        SelectionMode = DataGridViewSelectionMode.FullRowSelect,
        MultiSelect = true,
        AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.DisplayedCells,
        RowHeadersWidth = 28,
        ReadOnly = true
    };

    private void LoadSetting()
    {
        try
        {
            BindSetting(ConfigFormHelpers.LoadSetting(_settingPath));
            RefreshCenterWorkspace();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Load Setting Failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void BindSetting(MdkSetting setting)
    {
        _projectNameBox.Text = setting.ProjectName;
        _cycleMsBox.Value = Math.Clamp(setting.CycleMs, (int)_cycleMsBox.Minimum, (int)_cycleMsBox.Maximum);
        _monitoringPrefixBox.Text = setting.MonitoringPrefix ?? string.Empty;
        _driversBinding.DataSource = setting.Drivers.Select(DriverRow.FromConfig).ToList();
        _devicesBinding.DataSource = setting.Devices.Select(DeviceRow.FromConfig).ToList();
        _ioLabelsBinding.DataSource = BuildIoLabelRows(setting.Devices);
        _tasksBinding.DataSource = setting.Tasks.Select(TaskRow.FromConfig).ToList();
        _varsBinding.DataSource = setting.Vars.Select(VarRow.FromValue).OrderBy(v => v.Key, StringComparer.OrdinalIgnoreCase).ToList();
        _recipeVarKeysBinding.DataSource = setting.RecipeVarKeys
            .Select(key => new RecipeVarKeyRow { Key = key })
            .OrderBy(r => r.Key, StringComparer.OrdinalIgnoreCase)
            .ToList();
        _recipesBinding.DataSource = setting.Recipes.Select(RecipeRow.FromConfig).ToList();
        RefreshActiveRecipeCombo(setting);
        SyncProjectSettingsFromControls();
        UpdateStatus();
    }

    private void RefreshActiveRecipeCombo(MdkSetting setting)
    {
        _activeRecipeCombo.Items.Clear();
        _activeRecipeCombo.Items.Add(string.Empty);
        foreach (var recipe in setting.Recipes.OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase))
        {
            _activeRecipeCombo.Items.Add(recipe.Id);
        }

        var active = setting.ActiveRecipeId ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(active)
            && !setting.Recipes.Any(r => string.Equals(r.Id, active, StringComparison.OrdinalIgnoreCase)))
        {
            _activeRecipeCombo.Items.Add(active);
        }

        _activeRecipeCombo.SelectedItem = active;
        if (_activeRecipeCombo.SelectedIndex < 0)
        {
            _activeRecipeCombo.SelectedIndex = 0;
        }
    }

    private MdkSetting BuildSettingFromRows()
    {
        SyncProjectSettingsFromControls();
        ValidateNoBlankKeys(_driversBinding.List.Cast<DriverRow>().Select(r => r.Id), "driver id");
        ValidateNoBlankKeys(_devicesBinding.List.Cast<DeviceRow>().Select(r => r.Id), "device id");
        ValidateNoBlankKeys(_tasksBinding.List.Cast<TaskRow>().Select(r => r.Name), "task name");
        ValidateNoBlankKeys(_varsBinding.List.Cast<VarRow>().Select(r => r.Key), "var key");
        ValidateNoBlankKeys(_recipesBinding.List.Cast<RecipeRow>().Select(r => r.Id), "recipe id");

        var setting = new MdkSetting
        {
            ProjectName = string.IsNullOrWhiteSpace(_projectNameBox.Text) ? "MDKOSS" : _projectNameBox.Text.Trim(),
            CycleMs = (int)_cycleMsBox.Value,
            MonitoringPrefix = string.IsNullOrWhiteSpace(_monitoringPrefixBox.Text) ? null : _monitoringPrefixBox.Text.Trim(),
            Drivers = _driversBinding.List.Cast<DriverRow>().Select(r => r.ToConfig()).ToList(),
            Devices = _devicesBinding.List.Cast<DeviceRow>().Select(r => r.ToConfig()).ToList(),
            Tasks = _tasksBinding.List.Cast<TaskRow>().Select(r => r.ToConfig()).ToList(),
            Vars = BuildVars(),
            RecipeVarKeys = _recipeVarKeysBinding.List.Cast<RecipeVarKeyRow>()
                .Select(r => r.Key.Trim())
                .Where(k => !string.IsNullOrWhiteSpace(k))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList(),
            ActiveRecipeId = string.IsNullOrWhiteSpace(_activeRecipeCombo.Text) ? null : _activeRecipeCombo.Text.Trim(),
            Recipes = _recipesBinding.List.Cast<RecipeRow>().Select(r => r.ToConfig()).ToList()
        };

        ApplyIoLabelRows(setting.Devices);
        ValidateSetting(setting);
        return setting;
    }

    private static List<IoLabelRow> BuildIoLabelRows(IEnumerable<MdkSetting.DeviceConfig> devices)
    {
        var rows = new List<IoLabelRow>();
        foreach (var device in devices.Where(IsIoDevice))
        {
            foreach (var kv in device.Parameters.OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase))
            {
                var direction = kv.Key.StartsWith("in.", StringComparison.OrdinalIgnoreCase)
                    ? "in"
                    : kv.Key.StartsWith("out.", StringComparison.OrdinalIgnoreCase) ? "out" : null;
                if (direction is null)
                {
                    continue;
                }

                var alias = direction == "in" ? kv.Key[3..] : kv.Key[4..];
                var driverId = device.DriverId ?? string.Empty;
                var address = kv.Value;
                var description = string.Empty;
                if (GpioDeviceParameterSet.TryParsePointValue(
                        kv.Value, device.DriverId, out var parsedDriverId, out var parsedAddress, out var label))
                {
                    driverId = parsedDriverId;
                    address = parsedAddress;
                    description = label;
                }
                else
                {
                    device.Parameters.TryGetValue($"desc.{alias}", out var legacyDesc);
                    description = legacyDesc ?? string.Empty;
                }

                rows.Add(new IoLabelRow
                {
                    DeviceId = device.Id,
                    Alias = alias,
                    Direction = direction,
                    DriverId = driverId,
                    Address = string.Equals(address, "virtual", StringComparison.OrdinalIgnoreCase) ? string.Empty : address,
                    Description = description
                });
            }
        }

        return rows;
    }

    private void ApplyIoLabelRows(List<MdkSetting.DeviceConfig> devices)
    {
        foreach (var device in devices.Where(IsIoDevice))
        {
            var preserved = device.Parameters
                .Where(kv => !kv.Key.StartsWith("in.", StringComparison.OrdinalIgnoreCase)
                             && !kv.Key.StartsWith("out.", StringComparison.OrdinalIgnoreCase)
                             && !kv.Key.StartsWith("desc.", StringComparison.OrdinalIgnoreCase))
                .ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase);
            device.Parameters = preserved;
        }

        var byId = devices.ToDictionary(d => d.Id, StringComparer.OrdinalIgnoreCase);
        foreach (var row in _ioLabelsBinding.List.Cast<IoLabelRow>())
        {
            if (string.IsNullOrWhiteSpace(row.DeviceId) || string.IsNullOrWhiteSpace(row.Alias))
            {
                continue;
            }

            if (!byId.TryGetValue(row.DeviceId.Trim(), out var device) || !IsIoDevice(device))
            {
                throw new InvalidOperationException($"I/O label '{row.Alias}' references unknown I/O device '{row.DeviceId}'.");
            }

            var direction = string.Equals(row.Direction, "out", StringComparison.OrdinalIgnoreCase) ? "out" : "in";
            if (string.Equals(device.Type, "vio", StringComparison.OrdinalIgnoreCase))
            {
                var vioKey = VioDeviceParameterSet.IsUndirectedBitKey(row.Alias.Trim())
                             || string.Equals(row.Direction, "vio", StringComparison.OrdinalIgnoreCase)
                    ? row.Alias.Trim()
                    : $"{direction}.{row.Alias.Trim()}";
                device.Parameters[vioKey] = string.IsNullOrWhiteSpace(row.Description)
                    ? "virtual"
                    : $"virtual|{row.Description.Trim()}";
            }
            else
            {
                var key = $"{direction}.{row.Alias.Trim()}";
                var drv = string.IsNullOrWhiteSpace(row.DriverId) ? device.DriverId : row.DriverId.Trim();
                if (string.IsNullOrWhiteSpace(drv) || string.IsNullOrWhiteSpace(row.Address))
                {
                    throw new InvalidOperationException($"GPIO label '{row.Alias}' must include driver id and address.");
                }

                device.Parameters[key] = GpioDeviceParameterSet.FormatPointValue(
                    drv, row.Address.Trim(), row.Description, device.DriverId);
                if (string.IsNullOrWhiteSpace(device.DriverId))
                {
                    device.DriverId = drv;
                }
            }
        }
    }

    private static bool IsIoDevice(MdkSetting.DeviceConfig device) =>
        string.Equals(device.Type, "gpio", StringComparison.OrdinalIgnoreCase)
        || string.Equals(device.Type, "vio", StringComparison.OrdinalIgnoreCase);

    private static void ValidateSetting(MdkSetting setting)
    {
        ValidateUnique(setting.Drivers.Select(d => d.Id), "driver id");
        ValidateUnique(setting.Devices.Select(d => d.Id), "device id");
        ValidateUnique(setting.Tasks.Select(t => t.Name), "task name");
        ValidateUnique(setting.Recipes.Select(r => r.Id), "recipe id");

        if (!string.IsNullOrWhiteSpace(setting.ActiveRecipeId)
            && !setting.Recipes.Any(r => string.Equals(r.Id, setting.ActiveRecipeId, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException($"Active recipe '{setting.ActiveRecipeId}' was not found in recipes.");
        }

        var recipeKeys = setting.RecipeVarKeys.Count > 0
            ? setting.RecipeVarKeys.ToHashSet(StringComparer.OrdinalIgnoreCase)
            : setting.Recipes.SelectMany(r => r.Vars.Keys).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var recipe in setting.Recipes)
        {
            foreach (var key in recipe.Vars.Keys)
            {
                if (recipeKeys.Count > 0 && !recipeKeys.Contains(key))
                {
                    throw new InvalidOperationException(
                        $"Recipe '{recipe.Id}' uses var '{key}' which is not listed in recipeVarKeys.");
                }
            }
        }

        var driverIds = setting.Drivers.Select(d => d.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var device in setting.Devices.Where(d => d.Enabled && !string.IsNullOrWhiteSpace(d.DriverId)))
        {
            if (!driverIds.Contains(device.DriverId))
            {
                throw new InvalidOperationException($"Device '{device.Id}' references missing driver '{device.DriverId}'.");
            }
        }

        foreach (var task in setting.Tasks.Where(t => !string.IsNullOrWhiteSpace(t.DriverId)))
        {
            if (!driverIds.Contains(task.DriverId))
            {
                throw new InvalidOperationException($"Task '{task.Name}' references missing driver '{task.DriverId}'.");
            }
        }
    }

    private static void ValidateUnique(IEnumerable<string?> values, string label)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var value in values.Select(v => v?.Trim()).Where(v => !string.IsNullOrWhiteSpace(v)))
        {
            if (!seen.Add(value!))
            {
                throw new InvalidOperationException($"Duplicate {label}: {value}");
            }
        }
    }

    private Dictionary<string, object?> BuildVars()
    {
        var vars = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in _varsBinding.List.Cast<VarRow>())
        {
            if (string.IsNullOrWhiteSpace(row.Key))
            {
                continue;
            }

            vars[row.Key.Trim()] = ParseJsonValue(row.ValueJson);
        }

        return vars;
    }

    private static object? ParseJsonValue(string? valueJson)
    {
        if (string.IsNullOrWhiteSpace(valueJson))
        {
            return null;
        }

        return JsonNode.Parse(valueJson) is { } node ? node.Deserialize<object?>() : null;
    }

    private static void ValidateNoBlankKeys(IEnumerable<string?> keys, string label)
    {
        if (keys.Any(string.IsNullOrWhiteSpace))
        {
            throw new InvalidOperationException($"Blank {label} is not allowed.");
        }
    }

    private void ApplyChanges()
    {
        try
        {
            ConfigFormHelpers.SaveSetting(_settingPath, BuildSettingFromRows());
            UpdateStatus();
            MessageBox.Show(this, "Component config saved.", "Info", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Save Failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void ImportSetting()
    {
        try
        {
            var setting = ConfigFormHelpers.ImportObject<MdkSetting>(this);
            if (setting is not null)
            {
                BindSetting(setting);
                RefreshCenterWorkspace();
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Import Failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void ExportSetting()
    {
        try
        {
            ConfigFormHelpers.ExportObject(this, BuildSettingFromRows());
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Export Failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void ImportCurrentTab()
    {
        try
        {
            switch (_currentPage)
            {
                case "Drivers":
                    _driversBinding.DataSource = ConfigFormHelpers.ImportRows<DriverRow>(this);
                    break;
                case "Devices":
                    _devicesBinding.DataSource = ConfigFormHelpers.ImportRows<DeviceRow>(this);
                    break;
                case "Tasks":
                    _tasksBinding.DataSource = ConfigFormHelpers.ImportRows<TaskRow>(this);
                    break;
                case "I/O Labels":
                    _ioLabelsBinding.DataSource = ConfigFormHelpers.ImportRows<IoLabelRow>(this);
                    break;
                case "Vars":
                    _varsBinding.DataSource = ConfigFormHelpers.ImportRows<VarRow>(this);
                    break;
                case "Recipe Keys":
                    _recipeVarKeysBinding.DataSource = ConfigFormHelpers.ImportRows<RecipeVarKeyRow>(this);
                    break;
                case "Recipes":
                    _recipesBinding.DataSource = ConfigFormHelpers.ImportRows<RecipeRow>(this);
                    _activeRecipeCombo.Items.Clear();
                    _activeRecipeCombo.Items.Add(string.Empty);
                    foreach (var row in _recipesBinding.List.Cast<RecipeRow>())
                    {
                        if (!string.IsNullOrWhiteSpace(row.Id))
                        {
                            _activeRecipeCombo.Items.Add(row.Id.Trim());
                        }
                    }

                    _activeRecipeCombo.SelectedIndex = 0;
                    SyncProjectSettingsFromControls();
                    break;
                default:
                    MessageBox.Show(this, "Project / help pages use File → Import Setting for full JSON.", "Info", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
            }

            RefreshCenterWorkspace();
            UpdateStatus();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Import Failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void ExportCurrentTab()
    {
        try
        {
            switch (_currentPage)
            {
                case "Drivers":
                    ConfigFormHelpers.ExportRows(this, _driversBinding.List.Cast<DriverRow>().ToList());
                    break;
                case "Devices":
                    ConfigFormHelpers.ExportRows(this, _devicesBinding.List.Cast<DeviceRow>().ToList());
                    break;
                case "Tasks":
                    ConfigFormHelpers.ExportRows(this, _tasksBinding.List.Cast<TaskRow>().ToList());
                    break;
                case "I/O Labels":
                    ConfigFormHelpers.ExportRows(this, _ioLabelsBinding.List.Cast<IoLabelRow>().ToList());
                    break;
                case "Vars":
                    ConfigFormHelpers.ExportRows(this, _varsBinding.List.Cast<VarRow>().ToList());
                    break;
                case "Recipe Keys":
                    ConfigFormHelpers.ExportRows(this, _recipeVarKeysBinding.List.Cast<RecipeVarKeyRow>().ToList());
                    break;
                case "Recipes":
                    ConfigFormHelpers.ExportRows(this, _recipesBinding.List.Cast<RecipeRow>().ToList());
                    break;
                default:
                    ExportSetting();
                    break;
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Export Failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void BackupSetting()
    {
        try
        {
            var backupPath = Path.Combine(
                Path.GetDirectoryName(_settingPath) ?? Environment.CurrentDirectory,
                $"{Path.GetFileNameWithoutExtension(_settingPath)}.{DateTime.Now:yyyyMMdd-HHmmss}.backup.json");
            ConfigFormHelpers.SaveSetting(backupPath, BuildSettingFromRows());
            MessageBox.Show(this, $"Backup saved:\n{backupPath}", "Info", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Backup Failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void ApplyParameterPreset()
    {
        switch (_shell.PropertyGrid.SelectedObject)
        {
            case DriverRow driver:
                driver.Parameters = GetDriverParameterPreset(driver.Type);
                _driversBinding.ResetBindings(false);
                _shell.PropertyGrid.Refresh();
                break;
            case DeviceRow device:
                device.Parameters = GetDeviceParameterPreset(device.Type);
                _devicesBinding.ResetBindings(false);
                _shell.PropertyGrid.Refresh();
                break;
            case TaskRow task:
                task.Parameters = GetTaskParameterPreset(task.Type);
                _tasksBinding.ResetBindings(false);
                _shell.PropertyGrid.Refresh();
                break;
            default:
                MessageBox.Show(this, "Select a driver, device, or task in the table or diagram first.", "Info", MessageBoxButtons.OK, MessageBoxIcon.Information);
                break;
        }
    }

    private static string GetDriverParameterPreset(string? type)
    {
        var dict = DriverParameterPresets.ForType(type);
        if (dict.Count == 0)
        {
            return "key=value";
        }

        return string.Join("; ", dict.Select(kv => $"{kv.Key}={kv.Value}"));
    }

    private static string GetDeviceParameterPreset(string? type)
    {
        return (type ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "gpio" => "in.startButton=0|启动按钮; in.stopButton=1|停止按钮; out.tower.green=0|绿灯; out.tower.red=1|红灯",
            "vio" => string.Join("; ", VioDeviceParameterSet.DefaultParameters()
                .Select(kv => $"{kv.Key}={kv.Value}")),
            "axis" or "linear" or "lin" or "直线" or "直线轴" =>
                string.Join("; ", AxisDeviceParameterSet.DefaultParameters(MAxisKind.Linear)
                    .Select(kv => $"{kv.Key}={kv.Value}")),
            "rotary" or "rot" or "rotate" or "旋转" or "旋转轴" =>
                string.Join("; ", AxisDeviceParameterSet.DefaultParameters(MAxisKind.Rotary)
                    .Select(kv => $"{kv.Key}={kv.Value}")),
            "platform" => "kind=xyz; model=PlatformXyz; axis.X=drv-m1; axisIndex.X=0; axis.Y=drv-m1; axisIndex.Y=1; axis.Z=drv-m1; axisIndex.Z=2",
            "x" => "kind=x; model=PlatformXyz; axis.X=drv-m1; axisIndex.X=0",
            "xy" => "kind=xy; model=PlatformXyz; axis.X=drv-m1; axisIndex.X=0; axis.Y=drv-m1; axisIndex.Y=1",
            "xyz" => "kind=xyz; model=PlatformXyz; axis.X=drv-m1; axisIndex.X=0; axis.Y=drv-m1; axisIndex.Y=1; axis.Z=drv-m1; axisIndex.Z=2",
            "serial" => "readTimeoutMs=1000; writeTimeoutMs=1000",
            "tcp" => "endpoint=127.0.0.1:502",
            _ => "key=value"
        };
    }

    private static string GetTaskParameterPreset(string? type)
    {
        return (type ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "polldriver" => "varPrefix=driver",
            "operation" => "deviceId=gpio-main",
            "cycle" => "deviceId=gpio-main",
            _ => "key=value"
        };
    }

    private void UpdateStatus()
    {
        _shell.PathLabel.Text = $"Setting: {_settingPath}";
        _shell.ModeLabel.Text = "Mode: Offline";
        _shell.CountsLabel.Text =
            $"Drivers: {_driversBinding.Count}  Devices: {_devicesBinding.Count}  I/O: {_ioLabelsBinding.Count}  Tasks: {_tasksBinding.Count}  Vars: {_varsBinding.Count}  Recipes: {_recipesBinding.Count}";
    }

    private sealed class PageState(string name, BindingSource? binding, DataGridView? grid)
    {
        public string Name { get; } = name;
        public BindingSource? Binding { get; } = binding;
        public DataGridView? Grid { get; } = grid;
    }

    public sealed class ProjectSettings
    {
        [Category("Project")]
        public string ProjectName { get; set; } = "MDKOSS";

        [Category("Project")]
        public int CycleMs { get; set; } = 20;

        [Category("Project")]
        public string MonitoringPrefix { get; set; } = string.Empty;

        [Category("Project")]
        public string ActiveRecipeId { get; set; } = string.Empty;
    }

    private sealed class RowPropertyDialog : Form
    {
        private readonly TableLayoutPanel _layout = new()
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 2,
            Padding = new Padding(14)
        };
        private readonly Button _okButton = new() { Text = "OK", Width = 88, Height = 28 };
        private int _rowIndex;

        public RowPropertyDialog(string title)
        {
            Text = title;
            Width = 520;
            Height = 420;
            MinimizeBox = false;
            MaximizeBox = false;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;

            _layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
            _layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

            var buttons = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                FlowDirection = FlowDirection.RightToLeft,
                Height = 44,
                Padding = new Padding(8)
            };
            var cancelButton = new Button { Text = "Cancel", Width = 88, Height = 28, DialogResult = DialogResult.Cancel };
            buttons.Controls.Add(cancelButton);
            buttons.Controls.Add(_okButton);

            Controls.Add(_layout);
            Controls.Add(buttons);
            AcceptButton = _okButton;
            CancelButton = cancelButton;
        }

        public Func<bool>? ValidateBeforeAccept { get; set; }

        public TextBox AddText(string label, string value = "", bool multiline = false)
        {
            var box = new TextBox
            {
                Text = value,
                Dock = DockStyle.Fill,
                Multiline = multiline,
                ScrollBars = multiline ? ScrollBars.Vertical : ScrollBars.None,
                Height = multiline ? 84 : 24
            };
            AddEditor(label, box);
            return box;
        }

        public ComboBox AddCombo(string label, IReadOnlyCollection<string> values, string value)
        {
            var combo = new ComboBox
            {
                Dock = DockStyle.Fill,
                DropDownStyle = ComboBoxStyle.DropDown
            };
            combo.Items.AddRange(values.Cast<object>().ToArray());
            combo.Text = value;
            AddEditor(label, combo);
            return combo;
        }

        public CheckBox AddCheck(string label, bool value)
        {
            var check = new CheckBox
            {
                Checked = value,
                AutoSize = true
            };
            AddEditor(label, check);
            return check;
        }

        public NumericUpDown AddNumber(string label, int value, int minimum, int maximum)
        {
            var number = new NumericUpDown
            {
                Dock = DockStyle.Left,
                Width = 120,
                Minimum = minimum,
                Maximum = maximum,
                Value = Math.Clamp(value, minimum, maximum)
            };
            AddEditor(label, number);
            return number;
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            _okButton.Click += (_, _) =>
            {
                if (ValidateBeforeAccept?.Invoke() == false)
                {
                    return;
                }

                DialogResult = DialogResult.OK;
                Close();
            };
        }

        private void AddEditor(string label, Control editor)
        {
            var caption = new Label
            {
                Text = label,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                AutoSize = true,
                Margin = new Padding(0, 6, 8, 6)
            };
            editor.Margin = new Padding(0, 6, 0, 6);
            _layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            _layout.Controls.Add(caption, 0, _rowIndex);
            _layout.Controls.Add(editor, 1, _rowIndex);
            _rowIndex++;
        }
    }

    private static class DriverRowDialog
    {
        public static DriverRow? Create(IWin32Window? owner)
        {
            using var dialog = new RowPropertyDialog("New Driver");
            var id = dialog.AddText("Id", "drv-main");
            var type = dialog.AddCombo("Type", ["sim", "gts", "dmc", "vio", "tcp", "serial"], "sim");
            var enabled = dialog.AddCheck("Enabled", true);
            var lastPreset = GetDriverParameterPreset(type.Text);
            var parameters = dialog.AddText("Parameters", lastPreset, multiline: true);
            type.TextChanged += (_, _) =>
            {
                var nextPreset = GetDriverParameterPreset(type.Text);
                if (string.IsNullOrWhiteSpace(parameters.Text)
                    || string.Equals(parameters.Text, lastPreset, StringComparison.OrdinalIgnoreCase))
                {
                    parameters.Text = nextPreset;
                }

                lastPreset = nextPreset;
            };
            dialog.ValidateBeforeAccept = () => RequireText(dialog, id, "Driver id");

            return dialog.ShowDialog(owner) == DialogResult.OK
                ? new DriverRow
                {
                    Id = id.Text.Trim(),
                    Type = string.IsNullOrWhiteSpace(type.Text) ? "sim" : type.Text.Trim(),
                    Enabled = enabled.Checked,
                    Parameters = parameters.Text.Trim()
                }
                : null;
        }
    }

    private static class DeviceRowDialog
    {
        public static DeviceRow? Create(IWin32Window? owner)
        {
            using var dialog = new RowPropertyDialog("New Device");
            var id = dialog.AddText("Id", "device-main");
            var name = dialog.AddText("Name", "Device");
            var type = dialog.AddCombo("Type", ["dev", "platform", "axis", "gpio", "vio", "serial", "tcp", "xy", "xyz"], "axis");
            var driverId = dialog.AddText("DriverId", "drv-main");
            var enabled = dialog.AddCheck("Enabled", true);
            var lastPreset = GetDeviceParameterPreset(type.Text);
            var parameters = dialog.AddText("Parameters", lastPreset, multiline: true);
            type.TextChanged += (_, _) =>
            {
                var nextPreset = GetDeviceParameterPreset(type.Text);
                if (string.IsNullOrWhiteSpace(parameters.Text)
                    || string.Equals(parameters.Text, lastPreset, StringComparison.OrdinalIgnoreCase))
                {
                    parameters.Text = nextPreset;
                }

                lastPreset = nextPreset;
            };
            dialog.ValidateBeforeAccept = () => RequireText(dialog, id, "Device id");

            return dialog.ShowDialog(owner) == DialogResult.OK
                ? new DeviceRow
                {
                    Id = id.Text.Trim(),
                    Name = name.Text.Trim(),
                    Type = string.IsNullOrWhiteSpace(type.Text) ? "axis" : type.Text.Trim(),
                    DriverId = driverId.Text.Trim(),
                    Enabled = enabled.Checked,
                    Parameters = parameters.Text.Trim()
                }
                : null;
        }
    }

    private static class TaskRowDialog
    {
        public static TaskRow? Create(IWin32Window? owner)
        {
            using var dialog = new RowPropertyDialog("New Task");
            var name = dialog.AddText("Name", "task-main");
            var type = dialog.AddCombo("Type", ["pollDriver", "operation", "cycle", "motion"], "pollDriver");
            var driverId = dialog.AddText("DriverId", "drv-main");
            var intervalMs = dialog.AddNumber("IntervalMs", 100, 1, 600000);
            var lastPreset = GetTaskParameterPreset(type.Text);
            var parameters = dialog.AddText("Parameters", lastPreset, multiline: true);
            type.TextChanged += (_, _) =>
            {
                var nextPreset = GetTaskParameterPreset(type.Text);
                if (string.IsNullOrWhiteSpace(parameters.Text)
                    || string.Equals(parameters.Text, lastPreset, StringComparison.OrdinalIgnoreCase))
                {
                    parameters.Text = nextPreset;
                }

                lastPreset = nextPreset;
            };
            dialog.ValidateBeforeAccept = () => RequireText(dialog, name, "Task name");

            return dialog.ShowDialog(owner) == DialogResult.OK
                ? new TaskRow
                {
                    Name = name.Text.Trim(),
                    Type = string.IsNullOrWhiteSpace(type.Text) ? "pollDriver" : type.Text.Trim(),
                    DriverId = driverId.Text.Trim(),
                    IntervalMs = (int)intervalMs.Value,
                    Parameters = parameters.Text.Trim()
                }
                : null;
        }
    }

    private static bool RequireText(IWin32Window owner, TextBox box, string label)
    {
        if (!string.IsNullOrWhiteSpace(box.Text))
        {
            return true;
        }

        MessageBox.Show(owner, $"{label} is required.", "Invalid Config", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        box.Focus();
        return false;
    }

    public sealed class DriverRow
    {
        [Category("Overview")]
        public string Id { get; set; } = string.Empty;
        [Category("Overview")]
        public string Type { get; set; } = "sim";
        [Category("Overview")]
        public bool Enabled { get; set; } = true;
        [Category("Parameters"), DisplayName("Parameters (key=value; …)")]
        public string Parameters { get; set; } = string.Empty;

        public static DriverRow FromConfig(MdkSetting.DriverConfig config) => new()
        {
            Id = config.Id,
            Type = config.Type,
            Enabled = config.Enabled,
            Parameters = ConfigFormHelpers.ParametersToText(config.Parameters)
        };

        public MdkSetting.DriverConfig ToConfig() => new()
        {
            Id = Id.Trim(),
            Type = string.IsNullOrWhiteSpace(Type) ? "sim" : Type.Trim(),
            Enabled = Enabled,
            Parameters = ConfigFormHelpers.ParseParameters(Parameters)
        };

        public override string ToString() => string.IsNullOrWhiteSpace(Id) ? "Driver" : $"Driver:{Id}";
    }

    public sealed class DeviceRow
    {
        [Category("Overview")]
        public string Id { get; set; } = string.Empty;
        [Category("Overview")]
        public string Name { get; set; } = string.Empty;
        [Category("Overview")]
        public string Type { get; set; } = "gpio";
        [Category("Overview")]
        public string DriverId { get; set; } = string.Empty;
        [Category("Overview")]
        public bool Enabled { get; set; } = true;
        [Category("Parameters"), DisplayName("Parameters (key=value; …)")]
        public string Parameters { get; set; } = string.Empty;

        public static DeviceRow FromConfig(MdkSetting.DeviceConfig config) => new()
        {
            Id = config.Id,
            Name = config.Name,
            Type = config.Type,
            DriverId = config.DriverId,
            Enabled = config.Enabled,
            Parameters = ConfigFormHelpers.ParametersToText(config.Parameters)
        };

        public MdkSetting.DeviceConfig ToConfig() => new()
        {
            Id = Id.Trim(),
            Name = Name?.Trim() ?? string.Empty,
            Type = string.IsNullOrWhiteSpace(Type) ? "gpio" : Type.Trim(),
            DriverId = DriverId?.Trim() ?? string.Empty,
            Enabled = Enabled,
            Parameters = ConfigFormHelpers.ParseParameters(Parameters)
        };

        public override string ToString() => string.IsNullOrWhiteSpace(Id) ? "Device" : $"Device:{Id}";
    }

    public sealed class TaskRow
    {
        [Category("Overview")]
        public string Name { get; set; } = string.Empty;
        [Category("Overview")]
        public string Type { get; set; } = "pollDriver";
        [Category("Overview")]
        public string DriverId { get; set; } = string.Empty;
        [Category("Overview")]
        public int IntervalMs { get; set; } = 100;
        [Category("Parameters"), DisplayName("Parameters (key=value; …)")]
        public string Parameters { get; set; } = string.Empty;

        public static TaskRow FromConfig(MdkSetting.TaskConfig config) => new()
        {
            Name = config.Name,
            Type = config.Type,
            DriverId = config.DriverId,
            IntervalMs = config.IntervalMs,
            Parameters = ConfigFormHelpers.ParametersToText(config.Parameters)
        };

        public MdkSetting.TaskConfig ToConfig() => new()
        {
            Name = Name.Trim(),
            Type = string.IsNullOrWhiteSpace(Type) ? "pollDriver" : Type.Trim(),
            DriverId = DriverId?.Trim() ?? string.Empty,
            IntervalMs = Math.Max(1, IntervalMs),
            Parameters = ConfigFormHelpers.ParseParameters(Parameters)
        };

        public override string ToString() => string.IsNullOrWhiteSpace(Name) ? "Task" : $"Task:{Name}";
    }

    public sealed class IoLabelRow
    {
        [Category("Overview")]
        public string DeviceId { get; set; } = string.Empty;
        [Category("Overview")]
        public string Alias { get; set; } = string.Empty;
        [Category("Overview")]
        public string Direction { get; set; } = "in";
        [Category("Overview")]
        public string DriverId { get; set; } = string.Empty;
        [Category("Overview")]
        public string Address { get; set; } = string.Empty;
        [Category("Details")]
        public string Description { get; set; } = string.Empty;

        public override string ToString() => string.IsNullOrWhiteSpace(Alias) ? "I/O Label" : $"IO:{Alias}";
    }

    public sealed class VarRow
    {
        [Category("Overview")]
        public string Key { get; set; } = string.Empty;
        [Category("Overview"), DisplayName("Value JSON")]
        public string ValueJson { get; set; } = "null";

        public static VarRow FromValue(KeyValuePair<string, object?> value) => new()
        {
            Key = value.Key,
            ValueJson = JsonSerializer.Serialize(value.Value, new JsonSerializerOptions { WriteIndented = false })
        };

        public override string ToString() => string.IsNullOrWhiteSpace(Key) ? "Var" : $"Var:{Key}";
    }

    public sealed class RecipeVarKeyRow
    {
        [Category("Overview")]
        public string Key { get; set; } = string.Empty;

        public override string ToString() => string.IsNullOrWhiteSpace(Key) ? "Recipe Key" : $"Key:{Key}";
    }

    public sealed class RecipeRow
    {
        [Category("Overview")]
        public string Id { get; set; } = string.Empty;
        [Category("Overview")]
        public string Name { get; set; } = string.Empty;
        [Category("Overview")]
        public string Description { get; set; } = string.Empty;
        [Category("Details"), DisplayName("Vars JSON")]
        public string VarsJson { get; set; } = "{}";

        public static RecipeRow FromConfig(MdkSetting.RecipeConfig config) => new()
        {
            Id = config.Id,
            Name = config.Name,
            Description = config.Description ?? string.Empty,
            VarsJson = JsonSerializer.Serialize(config.Vars, new JsonSerializerOptions { WriteIndented = true })
        };

        public MdkSetting.RecipeConfig ToConfig()
        {
            Dictionary<string, object?> vars;
            if (string.IsNullOrWhiteSpace(VarsJson) || string.Equals(VarsJson.Trim(), "{}", StringComparison.Ordinal))
            {
                vars = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            }
            else if (JsonNode.Parse(VarsJson) is not JsonObject obj)
            {
                throw new InvalidOperationException($"Recipe '{Id}' vars JSON must be a JSON object.");
            }
            else
            {
                vars = obj.Deserialize<Dictionary<string, object?>>(new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                }) ?? new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            }

            return new MdkSetting.RecipeConfig
            {
                Id = Id.Trim(),
                Name = Name.Trim(),
                Description = string.IsNullOrWhiteSpace(Description) ? null : Description.Trim(),
                Vars = vars
            };
        }

        public override string ToString() => string.IsNullOrWhiteSpace(Id) ? "Recipe" : $"Recipe:{Id}";
    }
}
