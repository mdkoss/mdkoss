using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using MDKOSS.Core;
using MDKOSS.Core.Data;
using MDKOSS.Core.Flow;
using MDKOSS.Host;
using MDKOSS.Tasks;
using MDKOSS.Tools.Calib.Calib;

namespace MDKOSS.Tools.Calib.ViewModels;

public sealed class MainViewModel : ObservableObject, IDisposable
{
    private readonly DispatcherTimer _poll;
    private MdkRuntime? _runtime;
    private CalibItemViewModel? _selected;
    private string _settingPath = "";
    private string _statusText = "未加载配置";
    private string _runState = "Idle";
    private string _selectedSummary = "选择左侧标定项目";
    private bool _isRunning;
    private string? _lastLog;
    private string? _lastPc;
    private string? _lastPhase;

    public MainViewModel()
    {
        Items = [];
        Parameters = [];
        Results = [];
        Logs = [];

        OpenCommand = new RelayCommand(OpenSetting, () => !IsRunning);
        SaveCommand = new RelayCommand(SaveSetting, () => _runtime is not null && !IsRunning);
        RunCommand = new RelayCommand(RunSelected, () => CanRun);
        StopCommand = new RelayCommand(StopSelected, () => CanStop);
        ApplyParamsCommand = new RelayCommand(ApplyParameters, () => Selected is not null && !IsRunning);
        EditFlowCommand = new RelayCommand(EditFlow, () => Selected?.IsFlow == true && !IsRunning);

        _poll = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        _poll.Tick += (_, _) => PollRuntime();
        _poll.Start();
    }

    public ObservableCollection<CalibItemViewModel> Items { get; }

    public ObservableCollection<ParamRow> Parameters { get; }

    public ObservableCollection<ResultRow> Results { get; }

    public ObservableCollection<LogLine> Logs { get; }

    public RelayCommand OpenCommand { get; }

    public RelayCommand SaveCommand { get; }

    public RelayCommand RunCommand { get; }

    public RelayCommand StopCommand { get; }

    public RelayCommand ApplyParamsCommand { get; }

    public RelayCommand EditFlowCommand { get; }

    public CalibItemViewModel? Selected
    {
        get => _selected;
        set
        {
            if (!SetProperty(ref _selected, value))
            {
                return;
            }

            BindSelected();
            RaiseCommands();
        }
    }

    public string SettingPath
    {
        get => _settingPath;
        private set => SetProperty(ref _settingPath, value);
    }

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public string RunState
    {
        get => _runState;
        private set => SetProperty(ref _runState, value);
    }

    public string SelectedSummary
    {
        get => _selectedSummary;
        private set => SetProperty(ref _selectedSummary, value);
    }

    public bool IsRunning
    {
        get => _isRunning;
        private set
        {
            if (SetProperty(ref _isRunning, value))
            {
                RaiseCommands();
            }
        }
    }

    public bool CanRun => _runtime is not null && Selected is not null && !IsRunning;

    public bool CanStop => _runtime is not null && Selected is not null && IsRunning;

    public MdkRuntime? Runtime => _runtime;

    public bool TryLoad(string settingPath, out string? error)
    {
        error = null;
        ShutdownRuntime();
        Items.Clear();
        Parameters.Clear();
        Results.Clear();
        Logs.Clear();
        Selected = null;

        if (!File.Exists(settingPath))
        {
            error = $"找不到配置文件:\n{settingPath}";
            StatusText = "配置不存在";
            return false;
        }

        if (!RuntimeHost.TryLoadSettings(settingPath, out var setting))
        {
            error = "加载配置失败，详见 logs。";
            StatusText = "配置加载失败";
            return false;
        }

        try
        {
            _runtime = new MdkRuntime(setting, settingPath);
        }
        catch (Exception ex)
        {
            error = ex.Message;
            StatusText = "Runtime 创建失败";
            return false;
        }

        if (!RuntimeHost.TryBootstrapRuntime(_runtime, out var startupError))
        {
            error = startupError ?? "Runtime 启动失败。";
            _runtime.Dispose();
            _runtime = null;
            StatusText = "Runtime 启动失败";
            return false;
        }

        SettingPath = Path.GetFullPath(settingPath);
        ReloadItems();
        StatusText = $"已加载 {setting.ProjectName}  ·  标定项 {Items.Count}";
        RunState = "Idle";
        AppendLog("info", $"打开 {SettingPath}");
        RaiseCommands();
        return true;
    }

    public void Dispose()
    {
        _poll.Stop();
        ShutdownRuntime();
    }

