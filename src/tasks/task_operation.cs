using MDKOSS.Core;

namespace MDKOSS.Tasks;

public sealed class TaskOperationTask : MTaskBase
{
    private readonly MVarStore _vars;
    private readonly GpioDevice? _gpioDevice;
    private readonly object _sync = new();

    private bool _isStarted;
    private string _lampColor = "red";

    public TaskOperationTask(MVarStore vars, GpioDevice? gpioDevice, int intervalMs = 200)
        : base("task-operation", intervalMs)
    {
        _vars = vars;
        _gpioDevice = gpioDevice;
        InitializeVars();
    }

    public void RequestStart() => SetCommand("start");

    public void RequestStop() => SetCommand("stop");

    public void RequestReset() => SetCommand("reset");

    public void RequestLamp(string lampColor) => SetCommand($"lamp:{lampColor}");

    protected override Task TickAsync(CancellationToken cancellationToken)
    {
        var command = _vars.Get<string>("task.operation.command");
        if (!string.IsNullOrWhiteSpace(command))
        {
            HandleCommand(command.Trim());
            _vars.Set("task.operation.command", string.Empty);
        }

        _vars.Set("task.operation.alive", true);
        _vars.Set("task.operation.lastTickUtc", DateTime.UtcNow);
        return Task.CompletedTask;
    }

    private void InitializeVars()
    {
        _vars.Set("task.operation.state", "idle");
        _vars.Set("task.operation.running", false);
        _vars.Set("task.operation.lamp", _lampColor);
        _vars.Set("task.operation.command", string.Empty);
        _vars.Set("task.operation.message", "ready");
    }

    private void SetCommand(string command)
    {
        _vars.Set("task.operation.command", command);
        _vars.Set("task.operation.lastCommandUtc", DateTime.UtcNow);
    }

    private void HandleCommand(string command)
    {
        lock (_sync)
        {
            var normalized = command.ToLowerInvariant();
            switch (normalized)
            {
                case "start":
                    _isStarted = true;
                    ApplyLamp("green");
                    UpdateState("running", "Task started");
                    break;
                case "stop":
                    _isStarted = false;
                    ApplyLamp("red");
                    UpdateState("stopped", "Task stopped");
                    break;
                case "reset":
                    _isStarted = false;
                    ApplyLamp("yellow");
                    UpdateState("reset", "Task reset");
                    break;
                default:
                    if (normalized.StartsWith("lamp:", StringComparison.Ordinal))
                    {
                        ApplyLamp(normalized[5..]);
                        UpdateState(_isStarted ? "running" : "idle", $"Lamp set to {_lampColor}");
                    }
                    else
                    {
                        UpdateState("fault", $"Unsupported command: {command}");
                    }
                    break;
            }
        }
    }

    private void ApplyLamp(string rawColor)
    {
        var lamp = NormalizeLamp(rawColor);
        _lampColor = lamp;

        var red = lamp == "red";
        var yellow = lamp == "yellow";
        var green = lamp == "green";

        _vars.Set("task.operation.lamp", lamp);
        _vars.Set("task.operation.lamp.red", red);
        _vars.Set("task.operation.lamp.yellow", yellow);
        _vars.Set("task.operation.lamp.green", green);
        _vars.Set("task.operation.lastLampUpdateUtc", DateTime.UtcNow);

        if (_gpioDevice is null)
        {
            return;
        }

        // Optional hardware bridge. GPIO aliases can be overridden by vars at runtime.
        var redAlias = _vars.Get<string>("task.operation.alias.red") ?? "tower.red";
        var yellowAlias = _vars.Get<string>("task.operation.alias.yellow") ?? "tower.yellow";
        var greenAlias = _vars.Get<string>("task.operation.alias.green") ?? "tower.green";

        _gpioDevice.WriteOutput(redAlias, red);
        _gpioDevice.WriteOutput(yellowAlias, yellow);
        _gpioDevice.WriteOutput(greenAlias, green);
    }

    private static string NormalizeLamp(string lamp)
    {
        return lamp.ToLowerInvariant() switch
        {
            "green" => "green",
            "yellow" => "yellow",
            _ => "red"
        };
    }

    private void UpdateState(string state, string message)
    {
        _vars.Set("task.operation.state", state);
        _vars.Set("task.operation.running", _isStarted);
        _vars.Set("task.operation.message", message);
        _vars.Set("task.operation.lastActionUtc", DateTime.UtcNow);
    }
}
