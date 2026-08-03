using MDKOSS.Core;
using System.Text.Json;

namespace MDKOSS.Gui;

public sealed class MainForm : Form
{
    private readonly WorkspaceShell _shell = new();
    private readonly DataGridView _grid = new()
    {
        Dock = DockStyle.Fill,
        ReadOnly = true,
        AutoGenerateColumns = true,
        SelectionMode = DataGridViewSelectionMode.FullRowSelect,
        MultiSelect = false,
        AllowUserToAddRows = false,
        AllowUserToDeleteRows = false,
        RowHeadersVisible = false
    };
    private readonly TextBox _textView = new()
    {
        Dock = DockStyle.Fill,
        Multiline = true,
        ScrollBars = ScrollBars.Both,
        ReadOnly = true,
        Font = new Font("Consolas", 9f)
    };
    private readonly StructureDiagramPanel _diagram = new();
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 1000 };
    private readonly List<string> _history = [];
    private readonly ToolStripMenuItem _viewTableItem = new() { Text = "Table", Checked = true, CheckOnClick = true };
    private readonly ToolStripMenuItem _viewDiagramItem = new() { Text = "Structure Diagram", CheckOnClick = true };
    private readonly ToolStripMenuItem _viewPropertiesItem = new() { Text = "Properties", Checked = true, CheckOnClick = true };

    private readonly MdkRuntime _runtime;
    private string _settingPath;
    private string _currentNode = "Project";
    private bool _showDiagram;
    private string _lastRuntimeSummary = string.Empty;

    public MainForm(MdkRuntime runtime, string settingPath)
    {
        _runtime = runtime;
        _settingPath = settingPath;
        Text = "MDKOSS Monitor";
        Width = 1280;
        Height = 760;
        MinimumSize = new Size(960, 600);
        StartPosition = FormStartPosition.CenterScreen;

        BuildMenus();
        BuildTree();
        _shell.ModeLabel.Text = "Mode: Online";
        _shell.AttachToForm(this);
        _shell.NavigationTree.AfterSelect += (_, e) =>
        {
            if (e.Node?.Tag is string tag)
            {
                _currentNode = tag;
                RefreshWorkspace(forceHistory: false);
            }
        };
        _grid.SelectionChanged += (_, _) => BindSelectedGridRow();
        _diagram.SelectionChanged += (_, payload) =>
        {
            _shell.PropertyGrid.SelectedObject = payload switch
            {
                ValueTuple<string, string, bool> driver =>
                    new DriverSnapshotRow(driver.Item1, driver.Item2, driver.Item3),
                ValueTuple<string, string, string, bool> device =>
                    new DeviceSnapshotRow(device.Item1, device.Item1, device.Item2, "", "", device.Item4),
                ValueTuple<string, string, string> task =>
                    new TaskSnapshotRow(task.Item1, task.Item2, 0, ""),
                _ => payload
            };
            _shell.SelectionLabel.Text = _shell.PropertyGrid.SelectedObject is null
                ? "Selected: -"
                : $"Selected: {_shell.PropertyGrid.SelectedObject}";
        };

        _timer.Tick += (_, _) => RefreshWorkspace(forceHistory: true);
        _timer.Start();
        RefreshWorkspace(forceHistory: true);
    }

    private void BuildMenus()
    {
        var file = new ToolStripMenuItem("File");
        file.DropDownItems.Add(CreateItem("Open Setting…", (_, _) => BrowseSetting()));
        file.DropDownItems.Add(CreateItem("Reload Runtime Hint", (_, _) =>
            MessageBox.Show(this,
                "Setting path updated in UI only.\nRestart MDKOSS.Config to load a different setting into the runtime.",
                "Reload Runtime",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information)));
        file.DropDownItems.Add(new ToolStripSeparator());
        file.DropDownItems.Add(CreateItem("Exit", (_, _) => Close()));

        var view = new ToolStripMenuItem("View");
        _viewTableItem.Click += (_, _) => SetDiagram(false);
        _viewDiagramItem.Click += (_, _) => SetDiagram(true);
        _viewPropertiesItem.Click += (_, _) => _shell.SetPropertiesVisible(_viewPropertiesItem.Checked);
        view.DropDownItems.Add(_viewTableItem);
        view.DropDownItems.Add(_viewDiagramItem);
        view.DropDownItems.Add(new ToolStripSeparator());
        view.DropDownItems.Add(_viewPropertiesItem);

        var tools = new ToolStripMenuItem("Tools");
        tools.DropDownItems.Add(CreateItem("Config Manager", (_, _) => OpenConfigForm()));
        tools.DropDownItems.Add(new ToolStripSeparator());
        tools.DropDownItems.Add(CreateItem("Device Manager Window", (_, _) => OpenRuntimeForm(new DeviceManagerForm(_runtime))));
        tools.DropDownItems.Add(CreateItem("Task Manager Window", (_, _) => OpenRuntimeForm(new TaskManagerForm(_runtime))));
        tools.DropDownItems.Add(CreateItem("I/O Monitor Window", (_, _) => OpenRuntimeForm(new IoMonitorForm(_runtime))));

        var diagnostics = new ToolStripMenuItem("Diagnostics");
        diagnostics.DropDownItems.Add(CreateItem("Export Support Package…", (_, _) => ExportDiagnostics()));

        var help = new ToolStripMenuItem("Help");
        help.DropDownItems.Add(CreateItem("About", (_, _) =>
            MessageBox.Show(this,
                "Online runtime monitor.\nLayout: Menu / Tree / Table|Diagram / Properties / Status.",
                "About",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information)));

        _shell.Menu.Items.Add(file);
        _shell.Menu.Items.Add(view);
        _shell.Menu.Items.Add(tools);
        _shell.Menu.Items.Add(diagnostics);
        _shell.Menu.Items.Add(help);
    }

    private static ToolStripMenuItem CreateItem(string text, EventHandler onClick) => new(text, null, onClick);

    private void BuildTree()
    {
        var tree = _shell.NavigationTree;
        tree.Nodes.Clear();
        tree.Nodes.Add(new TreeNode("Project") { Tag = "Project" });
        tree.Nodes.Add(new TreeNode("Runtime") { Tag = "Runtime" });
        tree.Nodes.Add(new TreeNode("Drivers") { Tag = "Drivers" });
        tree.Nodes.Add(new TreeNode("Devices") { Tag = "Devices" });
        tree.Nodes.Add(new TreeNode("Tasks") { Tag = "Tasks" });
        tree.Nodes.Add(new TreeNode("I/O") { Tag = "I/O" });
        tree.Nodes.Add(new TreeNode("Variables") { Tag = "Variables" });
        tree.Nodes.Add(new TreeNode("History") { Tag = "History" });
        tree.SelectedNode = tree.Nodes[0];
    }

    private void SetDiagram(bool enabled)
    {
        _showDiagram = enabled;
        _viewTableItem.Checked = !enabled;
        _viewDiagramItem.Checked = enabled;
        RefreshWorkspace(forceHistory: false);
    }

    private void RefreshWorkspace(bool forceHistory)
    {
        var snapshot = _runtime.GetSnapshot();
        if (forceHistory)
        {
            AppendRuntimeHistory(snapshot);
        }

        _shell.PathLabel.Text = $"Setting: {_settingPath}";
        _shell.ModeLabel.Text = $"Mode: Online ({(snapshot.IsRunning ? "Running" : "Stopped")})";
        _shell.CountsLabel.Text =
            $"Drivers: {snapshot.Drivers.Count}  Devices: {snapshot.Devices.Count}  Tasks: {_runtime.GetTaskSnapshots().Count}  Vars: {snapshot.Vars.Count}";

        _shell.CenterHost.Controls.Clear();
        _shell.CenterToolbarHost.Controls.Clear();

        if (_showDiagram && _currentNode is "Project" or "Runtime" or "Drivers" or "Devices" or "Tasks")
        {
            var setting = TryLoadSetting();
            _diagram.Bind(
                snapshot.Drivers.Select(kv => (kv.Key, kv.Value.Type, kv.Value.IsConnected)),
                snapshot.Devices.Select(kv => (kv.Key, kv.Value.Type, setting?.Devices.FirstOrDefault(d => string.Equals(d.Id, kv.Key, StringComparison.OrdinalIgnoreCase))?.DriverId ?? "", true)),
                _runtime.GetTaskSnapshots().Select(t => (t.Name, t.Type, "")));
            _shell.CenterHost.Controls.Add(_diagram);
            return;
        }

        switch (_currentNode)
        {
            case "Project":
            case "Runtime":
                ShowText(BuildProjectText(snapshot));
                _shell.PropertyGrid.SelectedObject = new RuntimeProjectInfo
                {
                    ProjectName = snapshot.ProjectName,
                    IsRunning = snapshot.IsRunning,
                    SettingPath = _settingPath,
                    DriverCount = snapshot.Drivers.Count,
                    DeviceCount = snapshot.Devices.Count,
                    VarCount = snapshot.Vars.Count
                };
                _shell.SelectionLabel.Text = "Selected: Project";
                break;
            case "Drivers":
                ShowGrid(snapshot.Drivers
                    .OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
                    .Select(kv => new DriverSnapshotRow(kv.Key, kv.Value.Type, kv.Value.IsConnected))
                    .ToList());
                break;
            case "Devices":
                ShowGrid(snapshot.Devices
                    .OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
                    .Select(kv => new DeviceSnapshotRow(
                        kv.Key,
                        kv.Value.Name,
                        kv.Value.Type,
                        kv.Value.State,
                        kv.Value.DriverType,
                        kv.Value.DriverConnected))
                    .ToList());
                break;
            case "Tasks":
                ShowGrid(_runtime.GetTaskSnapshots()
                    .OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
                    .Select(t => new TaskSnapshotRow(t.Name, t.Type, t.IntervalMs, t.State))
                    .ToList());
                break;
            case "I/O":
                ShowGrid(BuildIoRows(snapshot));
                break;
            case "Variables":
                var sortedVars = snapshot.Vars
                    .OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase);
                ShowText(JsonSerializer.Serialize(sortedVars, new JsonSerializerOptions { WriteIndented = true }));
                _shell.PropertyGrid.SelectedObject = null;
                _shell.SelectionLabel.Text = "Selected: Variables";
                break;
            case "History":
                ShowText(string.Join(Environment.NewLine, _history));
                _shell.PropertyGrid.SelectedObject = null;
                _shell.SelectionLabel.Text = "Selected: History";
                break;
        }
    }

    private void ShowGrid<T>(List<T> rows)
    {
        _grid.DataSource = rows;
        _shell.CenterHost.Controls.Add(_grid);
        BindSelectedGridRow();
    }

    private void ShowText(string text)
    {
        _textView.Text = text;
        _shell.CenterHost.Controls.Add(_textView);
    }

    private void BindSelectedGridRow()
    {
        if (_grid.CurrentRow?.DataBoundItem is { } item)
        {
            _shell.PropertyGrid.SelectedObject = item;
            _shell.SelectionLabel.Text = $"Selected: {item}";
        }
        else if (_shell.CenterHost.Controls.Contains(_grid))
        {
            _shell.PropertyGrid.SelectedObject = null;
            _shell.SelectionLabel.Text = $"Selected: {_currentNode}";
        }
    }

    private static string BuildProjectText(RuntimeSnapshot snapshot) =>
        $"Project: {snapshot.ProjectName}\n" +
        $"Status: {(snapshot.IsRunning ? "Running" : "Stopped")}\n" +
        $"Drivers: {snapshot.Drivers.Count}\n" +
        $"Devices: {snapshot.Devices.Count}\n" +
        $"Variables: {snapshot.Vars.Count}\n\n" +
        "Use Tools → Config Manager for offline JSON editing.\n" +
        "Select Drivers / Devices / Tasks / I/O in the tree for live tables.";

    private static List<IoSnapshotRow> BuildIoRows(RuntimeSnapshot snapshot)
    {
        var rows = new List<IoSnapshotRow>();
        foreach (var device in snapshot.Devices.Values.OrderBy(d => d.Id, StringComparer.OrdinalIgnoreCase))
        {
            foreach (var point in device.GpioIoPoints ?? [])
            {
                rows.Add(new IoSnapshotRow(device.Id, point.Alias, point.Direction, point.Value, point.DriverId, point.Address));
            }
        }

        return rows;
    }

    private MdkSetting? TryLoadSetting()
    {
        try
        {
            return File.Exists(_settingPath) ? ConfigFormHelpers.LoadSetting(_settingPath) : null;
        }
        catch
        {
            return null;
        }
    }

    private void BrowseSetting()
    {
        using var dialog = new OpenFileDialog
        {
            Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*",
            InitialDirectory = Path.GetDirectoryName(_settingPath)
        };

        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        _settingPath = dialog.FileName;
        MessageBox.Show(this,
            "Setting path updated.\nRestart the application to load this setting into the runtime.\nConfig Manager can edit the file offline now.",
            "Setting Changed",
            MessageBoxButtons.OK,
            MessageBoxIcon.Warning);
        RefreshWorkspace(forceHistory: false);
    }

    private void OpenConfigForm()
    {
        if (!File.Exists(_settingPath))
        {
            MessageBox.Show(this, $"Setting file not found:\n{_settingPath}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        using var form = new ComponentConfigForm(_settingPath);
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
        _history.Add($"[{DateTime.Now:HH:mm:ss}] {summary}");
        if (_history.Count > 500)
        {
            _history.RemoveAt(0);
        }
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
            SettingPath = _settingPath,
            Setting = File.Exists(_settingPath) ? ConfigFormHelpers.LoadSetting(_settingPath) : null,
            Snapshot = _runtime.GetSnapshot(),
            Tasks = _runtime.GetTaskSnapshots(),
            History = _history
        };

        File.WriteAllText(dialog.FileName, JsonSerializer.Serialize(diagnostics, new JsonSerializerOptions { WriteIndented = true }));
        MessageBox.Show(this, $"Diagnostics exported:\n{dialog.FileName}", "Info", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _timer.Stop();
        _timer.Dispose();
        base.OnFormClosed(e);
    }

    private sealed class RuntimeProjectInfo
    {
        public string ProjectName { get; set; } = string.Empty;
        public bool IsRunning { get; set; }
        public string SettingPath { get; set; } = string.Empty;
        public int DriverCount { get; set; }
        public int DeviceCount { get; set; }
        public int VarCount { get; set; }
    }

    private sealed record DriverSnapshotRow(string Id, string Type, bool IsConnected)
    {
        public override string ToString() => $"Driver:{Id}";
    }

    private sealed record DeviceSnapshotRow(string Id, string Name, string Type, string State, string? DriverType, bool DriverConnected)
    {
        public override string ToString() => $"Device:{Id}";
    }

    private sealed record TaskSnapshotRow(string Name, string Type, int IntervalMs, string State)
    {
        public override string ToString() => $"Task:{Name}";
    }

    private sealed record IoSnapshotRow(string DeviceId, string Alias, string Direction, string? Value, string? DriverId, string? Address)
    {
        public override string ToString() => $"IO:{DeviceId}/{Alias}";
    }
}