    internal void ApplyFlowDocument(CalibItemViewModel item, FlowDocument document)
    {
        var json = document.ToJson();
        if (item.Config.Parameters.TryGetValue("flowFile", out var flowFile)
            && !string.IsNullOrWhiteSpace(flowFile))
        {
            var path = FlowTask.ResolveFlowFilePath(flowFile, _runtime?.SettingPath ?? SettingPath);
            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");
            File.WriteAllText(path, json);
            item.Config.Parameters.Remove("flowJson");
            AppendLog("info", $"已写入流程文件 {path}");
        }
        else
        {
            item.Config.Parameters["flowJson"] = json;
            AppendLog("info", $"已更新任务 {item.Name} 的 flowJson");
        }

        StatusText = $"流程已更新：{item.Title}";
    }

    private void ReloadItems()
    {
        Items.Clear();
        if (_runtime is null)
        {
            return;
        }

        foreach (var task in CalibCatalog.List(_runtime.Setting))
        {
            Items.Add(new CalibItemViewModel(task));
        }

        if (Items.Count > 0)
        {
            Selected = Items[0];
        }
    }

    private void BindSelected()
    {
        Parameters.Clear();
        Results.Clear();
        _lastLog = null;
        _lastPc = null;
        _lastPhase = null;
        if (Selected is null)
        {
            SelectedSummary = "选择左侧标定项目";
            return;
        }

        SelectedSummary = $"{Selected.Title}  [{Selected.Kind} / {Selected.Type}]";
        foreach (var kv in Selected.Config.Parameters
                     .Where(p => !CalibCatalog.HiddenParamKeys.Contains(p.Key))
                     .OrderBy(p => p.Key, StringComparer.OrdinalIgnoreCase))
        {
            Parameters.Add(new ParamRow { Key = kv.Key, Value = kv.Value });
        }

        OverlayPersistedParameters();
        RefreshResults();
        if (Results.Count == 0)
        {
            OverlayLatestResult();
        }
    }

