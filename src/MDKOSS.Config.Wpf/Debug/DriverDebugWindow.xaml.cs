using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using MDKOSS.Core;
using MDKOSS.Core.Drivers;

namespace MDKOSS.Config.Wpf.Debug;

public partial class DriverDebugWindow : Window
{
    private readonly ConfigWorkspace _workspace;
    private readonly ObservableCollection<KvPairRow> _paramRows = [];
    private ConnectedDriver? _session;
    private ObservableCollection<IoBitRow> _diRows = [];
    private ObservableCollection<IoBitRow> _doRows = [];
    private string? _preferredDriverId;

    public DriverDebugWindow(ConfigWorkspace workspace, string? preferredDriverId = null)
    {
        InitializeComponent();
        _workspace = workspace;
        _preferredDriverId = preferredDriverId;
        ParamGrid.ItemsSource = _paramRows;
        foreach (var key in DebugUi.ConfigPathKeys)
        {
            CfgKeyCombo.Items.Add(key);
        }

        CfgKeyCombo.SelectedIndex = 0;
        foreach (var (label, type) in DebugUi.DiPortPresets)
        {
            DiTypeCombo.Items.Add(new ComboBoxItem { Content = label, Tag = type });
        }

        foreach (var (label, type) in DebugUi.DoPortPresets)
        {
            DoTypeCombo.Items.Add(new ComboBoxItem { Content = label, Tag = type });
        }

        DiTypeCombo.SelectedIndex = 0;
        DoTypeCombo.SelectedIndex = 0;
        DiGroupBox.Text = DebugUi.DefaultDiGroup.ToString(CultureInfo.InvariantCulture);
        DoGroupBox.Text = DebugUi.DefaultDoGroup.ToString(CultureInfo.InvariantCulture);
        Loaded += (_, _) => ReloadDriverList();
        Closed += (_, _) => DisconnectInternal();
    }

