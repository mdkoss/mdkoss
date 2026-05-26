using MDKOSS.Core;

namespace MDKOSS.Gui;

public sealed class IoMonitorForm : Form
{
    private readonly MdkRuntime _runtime;
    private readonly DataGridView _grid = new() { Dock = DockStyle.Fill, ReadOnly = true, AutoGenerateColumns = true };
    private readonly CheckBox _hexBox = new() { Text = "Hex values", Width = 100 };
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 500 };

    public IoMonitorForm(MdkRuntime runtime)
    {
        _runtime = runtime;
        Text = "I/O Monitor";
        Width = 980;
        Height = 620;
        StartPosition = FormStartPosition.CenterParent;

        var toolbar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 40, Padding = new Padding(8), WrapContents = false };
        var btnRefresh = new Button { Text = "Refresh", Width = 90 };
        var btnClose = new Button { Text = "Close", Width = 90 };
        toolbar.Controls.AddRange([btnRefresh, _hexBox, btnClose]);

        Controls.Add(_grid);
        Controls.Add(toolbar);

        btnRefresh.Click += (_, _) => RefreshRows();
        btnClose.Click += (_, _) => Close();
        _hexBox.CheckedChanged += (_, _) => RefreshRows();
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
        var rows = new List<object>();
        foreach (var device in _runtime.GetSnapshot().Devices.Values.OrderBy(d => d.Id, StringComparer.OrdinalIgnoreCase))
        {
            foreach (var point in device.GpioIoPoints ?? [])
            {
                rows.Add(new
                {
                    DeviceId = device.Id,
                    point.Alias,
                    point.Direction,
                    point.DriverId,
                    point.Address,
                    point.DriverOnline,
                    Value = FormatValue(point.Value)
                });
            }
        }

        _grid.DataSource = rows;
    }

    private string? FormatValue(string? value)
    {
        if (!_hexBox.Checked || string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        return int.TryParse(value, out var number) ? $"0x{number:X}" : value;
    }
}
