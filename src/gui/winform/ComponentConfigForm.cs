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
    private readonly TabControl _tabs = new() { Dock = DockStyle.Fill };
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
        var btnApply = CreateButton("Apply", 90);
        var btnClose = CreateButton("Close", 90);
        toolbar.Controls.AddRange([btnReload, btnImportSetting, btnExportSetting, btnImportTab, btnExportTab, btnApply, btnClose]);

        _tabs.TabPages.Add(CreateProjectPage());
        _tabs.TabPages.Add(CreateGridPage("Drivers", _driversBinding, CreateDriverGrid()));
        _tabs.TabPages.Add(CreateGridPage("Devices", _devicesBinding, CreateDeviceGrid()));
        _tabs.TabPages.Add(CreateGridPage("Tasks", _tasksBinding, CreateTaskGrid()));
        _tabs.TabPages.Add(CreateGridPage("Vars", _varsBinding, CreateVarsGrid()));

        Controls.Add(_tabs);
        Controls.Add(toolbar);

        btnReload.Click += (_, _) => LoadSetting();
        btnImportSetting.Click += (_, _) => ImportSetting();
        btnExportSetting.Click += (_, _) => ExportSetting();
        btnImportTab.Click += (_, _) => ImportCurrentTab();
        btnExportTab.Click += (_, _) => ExportCurrentTab();
        btnApply.Click += (_, _) => ApplyChanges();
        btnClose.Click += (_, _) => Close();

        LoadSetting();
    }

    private static Button CreateButton(string text, int width) => new() { Text = text, Width = width, Height = 28 };

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
        _tasksBinding.DataSource = setting.Tasks.Select(TaskRow.FromConfig).ToList();
        _varsBinding.DataSource = setting.Vars.Select(VarRow.FromValue).OrderBy(v => v.Key, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private MdkSetting BuildSettingFromRows()
    {
        ValidateNoBlankKeys(_driversBinding.List.Cast<DriverRow>().Select(r => r.Id), "driver id");
        ValidateNoBlankKeys(_devicesBinding.List.Cast<DeviceRow>().Select(r => r.Id), "device id");
        ValidateNoBlankKeys(_tasksBinding.List.Cast<TaskRow>().Select(r => r.Name), "task name");
        ValidateNoBlankKeys(_varsBinding.List.Cast<VarRow>().Select(r => r.Key), "var key");

        return new MdkSetting
        {
            ProjectName = string.IsNullOrWhiteSpace(_projectNameBox.Text) ? "MDKOSS" : _projectNameBox.Text.Trim(),
            CycleMs = (int)_cycleMsBox.Value,
            MonitoringPrefix = string.IsNullOrWhiteSpace(_monitoringPrefixBox.Text) ? null : _monitoringPrefixBox.Text.Trim(),
            Drivers = _driversBinding.List.Cast<DriverRow>().Select(r => r.ToConfig()).ToList(),
            Devices = _devicesBinding.List.Cast<DeviceRow>().Select(r => r.ToConfig()).ToList(),
            Tasks = _tasksBinding.List.Cast<TaskRow>().Select(r => r.ToConfig()).ToList(),
            Vars = BuildVars()
        };
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
                    break;
                case "Devices":
                    _devicesBinding.DataSource = ConfigFormHelpers.ImportRows<DeviceRow>(this);
                    break;
                case "Tasks":
                    _tasksBinding.DataSource = ConfigFormHelpers.ImportRows<TaskRow>(this);
                    break;
                case "Vars":
                    _varsBinding.DataSource = ConfigFormHelpers.ImportRows<VarRow>(this);
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
