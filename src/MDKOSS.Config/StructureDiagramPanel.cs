namespace MDKOSS.Gui;

internal sealed class StructureDiagramPanel : Panel
{
    private readonly List<DiagramNode> _nodes = [];
    private readonly List<(DiagramNode From, DiagramNode To)> _edges = [];
    private DiagramNode? _selected;

    public event EventHandler<object?>? SelectionChanged;

    public StructureDiagramPanel()
    {
        DoubleBuffered = true;
        Dock = DockStyle.Fill;
        BackColor = Color.FromArgb(248, 249, 251);
        BorderStyle = BorderStyle.FixedSingle;
        Cursor = Cursors.Hand;
    }

    public void Bind(
        IEnumerable<(string Id, string Type, bool Enabled)> drivers,
        IEnumerable<(string Id, string Type, string DriverId, bool Enabled)> devices,
        IEnumerable<(string Name, string Type, string DriverId)> tasks)
    {
        _nodes.Clear();
        _edges.Clear();
        _selected = null;

        var driverNodes = new Dictionary<string, DiagramNode>(StringComparer.OrdinalIgnoreCase);
        var y = 24;
        foreach (var driver in drivers)
        {
            var node = new DiagramNode($"Driver\n{driver.Id}\n[{driver.Type}]", driver.Id, "driver", driver, 24, y, driver.Enabled);
            driverNodes[driver.Id] = node;
            _nodes.Add(node);
            y += 78;
        }

        y = 24;
        var deviceNodes = new List<DiagramNode>();
        foreach (var device in devices)
        {
            var node = new DiagramNode($"Device\n{device.Id}\n[{device.Type}]", device.Id, "device", device, 240, y, device.Enabled);
            deviceNodes.Add(node);
            _nodes.Add(node);
            if (!string.IsNullOrWhiteSpace(device.DriverId) && driverNodes.TryGetValue(device.DriverId, out var driverNode))
            {
                _edges.Add((node, driverNode));
            }

            y += 78;
        }

        y = 24;
        foreach (var task in tasks)
        {
            var node = new DiagramNode($"Task\n{task.Name}\n[{task.Type}]", task.Name, "task", task, 456, y, true);
            _nodes.Add(node);
            if (!string.IsNullOrWhiteSpace(task.DriverId) && driverNodes.TryGetValue(task.DriverId, out var driverNode))
            {
                _edges.Add((node, driverNode));
            }

            y += 78;
        }

        AutoScrollMinSize = new Size(640, Math.Max(Height, Math.Max(24, _nodes.Count == 0 ? 120 : _nodes.Max(n => n.Bounds.Bottom) + 40)));
        Invalidate();
        SelectionChanged?.Invoke(this, null);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        g.TranslateTransform(AutoScrollPosition.X, AutoScrollPosition.Y);
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

        using var edgePen = new Pen(Color.FromArgb(160, 170, 185), 1.5f);
        foreach (var (from, to) in _edges)
        {
            var p1 = new Point(from.Bounds.Left, from.Bounds.Top + from.Bounds.Height / 2);
            var p2 = new Point(to.Bounds.Right, to.Bounds.Top + to.Bounds.Height / 2);
            g.DrawLine(edgePen, p1, p2);
        }

        foreach (var node in _nodes)
        {
            var fill = node.Enabled ? Color.White : Color.FromArgb(235, 235, 235);
            if (ReferenceEquals(node, _selected))
            {
                fill = Color.FromArgb(220, 235, 255);
            }

            using var brush = new SolidBrush(fill);
            using var border = new Pen(ReferenceEquals(node, _selected) ? Color.FromArgb(40, 110, 200) : Color.FromArgb(120, 130, 145), ReferenceEquals(node, _selected) ? 2f : 1f);
            g.FillRectangle(brush, node.Bounds);
            g.DrawRectangle(border, node.Bounds);
            TextRenderer.DrawText(
                g,
                node.Caption,
                Font,
                node.Bounds,
                Color.FromArgb(35, 40, 48),
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }

        if (_nodes.Count == 0)
        {
            TextRenderer.DrawText(
                g,
                "No drivers / devices / tasks to display.",
                Font,
                ClientRectangle,
                Color.Gray,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
    }

    protected override void OnMouseClick(MouseEventArgs e)
    {
        base.OnMouseClick(e);
        var point = new Point(e.X - AutoScrollPosition.X, e.Y - AutoScrollPosition.Y);
        _selected = _nodes.FirstOrDefault(n => n.Bounds.Contains(point));
        Invalidate();
        SelectionChanged?.Invoke(this, _selected?.Payload);
    }

    private sealed class DiagramNode(string caption, string id, string kind, object payload, int x, int y, bool enabled)
    {
        public string Caption { get; } = caption;
        public string Id { get; } = id;
        public string Kind { get; } = kind;
        public object Payload { get; } = payload;
        public bool Enabled { get; } = enabled;
        public Rectangle Bounds { get; } = new(x, y, 170, 64);
    }
}
