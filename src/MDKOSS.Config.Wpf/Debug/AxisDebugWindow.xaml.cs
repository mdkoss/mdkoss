using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using MDKOSS.Core;
using MDKOSS.Core.Drivers;

namespace MDKOSS.Config.Wpf.Debug;

public partial class AxisDebugWindow : Window
{
    private readonly ConfigWorkspace _workspace;
    private readonly DispatcherTimer _pollTimer;
    private ConnectedDriver? _session;
    private string? _preferredAxisId;
    private bool _jogging;

    public AxisDebugWindow(ConfigWorkspace workspace, string? preferredAxisId = null)
    {
        InitializeComponent();
        _workspace = workspace;
        _preferredAxisId = preferredAxisId;
        _pollTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        _pollTimer.Tick += (_, _) => RefreshStatusQuiet();
        Loaded += (_, _) => ReloadAxisList();
        Closed += (_, _) =>
        {
            _pollTimer.Stop();
            DisconnectInternal();
        };
    }

    private void ReloadAxisList()
    {
        AxisCombo.Items.Clear();
        var axes = _workspace.Setting.Axes
            .OrderBy(x => x.Id, StringComparer.OrdinalIgnoreCase)
            .ToList();
        foreach (var d in axes)
        {
            AxisCombo.Items.Add(new ComboBoxItem { Content = $"{d.Id} — {d.Name}", Tag = d.Id });
        }

        ComboBoxItem? prefer = null;
        if (!string.IsNullOrWhiteSpace(_preferredAxisId))
        {
            prefer = AxisCombo.Items.Cast<ComboBoxItem>()
                .FirstOrDefault(i => string.Equals(i.Tag as string, _preferredAxisId, StringComparison.OrdinalIgnoreCase));
        }

        AxisCombo.SelectedItem = prefer ?? (AxisCombo.Items.Count > 0 ? AxisCombo.Items[0] : null);
        BindSelected();
    }

    private MdkSetting.DeviceConfig? SelectedAxis()
    {
        if (AxisCombo.SelectedItem is ComboBoxItem { Tag: string id })
        {
            return _workspace.Setting.Axes.FirstOrDefault(d =>
                string.Equals(d.Id, id, StringComparison.OrdinalIgnoreCase));
        }

        return null;
    }

