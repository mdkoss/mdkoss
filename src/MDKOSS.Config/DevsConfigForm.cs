using MDKOSS.Core;

namespace MDKOSS.Gui;

public sealed class DevsConfigForm : Form
{
    private readonly string _settingPath;
    private readonly BindingSource _binding = new();
    private readonly DataGridView _grid = new() { Dock = DockStyle.Fill, AutoGenerateColumns = true };
    private static readonly string[] ReservedTypes = ["gpio", "axis", "platform"];

    public DevsConfigForm(string settingPath)
    {
        _settingPath = settingPath;
        Text = "Devs Config Manager";
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

        btnImport.Click += (_, _) => _binding.DataSource = ConfigFormHelpers.ImportRows<DeviceRow>(this);
        btnExport.Click += (_, _) => ConfigFormHelpers.ExportRows(this, _binding.List.Cast<DeviceRow>().ToList());
        btnApply.Click += (_, _) => ApplyChanges();
        btnClose.Click += (_, _) => Close();

        LoadRows();
    }

    private void LoadRows()
    {
        var setting = ConfigFormHelpers.LoadSetting(_settingPath);
        _binding.DataSource = setting.Devices
            .Where(d => !ReservedTypes.Contains(d.Type, StringComparer.OrdinalIgnoreCase))
            .Select(d => new DeviceRow(d.Id, d.Name, d.Type, d.DriverId, d.Enabled, ConfigFormHelpers.ParametersToText(d.Parameters)))
            .ToList();
    }

    private void ApplyChanges()
    {
        var setting = ConfigFormHelpers.LoadSetting(_settingPath);
        var keep = setting.Devices.Where(d => ReservedTypes.Contains(d.Type, StringComparer.OrdinalIgnoreCase)).ToList();
        var rows = _binding.List.Cast<DeviceRow>().ToList();

        var items = rows.Select(r => new MdkSetting.DeviceConfig
        {
            Id = r.Id ?? string.Empty,
            Name = r.Name ?? string.Empty,
            Type = string.IsNullOrWhiteSpace(r.Type) ? "generic" : r.Type.Trim(),
            DriverId = r.DriverId ?? string.Empty,
            Enabled = r.Enabled,
            Parameters = ConfigFormHelpers.ParseParameters(r.Parameters)
        });

        setting.Devices = keep.Concat(items).ToList();
        ConfigFormHelpers.SaveSetting(_settingPath, setting);
        MessageBox.Show(this, "Devs config saved.", "Info", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    public sealed class DeviceRow
    {
        public DeviceRow()
        {
        }

        public DeviceRow(string id, string name, string type, string driverId, bool enabled, string parameters)
        {
            Id = id;
            Name = name;
            Type = type;
            DriverId = driverId;
            Enabled = enabled;
            Parameters = parameters;
        }

        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Type { get; set; } = "generic";
        public string DriverId { get; set; } = string.Empty;
        public bool Enabled { get; set; } = true;
        public string Parameters { get; set; } = string.Empty;
    }
}
