using MDKOSS.Core;

namespace MDKOSS.Gui;

public sealed class TasksConfigForm : Form
{
    private readonly string _settingPath;
    private readonly BindingSource _binding = new();
    private readonly DataGridView _grid = new() { Dock = DockStyle.Fill, AutoGenerateColumns = true };

    public TasksConfigForm(string settingPath)
    {
        _settingPath = settingPath;
        Text = "Tasks Config Manager";
        Width = 980;
        Height = 620;
        StartPosition = FormStartPosition.CenterParent;

        var panel = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 40 };
        var btnImport = new Button { Text = "Import", Width = 90 };
        var btnExport = new Button { Text = "Export", Width = 90 };
        var btnApply = new Button { Text = "Apply", Width = 90 };
        var btnClose = new Button { Text = "Close", Width = 90 };
        panel.Controls.AddRange([btnImport, btnExport, btnApply, btnClose]);

        Controls.Add(_grid);
        Controls.Add(panel);
        _grid.DataSource = _binding;

        btnImport.Click += (_, _) => _binding.DataSource = ConfigFormHelpers.ImportRows<TaskRow>(this);
        btnExport.Click += (_, _) => ConfigFormHelpers.ExportRows(this, _binding.List.Cast<TaskRow>().ToList());
        btnApply.Click += (_, _) => ApplyChanges();
        btnClose.Click += (_, _) => Close();

        LoadRows();
    }

    private void LoadRows()
    {
        var setting = ConfigFormHelpers.LoadSetting(_settingPath);
        _binding.DataSource = setting.Tasks
            .Select(t => new TaskRow(
                t.Name,
                t.Type,
                t.DriverId,
                t.IntervalMs,
                ConfigFormHelpers.ParametersToText(t.Parameters)))
            .ToList();
    }

    private void ApplyChanges()
    {
        var setting = ConfigFormHelpers.LoadSetting(_settingPath);
        var rows = _binding.List.Cast<TaskRow>().ToList();
        setting.Tasks = rows.Select(r => new MdkSetting.TaskConfig
        {
            Name = r.Name ?? string.Empty,
            Type = string.IsNullOrWhiteSpace(r.Type) ? "pollDriver" : r.Type.Trim(),
            DriverId = r.DriverId ?? string.Empty,
            IntervalMs = Math.Max(1, r.IntervalMs),
            Parameters = ConfigFormHelpers.ParseParameters(r.Parameters)
        }).ToList();

        ConfigFormHelpers.SaveSetting(_settingPath, setting);
        MessageBox.Show(this, "Tasks config saved.", "Info", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    public sealed class TaskRow
    {
        public TaskRow()
        {
        }

        public TaskRow(string name, string type, string driverId, int intervalMs, string parameters)
        {
            Name = name;
            Type = type;
            DriverId = driverId;
            IntervalMs = intervalMs;
            Parameters = parameters;
        }

        public string Name { get; set; } = string.Empty;
        public string Type { get; set; } = "pollDriver";
        public string DriverId { get; set; } = string.Empty;
        public int IntervalMs { get; set; } = 100;
        public string Parameters { get; set; } = string.Empty;
    }
}
