using MDKOSS.Core;
using System.Text.Json;

namespace MDKOSS.Gui;

public sealed class MainForm : Form
{
    private readonly TextBox _settingPathBox = new() { Dock = DockStyle.Top };
    private readonly Button _browseButton = new() { Text = "Browse Setting", Width = 120 };
    private readonly Button _gpioCfgButton = new() { Text = "GPIO Config", Width = 100 };
    private readonly Button _axisCfgButton = new() { Text = "Axis Config", Width = 100 };
    private readonly Button _platformCfgButton = new() { Text = "Platform Config", Width = 110 };
    private readonly Button _devsCfgButton = new() { Text = "Devs Config", Width = 100 };
    private readonly Button _tasksCfgButton = new() { Text = "Tasks Config", Width = 100 };
    private readonly Label _statusLabel = new() { AutoSize = true, Text = "Status: Stopped" };
    private readonly Label _projectLabel = new() { AutoSize = true, Text = "Project: -" };
    private readonly DataGridView _driverGrid = new() { Dock = DockStyle.Fill, ReadOnly = true, AutoGenerateColumns = true };
    private readonly DataGridView _deviceGrid = new() { Dock = DockStyle.Fill, ReadOnly = true, AutoGenerateColumns = true };
    private readonly TextBox _varsBox = new() { Dock = DockStyle.Fill, Multiline = true, ScrollBars = ScrollBars.Both, ReadOnly = true };
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 1000 };

    private readonly MdkRuntime _runtime;

    public MainForm(MdkRuntime runtime, string settingPath)
    {
        _runtime = runtime;
        Text = "MDKOSS WinForms Monitor";
        Width = 1100;
        Height = 760;
        StartPosition = FormStartPosition.CenterScreen;

        var topPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 40,
            Padding = new Padding(8),
            AutoSize = false
        };
        topPanel.Controls.Add(_browseButton);
        topPanel.Controls.Add(_gpioCfgButton);
        topPanel.Controls.Add(_axisCfgButton);
        topPanel.Controls.Add(_platformCfgButton);
        topPanel.Controls.Add(_devsCfgButton);
        topPanel.Controls.Add(_tasksCfgButton);
        topPanel.Controls.Add(_statusLabel);
        topPanel.Controls.Add(_projectLabel);

        _settingPathBox.Text = settingPath;

        var splitMain = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Horizontal,
            SplitterDistance = 360
        };

        var splitTop = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical,
            SplitterDistance = 520
        };
        splitTop.Panel1.Controls.Add(CreateGroup("Drivers", _driverGrid));
        splitTop.Panel2.Controls.Add(CreateGroup("Devices", _deviceGrid));
        splitMain.Panel1.Controls.Add(splitTop);
        splitMain.Panel2.Controls.Add(CreateGroup("Vars Snapshot", _varsBox));

        Controls.Add(splitMain);
        Controls.Add(_settingPathBox);
        Controls.Add(topPanel);

        _browseButton.Click += (_, _) => BrowseSetting();
        _gpioCfgButton.Click += (_, _) => OpenConfigForm(path => new GpioConfigForm(path));
        _axisCfgButton.Click += (_, _) => OpenConfigForm(path => new AxisConfigForm(path));
        _platformCfgButton.Click += (_, _) => OpenConfigForm(path => new PlatformConfigForm(path));
        _devsCfgButton.Click += (_, _) => OpenConfigForm(path => new DevsConfigForm(path));
        _tasksCfgButton.Click += (_, _) => OpenConfigForm(path => new TasksConfigForm(path));
        _timer.Tick += (_, _) => RefreshSnapshot();
        _timer.Start();
        _statusLabel.Text = "Status: Running";
        RefreshSnapshot();
    }

    private static Control CreateGroup(string title, Control body)
    {
        var group = new GroupBox { Text = title, Dock = DockStyle.Fill, Padding = new Padding(8) };
        group.Controls.Add(body);
        return group;
    }

    private void BrowseSetting()
    {
        using var dialog = new OpenFileDialog
        {
            Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*",
            InitialDirectory = Path.GetDirectoryName(_settingPathBox.Text)
        };

        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            _settingPathBox.Text = dialog.FileName;
        }
    }

    private void RefreshSnapshot()
    {
        var snapshot = _runtime.GetSnapshot();
        _projectLabel.Text = $"Project: {snapshot.ProjectName}";
        _statusLabel.Text = $"Status: {(snapshot.IsRunning ? "Running" : "Stopped")}";

        _driverGrid.DataSource = snapshot.Drivers
            .Select(kv => new { Id = kv.Key, kv.Value.Type, kv.Value.IsConnected })
            .ToList();

        _deviceGrid.DataSource = snapshot.Devices
            .Select(kv => new
            {
                Id = kv.Key,
                kv.Value.Name,
                kv.Value.Type,
                kv.Value.State,
                kv.Value.DriverType,
                kv.Value.DriverConnected
            })
            .ToList();

        var sortedVars = snapshot.Vars
            .OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase);
        _varsBox.Text = JsonSerializer.Serialize(sortedVars, new JsonSerializerOptions { WriteIndented = true });
    }

    private void OpenConfigForm(Func<string, Form> formFactory)
    {
        var settingPath = _settingPathBox.Text.Trim();
        if (!File.Exists(settingPath))
        {
            MessageBox.Show(this, $"Setting file not found:\n{settingPath}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        using var form = formFactory(settingPath);
        form.ShowDialog(this);
    }
}
