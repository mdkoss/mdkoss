using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using MDKOSS.Core;

namespace MDKOSS.Config.Wpf;

public partial class ComponentEditorDialog : Window
{
    private readonly ConfigModule _module;
    private readonly ObservableCollection<KvPairRow> _paramRows = [];

    public ComponentEditorDialog(ConfigModule module, CreateComponentRequest request)
    {
        InitializeComponent();
        _module = module;
        Request = request;
        Title = $"新建 — {ConfigWorkspace.ModuleDisplayName(module)}";
        HeadlineText.Text = $"快速配置 {ConfigWorkspace.ModuleDisplayName(module)} 组件属性";
        ConfigureVisibility();
        BindOptions(request);
        Prefill(request);
        ParamGrid.ItemsSource = _paramRows;
    }

    public CreateComponentRequest Request { get; }

    private void ConfigureVisibility()
    {
        void Show(UIElement el, bool on) => el.Visibility = on ? Visibility.Visible : Visibility.Collapsed;

        switch (_module)
        {
            case ConfigModule.Drivers:
                Show(IdPanel, true);
                Show(NamePanel, true);
                Show(TypePanel, true);
                Show(EnabledPanel, true);
                Show(ParamsPanel, true);
                break;
            case ConfigModule.Devices:
            case ConfigModule.Axis:
                Show(IdPanel, true);
                Show(NamePanel, true);
                Show(TypePanel, true);
                Show(DriverPanel, true);
                Show(EnabledPanel, true);
                Show(ParamsPanel, true);
                break;
            case ConfigModule.Platform:
                Show(IdPanel, true);
                Show(NamePanel, true);
                Show(TypePanel, true);
                Show(DriverPanel, false);
                Show(EnabledPanel, true);
                Show(ParamsPanel, true);
                break;
            case ConfigModule.Tasks:
                Show(IdPanel, false);
                Show(NamePanel, true);
                Show(TypePanel, true);
                Show(DriverPanel, true);
                Show(IntervalPanel, true);
                Show(ParamsPanel, true);
                break;
            case ConfigModule.Recipes:
                Show(IdPanel, true);
                Show(NamePanel, true);
                Show(DescriptionPanel, true);
                Show(ParamsPanel, true);
                break;
            case ConfigModule.Vars:
                Show(IdPanel, true);
                Show(ValuePanel, true);
                break;
            default:
                break;
        }
    }

    private void BindOptions(CreateComponentRequest request)
    {
        TypeCombo.ItemsSource = request.TypeOptions;
        DriverCombo.ItemsSource = request.DriverOptions;
    }

    private void Prefill(CreateComponentRequest request)
    {
        IdBox.Text = request.Id;
        NameBox.Text = request.Name;
        TypeCombo.Text = request.Type;
        DriverCombo.Text = request.DriverId;
        IntervalBox.Text = request.IntervalMs.ToString();
        DescriptionBox.Text = request.Description;
        ValueBox.Text = request.Value;
        EnabledCheck.IsChecked = request.Enabled;
        _paramRows.Clear();
        foreach (var row in request.Parameters)
        {
            _paramRows.Add(new KvPairRow { Key = row.Key, Value = row.Value });
        }
    }

    private void TypeCombo_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (!IsLoaded)
        {
            return;
        }

        ApplyTypeParameterTemplate(replaceAll: true);
    }

    private void TypeCombo_LostFocus(object sender, RoutedEventArgs e) =>
        ApplyTypeParameterTemplate(replaceAll: false);

    private void FillTypeParams_Click(object sender, RoutedEventArgs e) =>
        ApplyTypeParameterTemplate(replaceAll: true);

    private void ApplyTypeParameterTemplate(bool replaceAll)
    {
        if (_module is not (ConfigModule.Drivers or ConfigModule.Devices or ConfigModule.Axis
            or ConfigModule.Platform or ConfigModule.Tasks))
        {
            return;
        }

        var type = TypeCombo.Text?.Trim() ?? "";
        var driverId = DriverCombo.Text?.Trim() ?? "";
        var template = ConfigTypeCatalog.DefaultParameters(_module, type, driverId);
        if (template.Count == 0)
        {
            return;
        }

        if (replaceAll)
        {
            _paramRows.Clear();
            var rows = template;
            if (_module == ConfigModule.Platform)
            {
                rows = PlatformDeviceParameterSet.NormalizeParameters(type, template);
            }

            foreach (var kv in rows)
            {
                _paramRows.Add(new KvPairRow { Key = kv.Key, Value = kv.Value });
            }

            return;
        }

        var existing = KvTableHelper.ToStringDict(_paramRows);
        var merged = DeviceParameterPresets.ApplyTemplate(existing, template, overwriteEmptyOnly: true);
        if (_module == ConfigModule.Platform)
        {
            merged = PlatformDeviceParameterSet.NormalizeParameters(type, merged);
        }

        _paramRows.Clear();
        foreach (var kv in merged.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase))
        {
            _paramRows.Add(new KvPairRow { Key = kv.Key, Value = kv.Value });
        }
    }

    private void AddParamRow_Click(object sender, RoutedEventArgs e) =>
        _paramRows.Add(new KvPairRow());

    private void RemoveParamRow_Click(object sender, RoutedEventArgs e)
    {
        if (ParamGrid.SelectedItem is KvPairRow row)
        {
            _paramRows.Remove(row);
        }
        else if (_paramRows.Count > 0)
        {
            _paramRows.RemoveAt(_paramRows.Count - 1);
        }
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            CollectIntoRequest();
            ValidateRequest();
            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "校验失败", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void CollectIntoRequest()
    {
        Request.Id = IdBox.Text?.Trim() ?? "";
        Request.Name = NameBox.Text?.Trim() ?? "";
        Request.Type = TypeCombo.Text?.Trim() ?? "";
        Request.DriverId = DriverCombo.Text?.Trim() ?? "";
        Request.Description = DescriptionBox.Text?.Trim() ?? "";
        Request.Value = ValueBox.Text ?? "";
        Request.Enabled = EnabledCheck.IsChecked == true;
        if (!int.TryParse(IntervalBox.Text, out var interval) || interval <= 0)
        {
            interval = 100;
        }

        Request.IntervalMs = interval;
        Request.Parameters = KvTableHelper.ToStringDict(_paramRows);
        Request.Vars = KvTableHelper.ToObjectDict(_paramRows);
    }

    private void ValidateRequest()
    {
        switch (_module)
        {
            case ConfigModule.Drivers:
            case ConfigModule.Devices:
            case ConfigModule.Axis:
            case ConfigModule.Platform:
            case ConfigModule.Recipes:
            case ConfigModule.Vars:
                if (string.IsNullOrWhiteSpace(Request.Id))
                {
                    throw new InvalidOperationException("Id / Key 不能为空。");
                }

                break;
            case ConfigModule.Tasks:
                if (string.IsNullOrWhiteSpace(Request.Name))
                {
                    throw new InvalidOperationException("任务 Name 不能为空。");
                }

                break;
        }
    }
}

/// <summary>Input/output model for the create-component dialog.</summary>
public sealed class CreateComponentRequest
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string DriverId { get; set; } = string.Empty;
    public int IntervalMs { get; set; } = 100;
    public string Description { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;
    public Dictionary<string, string> Parameters { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, object?> Vars { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public IReadOnlyList<string> TypeOptions { get; set; } = [];
    public IReadOnlyList<string> DriverOptions { get; set; } = [];
}