    private void OpenSetting()
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "MDKOSS 配置 (*.setting.json;*.json)|*.setting.json;*.json|全部文件 (*.*)|*.*",
            Title = "打开标定配置",
        };
        if (dlg.ShowDialog() != true)
        {
            return;
        }

        if (!TryLoad(dlg.FileName, out var error))
        {
            MessageBox.Show(error ?? "打开失败。", "MDKOSS.Tools.Calib", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void SaveSetting()
    {
        if (_runtime is null || string.IsNullOrWhiteSpace(SettingPath))
        {
            return;
        }

        ApplyParameters();
        try
        {
            _runtime.Setting.Save(SettingPath);
            StatusText = $"已保存 {SettingPath}";
            AppendLog("info", "配置已保存");
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "保存失败", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ApplyParameters()
    {
        if (Selected is null || _runtime is null)
        {
            return;
        }

        foreach (var row in Parameters)
        {
            if (string.IsNullOrWhiteSpace(row.Key) || CalibCatalog.HiddenParamKeys.Contains(row.Key))
            {
                continue;
            }

            Selected.Config.Parameters[row.Key.Trim()] = row.Value ?? "";
            _runtime.Vars.Set($"task.{Selected.Name}.param.{row.Key.Trim()}", row.Value ?? "");
        }

        PersistParameters();
        StatusText = $"已应用参数：{Selected.Title}";
        AppendLog("info", "参数已写入任务配置、运行时变量与数据库");
    }

    private void RunSelected()
    {
        if (Selected is null || _runtime is null)
        {
            return;
        }

        ApplyParameters();
        Results.Clear();
        Logs.Clear();
        _lastLog = null;
        _lastPc = null;
        _lastPhase = null;
        AppendLog("info", $"启动 {Selected.Title}");

        if (Selected.IsFlow)
        {
            if (!_runtime.TryGetTask(Selected.Name, out var task) || task is not FlowTask flow)
            {
                AppendLog("error", "找不到 FlowTask 实例（配置 type=flow 且已注册）");
                return;
            }

            flow.Reset();
            IsRunning = true;
            RunState = "Running";
            StatusText = $"运行中：{Selected.Title}";
            return;
        }

        _runtime.Vars.Set($"task.{Selected.Name}.command", "start");
        IsRunning = true;
        RunState = "Running";
        StatusText = $"运行中：{Selected.Title}";
    }

    private void StopSelected()
    {
        if (Selected is null || _runtime is null)
        {
            return;
        }

        if (Selected.IsFlow && _runtime.TryGetTask(Selected.Name, out var task) && task is FlowTask flow)
        {
            flow.Halt();
        }
        else
        {
            _runtime.Vars.Set($"task.{Selected.Name}.command", "stop");
        }

        IsRunning = false;
        RunState = "Stopped";
        StatusText = $"已停止：{Selected.Title}";
        AppendLog("warn", "用户停止");
    }

    private void EditFlow()
    {
        if (Selected is null || !Selected.IsFlow)
        {
            return;
        }

        if (!TryLoadFlow(Selected.Config, _runtime?.SettingPath ?? SettingPath, out var doc, out var error))
        {
            MessageBox.Show(error ?? "无法加载流程。", "编辑流程", MessageBoxButton.OK, MessageBoxImage.Warning);
            doc = FlowDocument.CreateEmpty();
        }

        var editor = new Views.FlowEditWindow(doc) { Owner = Application.Current.MainWindow };
        if (editor.ShowDialog() == true && editor.Result is not null)
        {
            ApplyFlowDocument(Selected, editor.Result);
        }
    }

    private static bool TryLoadFlow(MdkSetting.TaskConfig config, string? settingPath, out FlowDocument document, out string? error)
    {
        document = FlowDocument.CreateEmpty();
        error = null;
        string? json = null;
        if (config.Parameters.TryGetValue("flowJson", out var inline) && !string.IsNullOrWhiteSpace(inline))
        {
            json = inline;
        }
        else if (config.Parameters.TryGetValue("flowFile", out var file)
                 && FlowTask.TryReadFlowFile(file, out var fileJson, settingPath))
        {
            json = fileJson;
        }

        if (string.IsNullOrWhiteSpace(json))
        {
            return true;
        }

        if (!FlowDocument.TryParse(json, out document, out error))
        {
            document = FlowDocument.CreateEmpty();
            return false;
        }

        return true;
    }

    private void PollRuntime()
    {
        if (_runtime is null || Selected is null)
        {
            return;
        }

        RefreshResults();
        PollProcess(Selected);

        if (!IsRunning)
        {
            return;
        }

        if (Selected.IsFlow)
        {
            if (_runtime.TryGetTask(Selected.Name, out var task) && task is FlowTask flow)
            {
                if (flow.FlowState == FlowRunState.Completed)
                {
                    FinishRun("Completed", "流程完成");
                }
                else if (flow.FlowState == FlowRunState.Fault)
                {
                    FinishRun("Fault", flow.LastError ?? "流程故障");
                }
            }

            return;
        }

        var phase = ReadTaskString(Selected.Name, "phase");
        if (string.Equals(phase, "Done", StringComparison.OrdinalIgnoreCase))
        {
            FinishRun("Completed", "标定完成");
        }
        else if (string.Equals(phase, "Fault", StringComparison.OrdinalIgnoreCase))
        {
            FinishRun("Fault", ReadTaskString(Selected.Name, "message") ?? "故障");
        }
    }

    private void PollProcess(CalibItemViewModel item)
    {
        var phase = ReadTaskString(item.Name, "phase");
        var message = ReadTaskString(item.Name, "message");
        var pc = ReadTaskString(item.Name, "flow.pc");
        var lastLog = ReadTaskString(item.Name, "flow.lastLog");
        var flowState = ReadTaskString(item.Name, "flow.state");

        if (!string.IsNullOrWhiteSpace(phase) && !string.Equals(phase, _lastPhase, StringComparison.Ordinal))
        {
            _lastPhase = phase;
            AppendLog("step", $"phase={phase}  {message}");
        }
        else if (!string.IsNullOrWhiteSpace(message)
                 && !string.Equals(message, _lastPhase, StringComparison.Ordinal)
                 && IsRunning)
        {
            AppendLog("step", message);
        }

        if (!string.IsNullOrWhiteSpace(pc) && !string.Equals(pc, _lastPc, StringComparison.Ordinal))
        {
            _lastPc = pc;
            AppendLog("flow", $"pc={pc}  state={flowState}");
        }

        if (!string.IsNullOrWhiteSpace(lastLog) && !string.Equals(lastLog, _lastLog, StringComparison.Ordinal))
        {
            _lastLog = lastLog;
            AppendLog("log", lastLog);
        }
    }

    private void RefreshResults()
    {
        if (_runtime is null || Selected is null)
        {
            return;
        }

        var prefix = $"task.{Selected.Name}.calib.";
        var snap = _runtime.Vars.Snapshot();
        var rows = snap
            .Where(kv => kv.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var kv in rows)
        {
            var key = kv.Key[prefix.Length..];
            var value = kv.Value?.ToString() ?? "";
            var existing = Results.FirstOrDefault(r => string.Equals(r.Key, key, StringComparison.OrdinalIgnoreCase));
            if (existing is null)
            {
                Results.Add(new ResultRow { Key = key, Value = value });
            }
            else if (existing.Value != value)
            {
                existing.Value = value;
            }
        }
    }

    private void FinishRun(string state, string message)
    {
        IsRunning = false;
        RunState = state;
        StatusText = $"{Selected?.Title}：{message}";
        AppendLog(string.Equals(state, "Fault", StringComparison.OrdinalIgnoreCase) ? "error" : "info", message);
        PersistResult(string.Equals(state, "Completed", StringComparison.OrdinalIgnoreCase), message);
    }

    private void OverlayPersistedParameters()
    {
        if (_runtime is null || Selected is null)
        {
            return;
        }

        if (!CalibStore.TryLoadParams(_runtime.DataStore, ProjectName(), Selected.Name, out var stored)
            || stored.Count == 0)
        {
            return;
        }

        foreach (var kv in stored)
        {
            var existing = Parameters.FirstOrDefault(r =>
                string.Equals(r.Key, kv.Key, StringComparison.OrdinalIgnoreCase));
            if (existing is null)
            {
                Parameters.Add(new ParamRow { Key = kv.Key, Value = kv.Value });
            }
            else
            {
                existing.Value = kv.Value;
            }

            Selected.Config.Parameters[kv.Key] = kv.Value;
        }
    }

    private void OverlayLatestResult()
    {
        if (_runtime is null || Selected is null)
        {
            return;
        }

        if (!CalibStore.TryLoadLatestResult(_runtime.DataStore, ProjectName(), Selected.Name, out var record)
            || record is null)
        {
            return;
        }

        foreach (var kv in record.Results.OrderBy(p => p.Key, StringComparer.OrdinalIgnoreCase))
        {
            Results.Add(new ResultRow { Key = kv.Key, Value = kv.Value });
        }
    }

    private void PersistParameters()
    {
        if (_runtime is null || Selected is null)
        {
            return;
        }

        var parameters = CollectGridParams();
        if (!CalibStore.TrySaveParams(_runtime.DataStore, ProjectName(), Selected.Name, parameters, out var error))
        {
            AppendLog("error", "标定参数写入数据库失败：" + (error ?? "unknown"));
        }
    }

    private void PersistResult(bool ok, string message)
    {
        if (_runtime is null || Selected is null)
        {
            return;
        }

        var parameters = CollectGridParams();
        var results = CalibStore.CollectResults(_runtime.Vars.Snapshot(), Selected.Name);
        if (results.Count == 0 && Results.Count > 0)
        {
            foreach (var row in Results)
            {
                if (!string.IsNullOrWhiteSpace(row.Key))
                {
                    results[row.Key] = row.Value ?? "";
                }
            }
        }

        if (!ok && CalibStore.IsTruthyResult(results))
        {
            ok = true;
        }

        if (!CalibStore.TrySaveResult(
                _runtime.DataStore,
                ProjectName(),
                Selected.Name,
                parameters,
                results,
                ok,
                message,
                out var error))
        {
            AppendLog("error", "标定结果写入数据库失败：" + (error ?? "unknown"));
            return;
        }

        AppendLog("info", "标定参数与结果已写入数据库");
    }

    private Dictionary<string, string> CollectGridParams()
    {
        if (Selected is not null && Parameters.Count == 0)
        {
            return CalibStore.CollectVisibleParams(Selected.Config);
        }

        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in Parameters)
        {
            if (string.IsNullOrWhiteSpace(row.Key) || CalibCatalog.HiddenParamKeys.Contains(row.Key))
            {
                continue;
            }

            dict[row.Key.Trim()] = row.Value ?? "";
        }

        return dict;
    }

    private string ProjectName() => _runtime?.Setting.ProjectName ?? "";

    private string? ReadTaskString(string taskName, string suffix)
    {
        if (_runtime is null)
        {
            return null;
        }

        return _runtime.Vars.TryGet<object>($"task.{taskName}.{suffix}", out var raw)
            ? raw?.ToString()
            : null;
    }

    private void AppendLog(string level, string text)
    {
        Logs.Add(new LogLine(DateTime.Now, level, text));
        while (Logs.Count > 400)
        {
            Logs.RemoveAt(0);
        }
    }

    private void ShutdownRuntime()
    {
        if (_runtime is null)
        {
            return;
        }

        try
        {
            RuntimeHost.ShutdownRuntime(_runtime);
        }
        catch
        {
            // best-effort
        }

        _runtime.Dispose();
        _runtime = null;
        IsRunning = false;
    }

    private void RaiseCommands()
    {
        OpenCommand.RaiseCanExecuteChanged();
        SaveCommand.RaiseCanExecuteChanged();
        RunCommand.RaiseCanExecuteChanged();
        StopCommand.RaiseCanExecuteChanged();
        ApplyParamsCommand.RaiseCanExecuteChanged();
        EditFlowCommand.RaiseCanExecuteChanged();
        OnPropertyChanged(nameof(CanRun));
        OnPropertyChanged(nameof(CanStop));
    }
}
