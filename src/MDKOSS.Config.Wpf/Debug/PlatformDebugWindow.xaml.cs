using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using MDKOSS.Core;
using MDKOSS.Core.Drivers;

namespace MDKOSS.Config.Wpf.Debug;

public sealed class PlatformAxisRow
{
    public string Letter { get; set; } = "";
    public short AxisIndex { get; set; }
    public string DriverId { get; set; } = "";
    public string Online { get; set; } = "-";
    public string Enabled { get; set; } = "-";
    public string PrfPos { get; set; } = "-";
    public string EncPos { get; set; } = "-";
    public string Velocity { get; set; } = "-";
}

public partial class PlatformDebugWindow : Window
{
    private readonly ConfigWorkspace _workspace;
    private readonly DispatcherTimer _pollTimer;
    private readonly ObservableCollection<PlatformAxisRow> _rows = [];
    private readonly MultiDriverBag _bag = new();
    private readonly List<(string Letter, short Index, string DriverId)> _axes = [];
    private string? _preferredPlatformId;
    private bool _connected;
    private bool _jogging;

    public PlatformDebugWindow(ConfigWorkspace workspace, string? preferredPlatformId = null)
    {
        InitializeComponent();
        _workspace = workspace;
        _preferredPlatformId = preferredPlatformId;
        AxisGrid.ItemsSource = _rows;
        _pollTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        _pollTimer.Tick += (_, _) => RefreshStatusQuiet();
        Loaded += (_, _) => ReloadPlatformList();
        Closed += (_, _) =>
        {
            _pollTimer.Stop();
            DisconnectInternal();
        };
    }

    private void ReloadPlatformList()
    {
        PlatformCombo.Items.Clear();
        var platforms = _workspace.Setting.Devices
            .Where(d => PlatformDeviceParameterSet.IsPlatformFamilyType((d.Type ?? "").ToLowerInvariant()))
            .OrderBy(d => d.Id, StringComparer.OrdinalIgnoreCase)
            .ToList();
        foreach (var d in platforms)
        {
            PlatformCombo.Items.Add(new ComboBoxItem { Content = $"{d.Id} — {d.Name}", Tag = d.Id });
        }

        ComboBoxItem? prefer = null;
        if (!string.IsNullOrWhiteSpace(_preferredPlatformId))
        {
            prefer = PlatformCombo.Items.Cast<ComboBoxItem>()
                .FirstOrDefault(i => string.Equals(i.Tag as string, _preferredPlatformId, StringComparison.OrdinalIgnoreCase));
        }

        PlatformCombo.SelectedItem = prefer ?? (PlatformCombo.Items.Count > 0 ? PlatformCombo.Items[0] : null);
        BindSelected();
    }

    private MdkSetting.DeviceConfig? SelectedPlatform()
    {
        if (PlatformCombo.SelectedItem is ComboBoxItem { Tag: string id })
        {
            return _workspace.Setting.Devices.FirstOrDefault(d =>
                string.Equals(d.Id, id, StringComparison.OrdinalIgnoreCase));
        }

        return null;
    }

