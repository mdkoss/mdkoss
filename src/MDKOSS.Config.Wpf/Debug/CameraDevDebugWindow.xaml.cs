using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using MDKOSS.Core;
using MDKOSS.Core.Drivers;

namespace MDKOSS.Config.Wpf.Debug;

public partial class CameraDevDebugWindow : Window
{
    private readonly ConfigWorkspace _workspace;
    private readonly MVarStore _vars = new();
    private string? _preferredCameraId;
    private ConnectedDriver? _driver;
    private MDeviceBase? _device;
    private bool _isOpen;

    public CameraDevDebugWindow(ConfigWorkspace workspace, string? preferredCameraId = null)
    {
        InitializeComponent();
        _workspace = workspace;
        _preferredCameraId = preferredCameraId;
        Loaded += (_, _) => ReloadCameraList();
        Closed += (_, _) => TearDown();
    }

    private void ReloadCameraList()
    {
        CameraCombo.Items.Clear();
        var cams = _workspace.Setting.Devices
            .Where(d =>
            {
                var t = (d.Type ?? "").ToLowerInvariant();
                return t is "cameradev" or "extcamera";
            })
            .OrderBy(d => d.Id, StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var d in cams)
        {
            CameraCombo.Items.Add(new ComboBoxItem
            {
                Content = $"{d.Id} — {d.Name} [{d.Type}]",
                Tag = d.Id,
            });
        }

        ComboBoxItem? prefer = null;
        if (!string.IsNullOrWhiteSpace(_preferredCameraId))
        {
            prefer = CameraCombo.Items.Cast<ComboBoxItem>()
                .FirstOrDefault(i => string.Equals(i.Tag as string, _preferredCameraId, StringComparison.OrdinalIgnoreCase));
        }

        CameraCombo.SelectedItem = prefer ?? (CameraCombo.Items.Count > 0 ? CameraCombo.Items[0] : null);
        BindSelected();
    }

    private MdkSetting.DeviceConfig? SelectedCamera()
    {
        if (CameraCombo.SelectedItem is ComboBoxItem { Tag: string id })
        {
            return _workspace.Setting.Devices.FirstOrDefault(d =>
                string.Equals(d.Id, id, StringComparison.OrdinalIgnoreCase));
        }

        return null;
    }

