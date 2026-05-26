using MDKOSS.Core;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace MDKOSS.Gui;

public sealed class ComponentConfigForm : Form
{
    private readonly string _settingPath;
    private readonly BindingSource _driversBinding = new();
    private readonly BindingSource _devicesBinding = new();
    private readonly BindingSource _tasksBinding = new();
    private readonly BindingSource _varsBinding = new();
    private readonly BindingSource _ioLabelsBinding = new();
    private readonly TreeView _navigationTree = new() { Dock = DockStyle.Fill, HideSelection = false };
    private readonly TabControl _tabs = new() { Dock = DockStyle.Fill };
    private readonly ToolStripStatusLabel _settingStatusLabel = new();
    private readonly ToolStripStatusLabel _countsStatusLabel = new();
    private readonly TextBox _projectNameBox = new() { Width = 260 };
    private readonly NumericUpDown _cycleMsBox = new() { Width = 100, Minimum = 1, Maximum = 600000, Value = 20 };
    private readonly TextBox _monitoringPrefixBox = new() { Width = 360 };

    public ComponentConfigForm(string settingPath)
    {
        _settingPath = settingPath;
        Text = "Component Config Manager";
        Width = 1180;
        Height = 720;
        MinimumSize = new Size(900, 560);
        StartPosition = FormStartPosition.CenterParent;

        var toolbar = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 44,
            Padding = new Padding(8, 8, 8, 4),
            WrapContents = false
        };

        var btnReload = CreateButton("Reload", 90);
        var btnImportSetting = CreateButton("Import Setting", 120);
        var btnExportSetting = CreateButton("Export Setting", 120);
        var btnImportTab = CreateButton("Import Tab", 110);
        var btnExportTab = CreateButton("Export Tab", 110);
        var btnBackup = CreateButton("Backup", 90);
        var btnPreset = CreateButton("Param Preset", 110);
        var btnApply = CreateButton("Apply", 90);
        var btnClose = CreateButton("Close", 90);
        toolbar.Controls.AddRange([btnReload, btnImportSetting, btnExportSetting, btnImportTab, btnExportTab, btnBackup, btnPreset, btnApply, btnClose]);

        AddConfigurationPages();
        BuildNavigationTree();

