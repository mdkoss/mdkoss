using MDKOSS.Core;

namespace MDKOSS.Gui;

public sealed class GpioConfigForm : Form
{
    private readonly string _settingPath;
    private readonly BindingSource _binding = new();
    private readonly DataGridView _grid = new() { Dock = DockStyle.Fill, AutoGenerateColumns = true };

    public GpioConfigForm(string settingPath)
    {
        _settingPath = settingPath;
        Text = "GPIO Config Manager";
        Width = 900;
        Height = 600;
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

        btnImport.Click += (_, _) => ImportRows();
        btnExport.Click += (_, _) => ExportRows();
        btnApply.Click += (_, _) => ApplyChanges();
        btnClose.Click += (_, _) => Close();

        LoadRows();
    }

    private void LoadRows()
    {
        var setting = ConfigFormHelpers.LoadSetting(_settingPath);
        var rows = setting.Devices
            .Where(d => d.Type.Equals("gpio", StringComparison.OrdinalIgnoreCase))
            .Select(d => new DeviceRow(d.Id, d.Name, d.DriverId, d.Enabled, ConfigFormHelpers.ParametersToText(d.Parameters)))
            .ToList();
        _binding.DataSource = rows;
    }

    private void ImportRows()
    {
        _binding.DataSource = ConfigFormHelpers.ImportRows<DeviceRow>(this);
    }

    private void ExportRows()
    {
        var rows = ((IEnumerable<DeviceRow>)_binding.List.Cast<DeviceRow>()).ToList();
        ConfigFormHelpers.ExportRows(this, rows);
    }

    private void ApplyChanges()
    {
        var setting = ConfigFormHelpers.LoadSetting(_settingPath);
        var keep = setting.Devices.Where(d => !d.Type.Equals("gpio", StringComparison.OrdinalIgnoreCase)).ToList();
        var rows = ((IEnumerable<DeviceRow>)_binding.List.Cast<DeviceRow>()).ToList();

        var newItems = rows.Select(r => new MdkSetting.DeviceConfig
        {
            Id = r.Id ?? string.Empty,
            Name = r.Name ?? string.Empty,
            DriverId = r.DriverId ?? string.Empty,
            Type = "gpio",
            Enabled = r.Enabled,
            Parameters = ConfigFormHelpers.ParseParameters(r.Parameters)
        });

        setting.Devices = keep.Concat(newItems).ToList();
        ConfigFormHelpers.SaveSetting(_settingPath, setting);
        MessageBox.Show(this, "GPIO config saved.", "Info", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    public sealed class DeviceRow
    {
        public DeviceRow()
        {
        }

        public DeviceRow(string id, string name, string driverId, bool enabled, string parameters)
        {
            Id = id;
            Name = name;
            DriverId = driverId;
            Enabled = enabled;
            Parameters = parameters;
        }

        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string DriverId { get; set; } = string.Empty;
        public bool Enabled { get; set; } = true;
        public string Parameters { get; set; } = string.Empty;
    }
}