    private void DiTypeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DiTypeCombo.SelectedItem is ComboBoxItem { Tag: short type })
        {
            DiGroupBox.Text = type.ToString(CultureInfo.InvariantCulture);
        }
    }

    private void DoTypeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DoTypeCombo.SelectedItem is ComboBoxItem { Tag: short type })
        {
            DoGroupBox.Text = type.ToString(CultureInfo.InvariantCulture);
        }
    }

    private void ReloadDriverList()
    {
        DriverCombo.Items.Clear();
        foreach (var d in _workspace.Setting.Drivers.OrderBy(x => x.Id, StringComparer.OrdinalIgnoreCase))
        {
            DriverCombo.Items.Add(d.Id);
        }

        if (!string.IsNullOrWhiteSpace(_preferredDriverId)
            && DriverCombo.Items.Cast<string>().Any(id =>
                string.Equals(id, _preferredDriverId, StringComparison.OrdinalIgnoreCase)))
        {
            DriverCombo.SelectedItem = DriverCombo.Items.Cast<string>()
                .First(id => string.Equals(id, _preferredDriverId, StringComparison.OrdinalIgnoreCase));
        }
        else if (DriverCombo.Items.Count > 0)
        {
            DriverCombo.SelectedIndex = 0;
        }

        SettingPathBox.Text = _workspace.DocumentPath;
        BindSelectedDriver();
    }

    private MdkSetting.DriverConfig? SelectedConfig()
    {
        if (DriverCombo.SelectedItem is not string id)
        {
            return null;
        }

        return _workspace.Setting.Drivers.FirstOrDefault(d =>
            string.Equals(d.Id, id, StringComparison.OrdinalIgnoreCase));
    }

    private void DriverCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_session is not null)
        {
            DisconnectInternal();
            SetConnectedUi(false);
        }

        BindSelectedDriver();
    }

    private void BindSelectedDriver()
    {
        var cfg = SelectedConfig();
        if (cfg is null)
        {
            IdBox.Text = TypeBox.Text = EnabledBox.Text = DriverCfgPathBox.Text = string.Empty;
            _paramRows.Clear();
            return;
        }

        IdBox.Text = cfg.Id;
        TypeBox.Text = cfg.Type;
        EnabledBox.Text = cfg.Enabled ? "true" : "false";
        _paramRows.Clear();
        foreach (var kv in cfg.Parameters.OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase))
        {
            _paramRows.Add(new KvPairRow { Key = kv.Key, Value = kv.Value });
        }

        SyncCfgPathFromParams();
    }

    private void SyncCfgPathFromParams()
    {
        var key = CfgKeyCombo.SelectedItem as string ?? "configPath";
        var row = _paramRows.FirstOrDefault(r => string.Equals(r.Key, key, StringComparison.OrdinalIgnoreCase));
        DriverCfgPathBox.Text = row?.Value ?? DebugUi.FindConfigPath(ToParamDict()) ?? string.Empty;
    }

    private Dictionary<string, string> ToParamDict() =>
        _paramRows
            .Where(r => !string.IsNullOrWhiteSpace(r.Key))
            .ToDictionary(r => r.Key.Trim(), r => r.Value ?? string.Empty, StringComparer.OrdinalIgnoreCase);

    private void CfgKeyCombo_SelectionChanged(object sender, SelectionChangedEventArgs e) => SyncCfgPathFromParams();

    private void BrowseConfig_Click(object sender, RoutedEventArgs e)
    {
        MessageBox.Show(this,
            "主配置路径由「文件 → 打开」决定，调试窗只读显示。\n如需换工程请回到主窗口打开其它 JSON/DB。",
            "配置文件路径",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private void BrowseDriverCfg_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title = "选择驱动配置文件",
            Filter = "Config|*.cfg;*.ini;*.xml;*.json;*.dll|All|*.*",
        };
        if (dlg.ShowDialog(this) != true)
        {
            return;
        }

        DriverCfgPathBox.Text = dlg.FileName;
        var key = CfgKeyCombo.SelectedItem as string ?? "configPath";
        var row = _paramRows.FirstOrDefault(r => string.Equals(r.Key, key, StringComparison.OrdinalIgnoreCase));
        if (row is null)
        {
            _paramRows.Add(new KvPairRow { Key = key, Value = dlg.FileName });
        }
        else
        {
            row.Value = dlg.FileName;
            ParamGrid.Items.Refresh();
        }
    }

    private void ApplyParams_Click(object sender, RoutedEventArgs e)
    {
        if (_session is null)
        {
            DebugUi.Log(LogBox, "未连接：参数仅缓存在界面，连接时会带上当前表格。");
            return;
        }

        // Re-init requires reconnect — copy values into session config for display.
        foreach (var kv in ToParamDict())
        {
            _session.Config.Parameters[kv.Key] = kv.Value;
        }

        DebugUi.Log(LogBox, "已更新会话参数副本。若驱动需重新加载配置文件，请断开后重新连接。");
    }

    private void Connect_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var baseCfg = SelectedConfig()
                ?? throw new InvalidOperationException("请先选择 Driver。");

            var cfg = new MdkSetting.DriverConfig
            {
                Id = baseCfg.Id,
                Type = baseCfg.Type,
                Enabled = true,
                Parameters = ToParamDict(),
            };

            // Push UI config-path field into parameters.
            var key = CfgKeyCombo.SelectedItem as string ?? "configPath";
            if (!string.IsNullOrWhiteSpace(DriverCfgPathBox.Text))
            {
                cfg.Parameters[key] = DriverCfgPathBox.Text.Trim();
            }

            DisconnectInternal();
            _session = ConnectedDriver.Open(cfg);
            SetConnectedUi(true);
            DebugUi.Log(LogBox, $"已连接 {_session.Config.Id} ({_session.Driver.Name}), IsConnected={_session.IsConnected}");
            ReadDi_Click(sender, e);
            ReadDo_Click(sender, e);
        }
        catch (Exception ex)
        {
            DebugUi.Log(LogBox, "连接失败: " + ex.Message);
            MessageBox.Show(this, ex.Message, "连接失败", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Disconnect_Click(object sender, RoutedEventArgs e)
    {
        DisconnectInternal();
        SetConnectedUi(false);
        DebugUi.Log(LogBox, "已断开");
    }

    private void DisconnectInternal()
    {
        _session?.Dispose();
        _session = null;
    }

    private void SetConnectedUi(bool connected)
    {
        BtnConnect.IsEnabled = !connected;
        BtnDisconnect.IsEnabled = connected;
        DriverCombo.IsEnabled = !connected;
        ConnBadge.Text = connected ? "已连接" : "未连接";
        ConnBadge.Foreground = connected
            ? (System.Windows.Media.Brush)FindResource("AccentBrush")
            : (System.Windows.Media.Brush)FindResource("MutedBrush");
    }

    private void Refresh_Click(object sender, RoutedEventArgs e)
    {
        if (_session is null)
        {
            DebugUi.Log(LogBox, "未连接");
            return;
        }

        DebugUi.Log(LogBox, $"Name={_session.Driver.Name}, IsConnected={_session.IsConnected}");
        ReadDi_Click(sender, e);
        ReadDo_Click(sender, e);
    }

    private short ReadIoGroup(TextBox box, string name)
    {
        if (!short.TryParse(box.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var g) || g < 0)
        {
            throw new InvalidOperationException($"{name} Group 必须是非负整数。");
        }

        return g;
    }

    private short DiGroup() => ReadIoGroup(DiGroupBox, "DI");
    private short DoGroup() => ReadIoGroup(DoGroupBox, "DO");

    private IReadOnlyDictionary<string, string> SessionParameters() =>
        _session?.Config.Parameters ?? ToParamDict();

    private int DiBitCount() =>
        IoBitGrid.ResolveBitCount(SessionParameters(), "inBits",
            string.Equals(SelectedConfig()?.Type, "vio", StringComparison.OrdinalIgnoreCase)
                ? 128
                : IoBitGrid.DefaultBitCount);

    private int DoBitCount() =>
        IoBitGrid.ResolveBitCount(SessionParameters(), "outBits",
            string.Equals(SelectedConfig()?.Type, "vio", StringComparison.OrdinalIgnoreCase)
                ? 128
                : IoBitGrid.DefaultBitCount);

    private static int[] ReadGroups(IDriver drv, short baseGroup, int bitCount, bool di)
    {
        var groupCount = IoBitGrid.GroupCount(bitCount);
        var words = new int[groupCount];
        for (var i = 0; i < groupCount; i++)
        {
            var g = (short)(baseGroup + i);
            if (di)
            {
                if (!drv.TryReadDi(g, out words[i]))
                {
                    throw new InvalidOperationException($"TryReadDi({g}) FAIL");
                }
            }
            else if (!drv.TryReadDo(g, out words[i]))
            {
                throw new InvalidOperationException($"TryReadDo({g}) FAIL");
            }
        }

        return words;
    }

    private IDriver RequireDriver() =>
        _session?.Driver ?? throw new InvalidOperationException("请先连接驱动。");

    private void ReadDi_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var drv = RequireDriver();
            var group = DiGroup();
            var bitCount = DiBitCount();
            var words = ReadGroups(drv, group, bitCount, di: true);
            _diRows = IoBitGrid.FromWords(group, words, bitCount, "DI");
            DiGrid.ItemsSource = _diRows;
            DebugUi.Log(LogBox,
                $"TryReadDi({group}..{group + words.Length - 1}) bits={bitCount} → {string.Join(", ", words.Select(w => $"0x{w:X8}"))}");
        }
        catch (Exception ex)
        {
            DebugUi.Log(LogBox, ex.Message);
        }
    }

    private void ReadDo_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var drv = RequireDriver();
            var group = DoGroup();
            var bitCount = DoBitCount();
            var words = ReadGroups(drv, group, bitCount, di: false);
            _doRows = IoBitGrid.FromWords(group, words, bitCount, "DO");
            DoGrid.ItemsSource = _doRows;
            DebugUi.Log(LogBox,
                $"TryReadDo({group}..{group + words.Length - 1}) bits={bitCount} → {string.Join(", ", words.Select(w => $"0x{w:X8}"))}");
        }
        catch (Exception ex)
        {
            DebugUi.Log(LogBox, ex.Message);
        }
    }

    private void WriteDo_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var drv = RequireDriver();
            var group = DoGroup();
            var bitCount = DoBitCount();
            if (_doRows.Count == 0)
            {
                _doRows = IoBitGrid.FromWords(group, new int[IoBitGrid.GroupCount(bitCount)], bitCount, "DO");
                DoGrid.ItemsSource = _doRows;
            }

            var groupCount = IoBitGrid.GroupCount(bitCount);
            for (var i = 0; i < groupCount; i++)
            {
                var g = (short)(group + i);
                var word = IoBitGrid.ToWord(_doRows, g);
                var ok = drv.WriteDo(g, word);
                DebugUi.Log(LogBox, $"WriteDo({g}, 0x{word:X8}) {DebugUi.FormatBool(ok)}");
            }
        }
        catch (Exception ex)
        {
            DebugUi.Log(LogBox, ex.Message);
        }
    }

    private void WriteDoBit_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var drv = RequireDriver();
            if (DoGrid.SelectedItem is not IoBitRow row)
            {
                MessageBox.Show(this, "请先在 DO 表中选中一行。", "写位", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var ok = drv.WriteDoBit(row.Group, row.Index, row.Value);
            DebugUi.Log(LogBox, $"WriteDoBit({row.Group},{row.Index},{row.Value}) {DebugUi.FormatBool(ok)}");
        }
        catch (Exception ex)
        {
            DebugUi.Log(LogBox, ex.Message);
        }
    }
}
