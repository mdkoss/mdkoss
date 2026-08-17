using MDKOSS.Core;

namespace MDKOSS.Tasks;

/// <summary>
/// System-level machine task: start / stop / reset (GPIO buttons or <c>machine.command</c>)
/// drive <c>machine.state</c>. Always registered by the runtime when not already in config.
/// </summary>
public sealed class TaskMachineTask : MTaskBase
{
    public const string TaskName = "task-machine";

    public static class States
    {
        public const string Idle = "idle";
        public const string Running = "running";
        public const string Paused = "paused";
        public const string Stopped = "stopped";
        public const string Fault = "fault";
    }

    private const string DefaultStartAlias = "startButton";
    private const string DefaultStopAlias = "stopButton";
    private const string DefaultResetAlias = "resetButton";
    private const string DefaultPauseAlias = "pauseButton";

    private readonly MVarStore _vars;
    private readonly GpioDevice? _gpio;
    private readonly string _startAlias;
    private readonly string _stopAlias;
    private readonly string _resetAlias;
    private readonly string _pauseAlias;
    private readonly object _sync = new();

    private string _state = States.Idle;
    private bool _buttonsPrimed;
    private bool _prevStart;
    private bool _prevStop;
    private bool _prevReset;
    private bool _prevPause;

    public TaskMachineTask(
        MVarStore vars,
        GpioDevice? gpioDevice,
        int intervalMs = 50,
        IReadOnlyDictionary<string, string>? parameters = null)
        : base(TaskName, intervalMs)
    {
        _vars = vars;
        _gpio = gpioDevice;
        _startAlias = ReadAlias(parameters, "startAlias", DefaultStartAlias);
        _stopAlias = ReadAlias(parameters, "stopAlias", DefaultStopAlias);
        _resetAlias = ReadAlias(parameters, "resetAlias", DefaultResetAlias);
        _pauseAlias = ReadAlias(parameters, "pauseAlias", DefaultPauseAlias);
        InitializeVars();
    }

    public void RequestStart() => SetCommand("start");

    public void RequestStop() => SetCommand("stop");

    public void RequestReset() => SetCommand("reset");

    public void RequestPause() => SetCommand("pause");

    protected override Task TickAsync(CancellationToken cancellationToken)
    {
        var start = ReadButton(_startAlias);
        var stop = ReadButton(_stopAlias);
        var reset = ReadButton(_resetAlias);
        var pause = ReadButton(_pauseAlias);

        _vars.Set("machine.button.start", start);
        _vars.Set("machine.button.stop", stop);
        _vars.Set("machine.button.reset", reset);
        _vars.Set("machine.button.pause", pause);

        string? action = null;
        var source = "command";

        if (!_buttonsPrimed)
        {
            _prevStart = start;
            _prevStop = stop;
            _prevReset = reset;
            _prevPause = pause;
            _buttonsPrimed = true;
        }
        else
        {
            // Stop wins over reset/pause/start in the same scan.
            if (stop && !_prevStop)
            {
                action = "stop";
                source = "button";
            }
            else if (reset && !_prevReset)
            {
                action = "reset";
                source = "button";
            }
            else if (pause && !_prevPause)
            {
                action = "pause";
                source = "button";
            }
            else if (start && !_prevStart)
            {
                action = "start";
                source = "button";
            }

            _prevStart = start;
            _prevStop = stop;
            _prevReset = reset;
            _prevPause = pause;
        }

        var command = _vars.Get<string>("machine.command");
        if (action is null && !string.IsNullOrWhiteSpace(command))
        {
            action = command.Trim();
            source = "command";
        }

        if (!string.IsNullOrWhiteSpace(command))
        {
            _vars.Set("machine.command", string.Empty);
        }

        if (!string.IsNullOrWhiteSpace(action))
        {
            HandleCommand(action, source);
        }

        _vars.Set("machine.alive", true);
        _vars.Set("machine.lastTickUtc", DateTime.UtcNow);
        return Task.CompletedTask;
    }

    private void InitializeVars()
    {
        var existing = _vars.Get<string>("machine.state");
        if (!string.IsNullOrWhiteSpace(existing) && IsKnownState(existing))
        {
            _state = existing.Trim().ToLowerInvariant();
        }
        else
        {
            _state = States.Idle;
        }

        PublishState(_state, _vars.Get<string>("machine.message") ?? "ready");
        _vars.Set("machine.command", string.Empty);
        _vars.Set("machine.button.start", false);
        _vars.Set("machine.button.stop", false);
        _vars.Set("machine.button.reset", false);
        _vars.Set("machine.button.pause", false);
    }

