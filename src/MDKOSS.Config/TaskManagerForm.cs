using MDKOSS.Core;

namespace MDKOSS.Gui;

public sealed class TaskManagerForm : Form
{
    private readonly MdkRuntime _runtime;
    private readonly DataGridView _grid = new() { Dock = DockStyle.Fill, ReadOnly = true, AutoGenerateColumns = true };
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 1000 };

    public TaskManagerForm(MdkRuntime runtime)
    {
        _runtime = runtime;
        Text = "Task Manager";
        Width = 860;
        Height = 560;
        StartPosition = FormStartPosition.CenterParent;

        var toolbar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 40, Padding = new Padding(8), WrapContents = false };
        var btnRefresh = new Button { Text = "Refresh", Width = 90 };
        var btnClose = new Button { Text = "Close", Width = 90 };
        toolbar.Controls.AddRange([btnRefresh, btnClose]);

        Controls.Add(_grid);
        Controls.Add(toolbar);

        btnRefresh.Click += (_, _) => RefreshRows();
        btnClose.Click += (_, _) => Close();
        _timer.Tick += (_, _) => RefreshRows();
        _timer.Start();
        RefreshRows();
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _timer.Stop();
        _timer.Dispose();
        base.OnFormClosed(e);
    }

    private void RefreshRows()
    {
        _grid.DataSource = _runtime.GetTaskSnapshots()
            .Select(t => new { t.Name, t.Type, t.IntervalMs, t.State })
            .ToList();
    }
}