    private void CameraCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isOpen)
        {
            TearDown();
            SetOpenUi(false);
        }

        BindSelected();
    }

    private void BindSelected()
    {
        var cam = SelectedCamera();
        TypeBadge.Text = cam is null ? "type=-" : $"type={cam.Type}";
        StatusBox.Text = cam is null
            ? ""
            : ParamText.Format(cam.Parameters);
    }

    private void Open_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var cfg = SelectedCamera() ?? throw new InvalidOperationException("请选择相机设备。");
            TearDown();

            var type = (cfg.Type ?? "").ToLowerInvariant();
            var name = string.IsNullOrWhiteSpace(cfg.Name) ? cfg.Id : cfg.Name;

            if (type == "extcamera")
            {
                if (!DeviceExtensionRegistry.TryCreate(
                        "extcamera",
                        cfg,
                        name,
                        _vars,
                        new Dictionary<string, IDriver>(StringComparer.OrdinalIgnoreCase),
                        out var ext)
                    || ext is null)
                {
                    throw new InvalidOperationException(
                        "extcamera 扩展未注册。请确认 plugins 含 MDKOSS.Extensions.Camera.dll。");
                }

                _device = ext;
                _device.Initialize();
                _device.Start();

                if (DeviceActionRegistry.TryExecute(_device, "open", null, out var openResult))
                {
                    if (!openResult.Success)
                    {
                        throw new InvalidOperationException(openResult.Error ?? "open_failed");
                    }
                }
                else if (!TryInvokeBool(_device, "Open"))
                {
                    throw new InvalidOperationException("无法打开 extcamera（无 open action / Open 方法）。");
                }
            }
            else
            {
                // cameradev
                if (string.IsNullOrWhiteSpace(cfg.DriverId))
                {
                    throw new InvalidOperationException("cameradev 需要 DriverId。");
                }

                var drvCfg = _workspace.Setting.Drivers.FirstOrDefault(d =>
                    string.Equals(d.Id, cfg.DriverId, StringComparison.OrdinalIgnoreCase))
                    ?? throw new InvalidOperationException($"找不到驱动 '{cfg.DriverId}'。");

                _driver = ConnectedDriver.Open(drvCfg);
                var cam = new CameraDevDevice(cfg.Id, name, _driver.Driver, _vars);
                cam.Initialize();
                cam.Start();
                _device = cam;
            }

            _isOpen = true;
            SetOpenUi(true);
            DebugUi.Log(LogBox, $"已打开 {cfg.Id} ({type})");
            RefreshStatus();
        }
        catch (Exception ex)
        {
            TearDown();
            SetOpenUi(false);
            DebugUi.Log(LogBox, "打开失败: " + ex.Message);
            MessageBox.Show(this, ex.Message, "打开失败", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void CloseCam_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_device is not null)
            {
                var type = (SelectedCamera()?.Type ?? "").ToLowerInvariant();
                if (type == "extcamera")
                {
                    if (!DeviceActionRegistry.TryExecute(_device, "close", null, out _))
                    {
                        TryInvokeBool(_device, "Close");
                    }
                }

                _device.Stop();
            }

            DebugUi.Log(LogBox, "已关闭");
        }
        catch (Exception ex)
        {
            DebugUi.Log(LogBox, "关闭异常: " + ex.Message);
        }
        finally
        {
            TearDown();
            SetOpenUi(false);
            RefreshStatus();
        }
    }

    private void Capture_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (!_isOpen || _device is null)
            {
                throw new InvalidOperationException("请先打开相机。");
            }

            var recipe = string.IsNullOrWhiteSpace(RecipeBox.Text) ? "default" : RecipeBox.Text.Trim();
            var type = (SelectedCamera()?.Type ?? "").ToLowerInvariant();

            if (type == "extcamera")
            {
                var parameters = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase)
                {
                    ["recipe"] = JsonSerializer.SerializeToElement(recipe),
                };

                if (DeviceActionRegistry.TryExecute(_device, "capture", parameters, out var result))
                {
                    if (!result.Success)
                    {
                        throw new InvalidOperationException(result.Error ?? "capture_failed");
                    }

                    DebugUi.Log(LogBox, "capture OK");
                    StatusBox.Text = FormatResult(result.Data);
                    return;
                }

                throw new InvalidOperationException("extcamera capture action 不可用。");
            }

            if (_device is CameraDevDevice cam)
            {
                var ok = cam.TriggerCapture(recipe);
                DebugUi.Log(LogBox, $"TriggerCapture({recipe}) {DebugUi.FormatBool(ok)}");
                RefreshStatus();
                return;
            }

            throw new InvalidOperationException("未知相机设备类型。");
        }
        catch (Exception ex)
        {
            DebugUi.Log(LogBox, ex.Message);
            MessageBox.Show(this, ex.Message, "采集失败", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void Refresh_Click(object sender, RoutedEventArgs e) => RefreshStatus();

    private void RefreshStatus()
    {
        var sb = new StringBuilder();
        var cfg = SelectedCamera();
        if (cfg is not null)
        {
            sb.AppendLine($"Id: {cfg.Id}");
            sb.AppendLine($"Type: {cfg.Type}");
            sb.AppendLine($"DriverId: {cfg.DriverId}");
            sb.AppendLine($"Open: {_isOpen}");
            sb.AppendLine($"DeviceState: {_device?.State.ToString() ?? "-"}");
            sb.AppendLine($"DriverConnected: {_driver?.IsConnected.ToString() ?? (_device?.LinkedDriver.IsConnected.ToString() ?? "-")}");
            sb.AppendLine();
            sb.AppendLine("Parameters:");
            sb.AppendLine(ParamText.Format(cfg.Parameters));
        }

        if (_device is not null && string.Equals(cfg?.Type, "extcamera", StringComparison.OrdinalIgnoreCase))
        {
            if (DeviceActionRegistry.TryExecute(_device, "status", null, out var status) && status.Success)
            {
                sb.AppendLine();
                sb.AppendLine("ExtCamera status:");
                sb.AppendLine(FormatResult(status.Data));
            }
        }

        // cameradev vars
        if (_device is CameraDevDevice)
        {
            var snap = _vars.Snapshot();
            var related = snap
                .Where(kv => kv.Key.Contains(_device.Id, StringComparison.OrdinalIgnoreCase))
                .OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (related.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("Vars:");
                foreach (var kv in related)
                {
                    sb.AppendLine($"  {kv.Key} = {kv.Value}");
                }
            }
        }

        StatusBox.Text = sb.ToString();
    }

    private void SetOpenUi(bool open)
    {
        _isOpen = open;
        BtnOpen.IsEnabled = !open;
        BtnClose.IsEnabled = open;
        CameraCombo.IsEnabled = !open;
        StateBadge.Text = open ? "Open" : "Closed";
        StateBadge.Foreground = open
            ? (System.Windows.Media.Brush)FindResource("AccentBrush")
            : (System.Windows.Media.Brush)FindResource("MutedBrush");
    }

    private void TearDown()
    {
        try
        {
            _device?.Dispose();
        }
        catch
        {
            // ignore
        }

        _device = null;

        try
        {
            _driver?.Dispose();
        }
        catch
        {
            // ignore
        }

        _driver = null;
        _isOpen = false;
    }

    private static bool TryInvokeBool(object target, string methodName)
    {
        var mi = target.GetType().GetMethod(methodName, Type.EmptyTypes);
        if (mi is null || mi.ReturnType != typeof(bool))
        {
            return false;
        }

        return mi.Invoke(target, null) is true;
    }

    private static string FormatResult(object? data)
    {
        if (data is null)
        {
            return "(null)";
        }

        try
        {
            return JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
        }
        catch
        {
            return data.ToString() ?? "";
        }
    }
}