    private void SetCommand(string command)
    {
        _vars.Set("machine.command", command);
        _vars.Set("machine.lastCommandUtc", DateTime.UtcNow);
    }

    private void HandleCommand(string command, string source)
    {
        lock (_sync)
        {
            var normalized = command.ToLowerInvariant();
            switch (normalized)
            {
                case "start":
                case "resume":
                    ApplyStart(source);
                    break;
                case "stop":
                    ApplyStop(source);
                    break;
                case "pause":
                case "hold":
                    ApplyPause(source);
                    break;
                case "reset":
                    ApplyReset(source);
                    break;
                default:
                    PublishState(_state, $"unsupported command: {command}");
                    break;
            }
        }
    }

    private void ApplyStart(string source)
    {
        if (_state == States.Running)
        {
            PublishState(_state, "already running");
            return;
        }

        if (_state == States.Paused)
        {
            PublishState(States.Running, $"resumed ({source})");
            BridgeOperation(source, "start");
            return;
        }

        if (_state is States.Stopped or States.Fault)
        {
            PublishState(_state, "start rejected: reset required");
            return;
        }

        PublishState(States.Running, $"started ({source})");
        BridgeOperation(source, "start");
    }

    private void ApplyStop(string source)
    {
        if (_state is not (States.Running or States.Paused))
        {
            PublishState(_state, "stop ignored: not running");
            return;
        }

        PublishState(States.Stopped, $"stopped ({source})");
        BridgeOperation(source, "stop");
    }

    private void ApplyPause(string source)
    {
        if (_state != States.Running)
        {
            PublishState(_state, "pause ignored: not running");
            return;
        }

        PublishState(States.Paused, $"paused ({source})");
    }

    private void ApplyReset(string source)
    {
        if (_state == States.Running)
        {
            PublishState(_state, "reset rejected: stop first");
            return;
        }

        PublishState(States.Idle, $"reset ({source})");
        BridgeOperation(source, "reset");
    }

    /// <summary>
    /// Mirrors run permission into <c>task.operation.*</c> so process tasks keep working.
    /// GPIO buttons also enqueue <c>task.operation.command</c> for the lamp task;
    /// HMI/API already writes that command in parallel.
    /// </summary>
    private void BridgeOperation(string source, string command)
    {
        if (string.Equals(source, "button", StringComparison.OrdinalIgnoreCase))
        {
            _vars.Set("task.operation.command", command);
        }

        _vars.Set("task.operation.state", _state);
        _vars.Set("task.operation.running", _state == States.Running);
    }

    private void PublishState(string state, string message)
    {
        _state = state;
        var running = state == States.Running;
        _vars.Set("machine.state", state);
        _vars.Set("machine.running", running);
        _vars.Set("machine.paused", state == States.Paused);
        _vars.Set("machine.message", message);
        _vars.Set("machine.lastActionUtc", DateTime.UtcNow);
    }

    private bool ReadButton(string alias)
    {
        if (_gpio is null || string.IsNullOrWhiteSpace(alias))
        {
            return false;
        }

        return _gpio.ReadInput(alias);
    }

    private static bool IsKnownState(string state)
    {
        var normalized = state.Trim();
        return string.Equals(normalized, States.Idle, StringComparison.OrdinalIgnoreCase)
               || string.Equals(normalized, States.Running, StringComparison.OrdinalIgnoreCase)
               || string.Equals(normalized, States.Paused, StringComparison.OrdinalIgnoreCase)
               || string.Equals(normalized, States.Stopped, StringComparison.OrdinalIgnoreCase)
               || string.Equals(normalized, States.Fault, StringComparison.OrdinalIgnoreCase);
    }

    private static string ReadAlias(
        IReadOnlyDictionary<string, string>? parameters,
        string key,
        string fallback)
    {
        if (parameters is not null
            && parameters.TryGetValue(key, out var raw)
            && !string.IsNullOrWhiteSpace(raw))
        {
            return raw.Trim();
        }

        return fallback;
    }
}