    private void AxisCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_session is not null)
        {
            DisconnectInternal();
            SetConnectedUi(false);
        }

        BindSelected();
    }

    private void BindSelected()
    {
        var axis = SelectedAxis();
        if (axis is null)
        {
            AxisNoBox.Text = "0";
            StDriver.Text = "-";
            return;
        }

        AxisNoBox.Text = DebugUi.ParseAxisIndex(axis.Parameters).ToString(CultureInfo.InvariantCulture);
        StDriver.Text = string.IsNullOrWhiteSpace(axis.DriverId) ? "(none)" : axis.DriverId;
    }

    private short AxisNo()
    {
        if (!short.TryParse(AxisNoBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) || n < 0)
        {
            throw new InvalidOperationException("轴号无效。");
        }

        return n;
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

    private IDriver RequireDriver() =>
        _session?.Driver ?? throw new InvalidOperationException("请先连接。");

    private void Connect_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var axis = SelectedAxis() ?? throw new InvalidOperationException("请选择 Axis 设备。");
            if (string.IsNullOrWhiteSpace(axis.DriverId))
            {
                throw new InvalidOperationException("Axis 未配置 DriverId。");
            }

            var drvCfg = _workspace.Setting.Drivers.FirstOrDefault(d =>
                string.Equals(d.Id, axis.DriverId, StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidOperationException($"找不到驱动 '{axis.DriverId}'。");

            DisconnectInternal();
            _session = ConnectedDriver.Open(drvCfg);
            SetConnectedUi(true);
            _pollTimer.Start();
            DebugUi.Log(LogBox, $"已连接轴设备 {axis.Id} → 驱动 {drvCfg.Id} ({_session.Driver.Name})");
            RefreshStatusQuiet();
        }
        catch (Exception ex)
        {
            DebugUi.Log(LogBox, "连接失败: " + ex.Message);
            MessageBox.Show(this, ex.Message, "连接失败", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Disconnect_Click(object sender, RoutedEventArgs e)
    {
        _pollTimer.Stop();
        try
        {
            if (_session is not null)
            {
                _session.Driver.Stop(1 << AxisNo());
            }
        }
        catch
        {
            // ignore
        }

        DisconnectInternal();
        SetConnectedUi(false);
        DebugUi.Log(LogBox, "已断开");
    }

    private void DisconnectInternal()
    {
        _session?.Dispose();
        _session = null;
        _jogging = false;
    }

    private void SetConnectedUi(bool connected)
    {
        BtnConnect.IsEnabled = !connected;
        BtnDisconnect.IsEnabled = connected;
        AxisCombo.IsEnabled = !connected;
        ConnBadge.Text = connected ? "已连接" : "未连接";
        ConnBadge.Foreground = connected
            ? (System.Windows.Media.Brush)FindResource("AccentBrush")
            : (System.Windows.Media.Brush)FindResource("MutedBrush");
    }

    private void Refresh_Click(object sender, RoutedEventArgs e) => RefreshStatusQuiet(log: true);

    private void RefreshStatusQuiet(bool log = false)
    {
        if (_session is null)
        {
            return;
        }

        try
        {
            var drv = _session.Driver;
            var axis = AxisNo();
            if (drv.TryGetAxisState(axis, out var state))
            {
                StEnabled.Text = state.ServoOn ? "ON" : "OFF";
                StStatus.Text = $"0x{state.Raw:X8} {state.FormatFlags()}";
                StPrfPos.Text = state.PrfPosition.ToString("G6", CultureInfo.InvariantCulture);
                StEncPos.Text = state.EncPosition.ToString("G6", CultureInfo.InvariantCulture);
                StVel.Text = state.Velocity.ToString("G6", CultureInfo.InvariantCulture);
            }
            else
            {
                StEnabled.Text = drv.IsAxisEnabled(axis) ? "ON" : "OFF";
                StStatus.Text = "N/A";
                StPrfPos.Text = drv.TryGetAxisPrfPosition(axis, out var prf) ? prf.ToString("G6", CultureInfo.InvariantCulture) : "N/A";
                StEncPos.Text = drv.TryGetAxisEncPosition(axis, out var enc) ? enc.ToString("G6", CultureInfo.InvariantCulture) : "N/A";
                StVel.Text = drv.TryGetAxisVelocity(axis, out var vel) ? vel.ToString("G6", CultureInfo.InvariantCulture) : "N/A";
            }
            if (log)
            {
                DebugUi.Log(LogBox, $"状态刷新 axis={axis} enabled={StEnabled.Text} status={StStatus.Text}");
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

    private void Enable_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var ok = RequireDriver().EnableAxis(AxisNo());
            DebugUi.Log(LogBox, $"EnableAxis {DebugUi.FormatBool(ok)}");
            RefreshStatusQuiet();
        }
        catch (Exception ex)
        {
            DebugUi.Log(LogBox, ex.Message);
        }
    }

    private void Disable_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var ok = RequireDriver().DisableAxis(AxisNo());
            DebugUi.Log(LogBox, $"DisableAxis {DebugUi.FormatBool(ok)}");
            RefreshStatusQuiet();
        }
        catch (Exception ex)
        {
            DebugUi.Log(LogBox, ex.Message);
        }
    }

    private void Stop_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            _jogging = false;
            var axis = AxisNo();
            var ok = RequireDriver().Stop(1 << axis);
            DebugUi.Log(LogBox, $"Stop mask=0x{(1 << axis):X} {DebugUi.FormatBool(ok)}");
        }
        catch (Exception ex)
        {
            DebugUi.Log(LogBox, ex.Message);
        }
    }

    private void Home_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var drv = RequireDriver();
            var axis = AxisNo();
            var mode = ReadShort(HomeModeBox, "回零模式");
            var vel = ReadDouble(VelBox, "速度");
            var acc = ReadDouble(AccBox, "加速度");
            var dec = ReadDouble(DecBox, "减速度");
            var ok = drv.MoveAxisHome(axis, mode, vel, acc, dec);
            DebugUi.Log(LogBox, $"MoveAxisHome({axis}, mode={mode}) {DebugUi.FormatBool(ok)}");
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
            var drv = RequireDriver();
            var axis = AxisNo();
            var vel = Math.Abs(ReadDouble(VelBox, "速度")) * sign;
            var acc = ReadDouble(AccBox, "加速度");
            var dec = ReadDouble(DecBox, "减速度");
            _jogging = true;
            var ok = drv.MoveAxisJog(axis, vel, acc, dec);
            DebugUi.Log(LogBox, $"Jog start vel={vel} {DebugUi.FormatBool(ok)}");
        }
        catch (Exception ex)
        {
            DebugUi.Log(LogBox, ex.Message);
        }
    }

    private void StopJog()
    {
        if (!_jogging || _session is null)
        {
            return;
        }

        try
        {
            var axis = AxisNo();
            _session.Driver.Stop(1 << axis);
            _jogging = false;
            DebugUi.Log(LogBox, "Jog stop");
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
            var drv = RequireDriver();
            var axis = AxisNo();
            var vel = ReadDouble(VelBox, "速度");
            var acc = ReadDouble(AccBox, "加速度");
            var dec = ReadDouble(DecBox, "减速度");
            drv.SetAxisVelocity(axis, Math.Abs(vel));
            var ok = drv.MoveAxisJog(axis, vel, acc, dec);
            _jogging = true;
            DebugUi.Log(LogBox, $"速度移动 MoveAxisJog vel={vel} {DebugUi.FormatBool(ok)}（点停止结束）");
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
            var drv = RequireDriver();
            var axis = AxisNo();
            var pos = (int)Math.Round(ReadDouble(PosBox, "目标位置"));
            var vel = ReadDouble(VelBox, "速度");
            var acc = ReadDouble(AccBox, "加速度");
            var dec = ReadDouble(DecBox, "减速度");
            var ok = drv.MoveAxisTrap(axis, pos, vel, acc, dec);
            DebugUi.Log(LogBox, $"MoveAxisTrap({axis}, {pos}) {DebugUi.FormatBool(ok)}");
        }
        catch (Exception ex)
        {
            DebugUi.Log(LogBox, ex.Message);
        }
    }
}