    private void PlatformCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_connected)
        {
            DisconnectInternal();
            SetConnectedUi(false);
        }

        BindSelected();
    }

    private void BindSelected()
    {
        _axes.Clear();
        _rows.Clear();
        var plat = SelectedPlatform();
        if (plat is null)
        {
            KindBadge.Text = "kind=-";
            return;
        }

        var typeLower = (plat.Type ?? "").ToLowerInvariant();
        MPlatformKind? fromAlias = PlatformDeviceParameterSet.TryKindFromDeviceType(typeLower, out var k)
            ? k
            : null;
        var kind = PlatformDeviceParameterSet.ParseKindOrDefault(plat.Parameters, fromAlias);
        KindBadge.Text = $"kind={kind.ToConfigToken()}";

        short index = 0;
        foreach (var letter in kind.AxisLetters())
        {
            var driverId = PlatformDeviceParameterSet.ResolveAxisDriverId(
                plat.Parameters, letter, plat.DriverId ?? "");
            _axes.Add((letter, index, driverId));
            _rows.Add(new PlatformAxisRow
            {
                Letter = letter,
                AxisIndex = index,
                DriverId = driverId,
            });
            index++;
        }

        if (_rows.Count > 0)
        {
            AxisGrid.SelectedIndex = 0;
        }

        UpdateActiveAxisLabel();
    }

    private PlatformAxisRow? SelectedRow() => AxisGrid.SelectedItem as PlatformAxisRow;

    private void AxisGrid_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateActiveAxisLabel();

    private void UpdateActiveAxisLabel()
    {
        var row = SelectedRow();
        ActiveAxisLabel.Text = row is null
            ? "选中轴: —"
            : $"选中轴: {row.Letter} (index={row.AxisIndex}, driver={row.DriverId})";
    }

    private (string Letter, short Index, string DriverId) RequireAxis()
    {
        var row = SelectedRow() ?? throw new InvalidOperationException("请先选中一轴。");
        var match = _axes.FirstOrDefault(a => a.Letter == row.Letter);
        if (match.Letter is null)
        {
            throw new InvalidOperationException("轴定义丢失，请重新选择平台。");
        }

        return match;
    }

    private IDriver RequireDriverForSelected()
    {
        if (!_connected)
        {
            throw new InvalidOperationException("请先连接。");
        }

        var axis = RequireAxis();
        return _bag.GetOrOpen(_workspace.Setting, axis.DriverId).Driver;
    }

    private double ReadDouble(TextBox box, string name)
    {
        if (!double.TryParse(box.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
        {
            throw new InvalidOperationException($"{name} 无效。");
        }

        return v;
    }

    private short ReadShort(TextBox box, string name)
    {
        if (!short.TryParse(box.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v))
        {
            throw new InvalidOperationException($"{name} 无效。");
        }

        return v;
    }

    private void Connect_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var plat = SelectedPlatform() ?? throw new InvalidOperationException("请选择 Platform。");
            if (_axes.Count == 0)
            {
                throw new InvalidOperationException("平台无轴。");
            }

            DisconnectInternal();
            foreach (var axis in _axes)
            {
                _bag.GetOrOpen(_workspace.Setting, axis.DriverId);
            }

            _connected = true;
            SetConnectedUi(true);
            _pollTimer.Start();
            DebugUi.Log(LogBox, $"已连接平台 {plat.Id}，轴数={_axes.Count}，驱动数={_bag.Drivers.Count}");
            RefreshStatusQuiet();
        }
        catch (Exception ex)
        {
            DisconnectInternal();
            SetConnectedUi(false);
            DebugUi.Log(LogBox, "连接失败: " + ex.Message);
            MessageBox.Show(this, ex.Message, "连接失败", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Disconnect_Click(object sender, RoutedEventArgs e)
    {
        _pollTimer.Stop();
        StopAllInternal();
        DisconnectInternal();
        SetConnectedUi(false);
        DebugUi.Log(LogBox, "已断开");
    }

    private void DisconnectInternal()
    {
        _bag.Dispose();
        _connected = false;
        _jogging = false;
    }

    private void SetConnectedUi(bool connected)
    {
        BtnConnect.IsEnabled = !connected;
        BtnDisconnect.IsEnabled = connected;
        PlatformCombo.IsEnabled = !connected;
        ConnBadge.Text = connected ? "已连接" : "未连接";
        ConnBadge.Foreground = connected
            ? (System.Windows.Media.Brush)FindResource("AccentBrush")
            : (System.Windows.Media.Brush)FindResource("MutedBrush");
    }

    private void Refresh_Click(object sender, RoutedEventArgs e) => RefreshStatusQuiet(log: true);

    private void RefreshStatusQuiet(bool log = false)
    {
        if (!_connected)
        {
            return;
        }

        try
        {
            foreach (var row in _rows)
            {
                var axis = _axes.First(a => a.Letter == row.Letter);
                if (!_bag.Drivers.TryGetValue(axis.DriverId, out var conn))
                {
                    row.Online = "N";
                    continue;
                }

                var drv = conn.Driver;
                row.Online = drv.IsConnected ? "Y" : "N";
                row.Enabled = drv.IsAxisEnabled(axis.Index) ? "ON" : "OFF";
                row.PrfPos = drv.TryGetAxisPrfPosition(axis.Index, out var prf)
                    ? prf.ToString("G5", CultureInfo.InvariantCulture) : "N/A";
                row.EncPos = drv.TryGetAxisEncPosition(axis.Index, out var enc)
                    ? enc.ToString("G5", CultureInfo.InvariantCulture) : "N/A";
                row.Velocity = drv.TryGetAxisVelocity(axis.Index, out var vel)
                    ? vel.ToString("G5", CultureInfo.InvariantCulture) : "N/A";
            }

            AxisGrid.Items.Refresh();
            if (log)
            {
                DebugUi.Log(LogBox, "状态已刷新");
            }
        }
        catch (Exception ex)
        {
            if (log)
            {
                DebugUi.Log(LogBox, ex.Message);
            }
        }
    }

    private void Enable_Click(object sender, RoutedEventArgs e) =>
        RunAxis((drv, idx) => drv.EnableAxis(idx), "EnableAxis");

    private void Disable_Click(object sender, RoutedEventArgs e) =>
        RunAxis((drv, idx) => drv.DisableAxis(idx), "DisableAxis");

    private void Stop_Click(object sender, RoutedEventArgs e)
    {
        _jogging = false;
        RunAxis((drv, idx) => drv.Stop(1 << idx), "Stop");
    }

    private void StopAll_Click(object sender, RoutedEventArgs e)
    {
        StopAllInternal();
        DebugUi.Log(LogBox, "全部轴停止");
    }

    private void StopAllInternal()
    {
        _jogging = false;
        if (!_connected)
        {
            return;
        }

        foreach (var axis in _axes)
        {
            if (_bag.Drivers.TryGetValue(axis.DriverId, out var conn))
            {
                try
                {
                    conn.Driver.Stop(1 << axis.Index);
                }
                catch
                {
                    // ignore
                }
            }
        }
    }

    private void Home_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var axis = RequireAxis();
            var drv = RequireDriverForSelected();
            var mode = ReadShort(HomeModeBox, "回零模式");
            var vel = ReadDouble(VelBox, "速度");
            var acc = ReadDouble(AccBox, "加速度");
            var dec = ReadDouble(DecBox, "减速度");
            var ok = drv.MoveAxisHome(axis.Index, mode, vel, acc, dec);
            DebugUi.Log(LogBox, $"Home {axis.Letter} {DebugUi.FormatBool(ok)}");
        }
        catch (Exception ex)
        {
            DebugUi.Log(LogBox, ex.Message);
        }
    }

    private void JogPos_Down(object sender, MouseButtonEventArgs e) => StartJog(+1);
    private void JogNeg_Down(object sender, MouseButtonEventArgs e) => StartJog(-1);
    private void Jog_Up(object sender, MouseEventArgs e) => StopJog();

    private void StartJog(int sign)
    {
        try
        {
            var axis = RequireAxis();
            var drv = RequireDriverForSelected();
            var vel = Math.Abs(ReadDouble(VelBox, "速度")) * sign;
            var acc = ReadDouble(AccBox, "加速度");
            var dec = ReadDouble(DecBox, "减速度");
            _jogging = true;
            var ok = drv.MoveAxisJog(axis.Index, vel, acc, dec);
            DebugUi.Log(LogBox, $"Jog {axis.Letter} vel={vel} {DebugUi.FormatBool(ok)}");
        }
        catch (Exception ex)
        {
            DebugUi.Log(LogBox, ex.Message);
        }
    }

    private void StopJog()
    {
        if (!_jogging || !_connected)
        {
            return;
        }

        try
        {
            var axis = RequireAxis();
            if (_bag.Drivers.TryGetValue(axis.DriverId, out var conn))
            {
                conn.Driver.Stop(1 << axis.Index);
            }

            _jogging = false;
            DebugUi.Log(LogBox, $"Jog stop {axis.Letter}");
        }
        catch (Exception ex)
        {
            DebugUi.Log(LogBox, ex.Message);
        }
    }

    private void VelMove_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var axis = RequireAxis();
            var drv = RequireDriverForSelected();
            var vel = ReadDouble(VelBox, "速度");
            var acc = ReadDouble(AccBox, "加速度");
            var dec = ReadDouble(DecBox, "减速度");
            drv.SetAxisVelocity(axis.Index, Math.Abs(vel));
            var ok = drv.MoveAxisJog(axis.Index, vel, acc, dec);
            _jogging = true;
            DebugUi.Log(LogBox, $"速度移动 {axis.Letter} vel={vel} {DebugUi.FormatBool(ok)}");
        }
        catch (Exception ex)
        {
            DebugUi.Log(LogBox, ex.Message);
        }
    }

    private void PosMove_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var axis = RequireAxis();
            var drv = RequireDriverForSelected();
            var pos = (int)Math.Round(ReadDouble(PosBox, "目标位置"));
            var vel = ReadDouble(VelBox, "速度");
            var acc = ReadDouble(AccBox, "加速度");
            var dec = ReadDouble(DecBox, "减速度");
            var ok = drv.MoveAxisTrap(axis.Index, pos, vel, acc, dec);
            DebugUi.Log(LogBox, $"Trap {axis.Letter} → {pos} {DebugUi.FormatBool(ok)}");
        }
        catch (Exception ex)
        {
            DebugUi.Log(LogBox, ex.Message);
        }
    }

    private void RunAxis(Func<IDriver, short, bool> action, string name)
    {
        try
        {
            var axis = RequireAxis();
            var drv = RequireDriverForSelected();
            var ok = action(drv, axis.Index);
            DebugUi.Log(LogBox, $"{name} {axis.Letter} {DebugUi.FormatBool(ok)}");
            RefreshStatusQuiet();
        }
        catch (Exception ex)
        {
            DebugUi.Log(LogBox, ex.Message);
        }
    }
}
