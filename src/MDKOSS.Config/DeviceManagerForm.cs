using MDKOSS.Core;

namespace MDKOSS.Gui;

public sealed class DeviceManagerForm : Form
{
    private readonly MdkRuntime _runtime;
    private readonly DataGridView _grid = new() { Dock = DockStyle.Fill, ReadOnly = true, AutoGenerateColumns = true };
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 1000 };

    public DeviceManagerForm(MdkRuntime runtime)
    {
        _runtime = runtime;
        Text = "Device Manager";
        Width = 980;
        Height = 620;
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
        var snapshot = _runtime.GetSnapshot();
        _grid.DataSource = snapshot.Devices
            .OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
            .Select(kv => new
            {
                Id = kv.Key,
                kv.Value.Name,
                kv.Value.Type,
                kv.Value.State,
                kv.Value.DriverType,
                kv.Value.DriverConnected,
                IoPoints = kv.Value.GpioIoPoints?.Count ?? 0,
                PlatformAxes = kv.Value.PlatformAxes?.Count ?? 0
            })
            .ToList();
    }
}
