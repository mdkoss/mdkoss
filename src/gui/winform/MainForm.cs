using MDKOSS.Core;
using System.Text.Json;

namespace MDKOSS.Gui;

public sealed class MainForm : Form
{
    private readonly TextBox _settingPathBox = new() { Dock = DockStyle.Top };
    private readonly Button _browseButton = new() { Text = "Browse Setting", Width = 120 };
    private readonly Button _configManagerButton = new() { Text = "Config Manager", Width = 130 };
    private readonly Button _deviceManagerButton = new() { Text = "Device Manager", Width = 120 };
    private readonly Button _taskManagerButton = new() { Text = "Task Manager", Width = 110 };
    private readonly Button _ioMonitorButton = new() { Text = "I/O Monitor", Width = 100 };
    private readonly Button _diagnosticsButton = new() { Text = "Diagnostics", Width = 100 };
    private readonly Label _statusLabel = new() { AutoSize = true, Text = "Status: Stopped" };
    private readonly Label _projectLabel = new() { AutoSize = true, Text = "Project: -" };
    private readonly DataGridView _driverGrid = new() { Dock = DockStyle.Fill, ReadOnly = true, AutoGenerateColumns = true };
    private readonly DataGridView _deviceGrid = new() { Dock = DockStyle.Fill, ReadOnly = true, AutoGenerateColumns = true };
    private readonly TextBox _varsBox = new() { Dock = DockStyle.Fill, Multiline = true, ScrollBars = ScrollBars.Both, ReadOnly = true };
    private readonly TextBox _historyBox = new() { Dock = DockStyle.Fill, Multiline = true, ScrollBars = ScrollBars.Both, ReadOnly = true };
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 1000 };
    private string _lastRuntimeSummary = string.Empty;

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
        topPanel.Controls.Add(_configManagerButton);
        topPanel.Controls.Add(_deviceManagerButton);
        topPanel.Controls.Add(_taskManagerButton);
        topPanel.Controls.Add(_ioMonitorButton);
        topPanel.Controls.Add(_diagnosticsButton);
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
        var splitBottom = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical,
            SplitterDistance = 540
        };
        splitBottom.Panel1.Controls.Add(CreateGroup("Vars Snapshot", _varsBox));
        splitBottom.Panel2.Controls.Add(CreateGroup("Runtime History", _historyBox));
        splitMain.Panel2.Controls.Add(splitBottom);

        Controls.Add(splitMain);
        Controls.Add(_settingPathBox);
        Controls.Add(topPanel);

        _browseButton.Click += (_, _) => BrowseSetting();
        _configManagerButton.Click += (_, _) => OpenConfigForm(path => new ComponentConfigForm(path));
        _deviceManagerButton.Click += (_, _) => OpenRuntimeForm(new DeviceManagerForm(_runtime));
        _taskManagerButton.Click += (_, _) => OpenRuntimeForm(new TaskManagerForm(_runtime));
        _ioMonitorButton.Click += (_, _) => OpenRuntimeForm(new IoMonitorForm(_runtime));
        _diagnosticsButton.Click += (_, _) => ExportDiagnostics();
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
        AppendRuntimeHistory(snapshot);
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

    private void OpenRuntimeForm(Form form)
    {
        using (form)
        {
            form.ShowDialog(this);
        }
    }

    private void AppendRuntimeHistory(RuntimeSnapshot snapshot)
    {
        var summary =
            $"{(snapshot.IsRunning ? "Running" : "Stopped")}; " +
            $"drivers={snapshot.Drivers.Count}; devices={snapshot.Devices.Count}; vars={snapshot.Vars.Count}";
        if (string.Equals(summary, _lastRuntimeSummary, StringComparison.Ordinal))
        {
            return;
        }

        _lastRuntimeSummary = summary;
        _historyBox.AppendText($"[{DateTime.Now:HH:mm:ss}] {summary}{Environment.NewLine}");
    }

    private void ExportDiagnostics()
    {
        using var dialog = new SaveFileDialog
        {
            Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*",
            DefaultExt = "json",
            AddExtension = true,
            FileName = $"mdkoss-diagnostics-{DateTime.Now:yyyyMMdd-HHmmss}.json"
        };

        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        var diagnostics = new
        {
            ExportedAt = DateTimeOffset.Now,
            SettingPath = _settingPathBox.Text.Trim(),
            Setting = File.Exists(_settingPathBox.Text.Trim()) ? ConfigFormHelpers.LoadSetting(_settingPathBox.Text.Trim()) : null,
            Snapshot = _runtime.GetSnapshot(),
            Tasks = _runtime.GetTaskSnapshots(),
            History = _historyBox.Text
        };

        File.WriteAllText(dialog.FileName, JsonSerializer.Serialize(diagnostics, new JsonSerializerOptions { WriteIndented = true }));
        MessageBox.Show(this, $"Diagnostics exported:\n{dialog.FileName}", "Info", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }
}