        var split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical
        };
        split.Panel1.Controls.Add(CreateNavigationPanel());
        split.Panel2.Controls.Add(_tabs);

        var status = new StatusStrip();
        status.Items.Add(_settingStatusLabel);
        status.Items.Add(new ToolStripStatusLabel { Spring = true });
        status.Items.Add(_countsStatusLabel);

        Controls.Add(split);
        Controls.Add(status);
        Controls.Add(toolbar);

        btnReload.Click += (_, _) => LoadSetting();
        btnImportSetting.Click += (_, _) => ImportSetting();
        btnExportSetting.Click += (_, _) => ExportSetting();
        btnImportTab.Click += (_, _) => ImportCurrentTab();
        btnExportTab.Click += (_, _) => ExportCurrentTab();
        btnBackup.Click += (_, _) => BackupSetting();
        btnPreset.Click += (_, _) => ApplyParameterPreset();
        btnApply.Click += (_, _) => ApplyChanges();
        btnClose.Click += (_, _) => Close();
        _navigationTree.AfterSelect += (_, e) => SelectPage(e.Node?.Tag as string);
        _driversBinding.ListChanged += (_, _) => UpdateStatus();
        _devicesBinding.ListChanged += (_, _) => UpdateStatus();
        _tasksBinding.ListChanged += (_, _) => UpdateStatus();
        _varsBinding.ListChanged += (_, _) => UpdateStatus();
        _ioLabelsBinding.ListChanged += (_, _) => UpdateStatus();
        Shown += (_, _) => SetSafeNavigationWidth(split);
        split.SizeChanged += (_, _) => SetSafeNavigationWidth(split);

        LoadSetting();
    }

    private static Button CreateButton(string text, int width) => new() { Text = text, Width = width, Height = 28 };

    private static void SetSafeNavigationWidth(SplitContainer split)
    {
        const int panel1Min = 120;
        const int panel2Min = 260;

        if (split.Width <= panel1Min + panel2Min)
        {
            return;
        }

        var distance = Math.Clamp(210, panel1Min, split.Width - panel2Min);
        if (split.SplitterDistance != distance)
        {
            split.SplitterDistance = distance;
        }
    }

    private void AddConfigurationPages()
    {
        _tabs.TabPages.Add(CreateProjectPage());
        _tabs.TabPages.Add(CreateGridPage("Drivers", _driversBinding, CreateDriverGrid()));
        _tabs.TabPages.Add(CreateGridPage("Devices", _devicesBinding, CreateDeviceGrid()));
        _tabs.TabPages.Add(CreateGridPage("I/O Labels", _ioLabelsBinding, CreateIoLabelGrid()));
        _tabs.TabPages.Add(CreateGridPage("Tasks", _tasksBinding, CreateTaskGrid()));
        _tabs.TabPages.Add(CreateGridPage("Vars", _varsBinding, CreateVarsGrid()));
    }

    private Control CreateNavigationPanel()
    {
        var group = new GroupBox
        {
            Text = "Configuration",
            Dock = DockStyle.Fill,
            Padding = new Padding(8)
        };
        group.Controls.Add(_navigationTree);
        return group;
    }

    private void BuildNavigationTree()
    {
        _navigationTree.Nodes.Clear();
        var project = CreateNavigationNode("Project", "Project");
        var components = CreateNavigationNode("Components", "Drivers");
        components.Nodes.Add(CreateNavigationNode("Drivers", "Drivers"));
        components.Nodes.Add(CreateNavigationNode("Devices", "Devices"));
        components.Nodes.Add(CreateNavigationNode("I/O Labels", "I/O Labels"));
        components.Nodes.Add(CreateNavigationNode("Tasks", "Tasks"));
        components.Nodes.Add(CreateNavigationNode("Variables", "Vars"));
        var exchange = CreateNavigationNode("Import / Export", "Project");

        _navigationTree.Nodes.Add(project);
        _navigationTree.Nodes.Add(components);
        _navigationTree.Nodes.Add(exchange);
        _navigationTree.ExpandAll();
        _navigationTree.SelectedNode = project;
    }

    private static TreeNode CreateNavigationNode(string text, string pageName) => new(text) { Tag = pageName };

    private void SelectPage(string? pageName)
    {
        if (string.IsNullOrWhiteSpace(pageName))
        {
            return;
        }

        foreach (TabPage page in _tabs.TabPages)
        {
            if (string.Equals(page.Text, pageName, StringComparison.OrdinalIgnoreCase))
            {
                _tabs.SelectedTab = page;
                return;
            }
        }
    }

    private TabPage CreateProjectPage()
    {
        var page = new TabPage("Project");
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            Padding = new Padding(14),
            AutoSize = true,
            ColumnCount = 2,
            RowCount = 3
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        AddProjectRow(layout, 0, "Project Name", _projectNameBox);
        AddProjectRow(layout, 1, "Cycle Ms", _cycleMsBox);
        AddProjectRow(layout, 2, "Monitoring Prefix", _monitoringPrefixBox);
        page.Controls.Add(layout);
        return page;
    }

    private static void AddProjectRow(TableLayoutPanel layout, int row, string label, Control editor)
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
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(caption, 0, row);
        layout.Controls.Add(editor, 1, row);
    }

    private static TabPage CreateGridPage(string title, BindingSource binding, DataGridView grid)
    {
        var page = new TabPage(title);
        var topPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 38,
            Padding = new Padding(8, 6, 8, 2),
            WrapContents = false
        };

        var btnAdd = CreateButton("Add", 72);
        var btnDelete = CreateButton("Delete", 72);
        topPanel.Controls.AddRange([btnAdd, btnDelete]);

        btnAdd.Click += (_, _) => binding.AddNew();
        btnDelete.Click += (_, _) =>
        {
            foreach (DataGridViewRow row in grid.SelectedRows)
            {
                if (!row.IsNewRow)
                {
                    grid.Rows.Remove(row);
                }
            }
        };

        grid.DataSource = binding;
        page.Controls.Add(grid);
        page.Controls.Add(topPanel);
        return page;
    }

    private static DataGridView CreateDriverGrid()
    {
        var grid = CreateGrid();
        grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(DriverRow.Id), HeaderText = "Id", Width = 160 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(DriverRow.Type), HeaderText = "Type", Width = 140 });
        grid.Columns.Add(new DataGridViewCheckBoxColumn { DataPropertyName = nameof(DriverRow.Enabled), HeaderText = "Enabled", Width = 80 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(DriverRow.Parameters), HeaderText = "Parameters (key=value; key2=value2)", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill });
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
        grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(DeviceRow.Parameters), HeaderText = "Parameters (key=value; key2=value2)", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill });
        return grid;
    }

    private static DataGridView CreateTaskGrid()
    {
        var grid = CreateGrid();
        grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(TaskRow.Name), HeaderText = "Name", Width = 170 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(TaskRow.Type), HeaderText = "Type", Width = 140 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(TaskRow.DriverId), HeaderText = "DriverId", Width = 140 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(TaskRow.IntervalMs), HeaderText = "IntervalMs", Width = 100 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(TaskRow.Parameters), HeaderText = "Parameters (key=value; key2=value2)", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill });
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
        grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(IoLabelRow.Description), HeaderText = "Description", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill });
        return grid;
    }

    private static DataGridView CreateVarsGrid()
    {
        var grid = CreateGrid();
        grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(VarRow.Key), HeaderText = "Key", Width = 220 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(VarRow.ValueJson), HeaderText = "Value JSON", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill });
        return grid;
    }

    private static DataGridView CreateGrid() => new()
    {
        Dock = DockStyle.Fill,
        AutoGenerateColumns = false,
        AllowUserToAddRows = true,
        AllowUserToDeleteRows = true,
        SelectionMode = DataGridViewSelectionMode.FullRowSelect,
        MultiSelect = true,
        AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.DisplayedCells,
        RowHeadersWidth = 28
    };

    private void LoadSetting()
    {
        try
        {
            BindSetting(ConfigFormHelpers.LoadSetting(_settingPath));
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
        UpdateStatus();
    }

    private MdkSetting BuildSettingFromRows()
    {
        ValidateNoBlankKeys(_driversBinding.List.Cast<DriverRow>().Select(r => r.Id), "driver id");
        ValidateNoBlankKeys(_devicesBinding.List.Cast<DeviceRow>().Select(r => r.Id), "device id");
        ValidateNoBlankKeys(_tasksBinding.List.Cast<TaskRow>().Select(r => r.Name), "task name");
        ValidateNoBlankKeys(_varsBinding.List.Cast<VarRow>().Select(r => r.Key), "var key");

        var setting = new MdkSetting
        {
            ProjectName = string.IsNullOrWhiteSpace(_projectNameBox.Text) ? "MDKOSS" : _projectNameBox.Text.Trim(),
            CycleMs = (int)_cycleMsBox.Value,
            MonitoringPrefix = string.IsNullOrWhiteSpace(_monitoringPrefixBox.Text) ? null : _monitoringPrefixBox.Text.Trim(),
            Drivers = _driversBinding.List.Cast<DriverRow>().Select(r => r.ToConfig()).ToList(),
            Devices = _devicesBinding.List.Cast<DeviceRow>().Select(r => r.ToConfig()).ToList(),
            Tasks = _tasksBinding.List.Cast<TaskRow>().Select(r => r.ToConfig()).ToList(),
            Vars = BuildVars()
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
                var driverId = string.Empty;
                var address = kv.Value;
                if (GpioDeviceParameterSet.TryParsePointRoute(kv.Value, out var parsedDriverId, out var parsedAddress))
                {
                    driverId = parsedDriverId;
                    address = parsedAddress;
                }

                device.Parameters.TryGetValue($"desc.{alias}", out var description);
                rows.Add(new IoLabelRow
                {
                    DeviceId = device.Id,
                    Alias = alias,
                    Direction = direction,
                    DriverId = driverId,
                    Address = string.Equals(address, "virtual", StringComparison.OrdinalIgnoreCase) ? string.Empty : address,
                    Description = description ?? string.Empty
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
            var key = $"{direction}.{row.Alias.Trim()}";
            if (string.Equals(device.Type, "vio", StringComparison.OrdinalIgnoreCase))
            {
                device.Parameters[key] = "virtual";
            }
            else
            {
                if (string.IsNullOrWhiteSpace(row.DriverId) || string.IsNullOrWhiteSpace(row.Address))
                {
                    throw new InvalidOperationException($"GPIO label '{row.Alias}' must include driver id and address.");
                }

                device.Parameters[key] = $"{row.DriverId.Trim()}:{row.Address.Trim()}";
            }

            if (!string.IsNullOrWhiteSpace(row.Description))
            {
                device.Parameters[$"desc.{row.Alias.Trim()}"] = row.Description.Trim();
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
            switch (_tabs.SelectedTab?.Text)
            {
                case "Drivers":
                    _driversBinding.DataSource = ConfigFormHelpers.ImportRows<DriverRow>(this);
                    UpdateStatus();
                    break;
                case "Devices":
                    _devicesBinding.DataSource = ConfigFormHelpers.ImportRows<DeviceRow>(this);
                    UpdateStatus();
                    break;
                case "Tasks":
                    _tasksBinding.DataSource = ConfigFormHelpers.ImportRows<TaskRow>(this);
                    UpdateStatus();
                    break;
                case "I/O Labels":
                    _ioLabelsBinding.DataSource = ConfigFormHelpers.ImportRows<IoLabelRow>(this);
                    UpdateStatus();
                    break;
                case "Vars":
                    _varsBinding.DataSource = ConfigFormHelpers.ImportRows<VarRow>(this);
                    UpdateStatus();
                    break;
                default:
                    MessageBox.Show(this, "Project tab is exported with the full setting JSON.", "Info", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    break;
            }
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
            switch (_tabs.SelectedTab?.Text)
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
        switch (_tabs.SelectedTab?.Text)
        {
            case "Drivers" when _driversBinding.Current is DriverRow driver:
                driver.Parameters = GetDriverParameterPreset(driver.Type);
                _driversBinding.ResetCurrentItem();
                break;
            case "Devices" when _devicesBinding.Current is DeviceRow device:
                device.Parameters = GetDeviceParameterPreset(device.Type);
                _devicesBinding.ResetCurrentItem();
                break;
            case "Tasks" when _tasksBinding.Current is TaskRow task:
                task.Parameters = GetTaskParameterPreset(task.Type);
                _tasksBinding.ResetCurrentItem();
                break;
            default:
                MessageBox.Show(this, "Select a driver, device, or task row before applying a parameter preset.", "Info", MessageBoxButtons.OK, MessageBoxIcon.Information);
                break;
        }
    }

    private static string GetDriverParameterPreset(string? type)
    {
        return (type ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "tcp" => "host=127.0.0.1; port=502",
            "serial" => "port=COM1; baudRate=115200; parity=None; dataBits=8; stopBits=One",
            "gts" => "card=0",
            "sim" => "connect=true",
            _ => "key=value"
        };
    }

    private static string GetDeviceParameterPreset(string? type)
    {
        return (type ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "gpio" => "driverIds=drv-main; in.ready=drv-main:0; out.start=drv-main:0",
            "vio" => "in.ready=virtual; out.start=virtual",
            "axis" => "axis=0",
            "platform" => "kind=xyz; axis.X=axis-x; axis.Y=axis-y; axis.Z=axis-z",
            "xy" => "axis.X=axis-x; axis.Y=axis-y",
            "xyz" => "axis.X=axis-x; axis.Y=axis-y; axis.Z=axis-z",
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
        _settingStatusLabel.Text = $"Setting: {_settingPath}";
        _countsStatusLabel.Text =
            $"Drivers: {_driversBinding.Count}  Devices: {_devicesBinding.Count}  I/O: {_ioLabelsBinding.Count}  Tasks: {_tasksBinding.Count}  Vars: {_varsBinding.Count}";
    }

    public sealed class DriverRow
    {
        public string Id { get; set; } = string.Empty;
        public string Type { get; set; } = "sim";
        public bool Enabled { get; set; } = true;
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
    }

    public sealed class DeviceRow
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Type { get; set; } = "gpio";
        public string DriverId { get; set; } = string.Empty;
        public bool Enabled { get; set; } = true;
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
    }

    public sealed class TaskRow
    {
        public string Name { get; set; } = string.Empty;
        public string Type { get; set; } = "pollDriver";
        public string DriverId { get; set; } = string.Empty;
        public int IntervalMs { get; set; } = 100;
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
    }

    public sealed class IoLabelRow
    {
        public string DeviceId { get; set; } = string.Empty;
        public string Alias { get; set; } = string.Empty;
        public string Direction { get; set; } = "in";
        public string DriverId { get; set; } = string.Empty;
        public string Address { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
    }

    public sealed class VarRow
    {
        public string Key { get; set; } = string.Empty;
        public string ValueJson { get; set; } = "null";

        public static VarRow FromValue(KeyValuePair<string, object?> value) => new()
        {
            Key = value.Key,
            ValueJson = JsonSerializer.Serialize(value.Value, new JsonSerializerOptions { WriteIndented = false })
        };
    }
}
