namespace MDKOSS.Gui;

/// <summary>
/// Shared five-zone IDE shell: Menu | Tree | Center | Properties | Status.
/// </summary>
internal sealed class WorkspaceShell : UserControl
{
    private readonly SplitContainer _outerSplit = new()
    {
        Dock = DockStyle.Fill,
        Orientation = Orientation.Vertical
    };
    private readonly SplitContainer _innerSplit = new()
    {
        Dock = DockStyle.Fill,
        Orientation = Orientation.Vertical
    };

    public MenuStrip Menu { get; } = new();
    public TreeView NavigationTree { get; } = new()
    {
        Dock = DockStyle.Fill,
        HideSelection = false,
        ShowLines = true,
        ShowPlusMinus = true,
        FullRowSelect = true
    };
    public Panel CenterHost { get; } = new() { Dock = DockStyle.Fill };
    public Panel CenterToolbarHost { get; } = new()
    {
        Dock = DockStyle.Top,
        Height = 36,
        Padding = new Padding(6, 4, 6, 2)
    };
    public PropertyGrid PropertyGrid { get; } = new()
    {
        Dock = DockStyle.Fill,
        HelpVisible = false,
        PropertySort = PropertySort.Categorized,
        ToolbarVisible = false
    };
    public GroupBox PropertyGroup { get; } = new()
    {
        Text = "Properties",
        Dock = DockStyle.Fill,
        Padding = new Padding(6)
    };
    public StatusStrip Status { get; } = new();
    public ToolStripStatusLabel PathLabel { get; } = new();
    public ToolStripStatusLabel ModeLabel { get; } = new();
    public ToolStripStatusLabel SelectionLabel { get; } = new();
    public ToolStripStatusLabel CountsLabel { get; } = new();

    public WorkspaceShell()
    {
        Dock = DockStyle.Fill;

        var treeGroup = new GroupBox
        {
            Text = "Explorer",
            Dock = DockStyle.Fill,
            Padding = new Padding(6)
        };
        treeGroup.Controls.Add(NavigationTree);

        var centerPanel = new Panel { Dock = DockStyle.Fill };
        centerPanel.Controls.Add(CenterHost);
        centerPanel.Controls.Add(CenterToolbarHost);

        PropertyGroup.Controls.Add(PropertyGrid);

        _innerSplit.Panel1.Controls.Add(centerPanel);
        _innerSplit.Panel2.Controls.Add(PropertyGroup);
        _outerSplit.Panel1.Controls.Add(treeGroup);
        _outerSplit.Panel2.Controls.Add(_innerSplit);

        Status.Items.Add(PathLabel);
        Status.Items.Add(new ToolStripSeparator());
        Status.Items.Add(ModeLabel);
        Status.Items.Add(new ToolStripSeparator());
        Status.Items.Add(SelectionLabel);
        Status.Items.Add(new ToolStripStatusLabel { Spring = true });
        Status.Items.Add(CountsLabel);

        Controls.Add(_outerSplit);
        Controls.Add(Status);
        Controls.Add(Menu);

        LayoutSafeSplitters();
        SizeChanged += (_, _) => LayoutSafeSplitters();
        _outerSplit.SizeChanged += (_, _) => LayoutSafeSplitters();
        _innerSplit.SizeChanged += (_, _) => LayoutSafeSplitters();
    }

    public void AttachToForm(Form form)
    {
        form.MainMenuStrip = Menu;
        form.Controls.Add(this);
    }

    public void SetPropertiesVisible(bool visible)
    {
        _innerSplit.Panel2Collapsed = !visible;
    }

    public bool PropertiesVisible => !_innerSplit.Panel2Collapsed;

    private void LayoutSafeSplitters()
    {
        SetSafeDistance(_outerSplit, preferred: 210, panel1Min: 140, panel2Min: 420);
        SetSafeDistance(_innerSplit, preferred: Math.Max(360, _innerSplit.Width - 320), panel1Min: 280, panel2Min: 220);
    }

    private static void SetSafeDistance(SplitContainer split, int preferred, int panel1Min, int panel2Min)
    {
        if (split.Width <= panel1Min + panel2Min)
        {
            return;
        }

        var distance = Math.Clamp(preferred, panel1Min, split.Width - panel2Min);
        if (split.SplitterDistance != distance)
        {
            split.SplitterDistance = distance;
        }
    }
}
