using MDKOSS.Core;
using System.Text.Json;

namespace MDKOSS.Tasks;

/// <summary>
/// Periodic aggregation task that syncs IO / device / task status into MVar.
/// </summary>
public sealed class TaskCycleTask : MTaskBase
{
    private readonly MVarStore _vars;
    private readonly Func<RuntimeSnapshot> _snapshotProvider;
    private readonly Func<IReadOnlyList<MTaskBase>> _taskProvider;

    public TaskCycleTask(
        MVarStore vars,
        Func<RuntimeSnapshot> snapshotProvider,
        Func<IReadOnlyList<MTaskBase>> taskProvider,
        int intervalMs = 1000)
        : base("task-cycle", intervalMs)
    {
        _vars = vars;
        _snapshotProvider = snapshotProvider;
        _taskProvider = taskProvider;
    }

    protected override Task TickAsync(CancellationToken cancellationToken)
    {
        var snapshot = _snapshotProvider();
        var tasks = _taskProvider();

        var ioTotal = snapshot.Drivers.Count;
        var ioOnline = snapshot.Drivers.Values.Count(d => d.IsConnected);
        var ioOffline = ioTotal - ioOnline;

        var devTotal = snapshot.Devices.Count;
        var devRunning = snapshot.Devices.Values.Count(d =>
            string.Equals(d.State, MDeviceState.Running.ToString(), StringComparison.OrdinalIgnoreCase));
        var devFault = snapshot.Devices.Values.Count(d =>
            string.Equals(d.State, MDeviceState.Fault.ToString(), StringComparison.OrdinalIgnoreCase));

        var taskTotal = tasks.Count;
        var taskRunning = tasks.Count(t => t.State == MTaskState.Running);
        var taskFault = tasks.Count(t => t.State == MTaskState.Fault);
        var taskStopped = tasks.Count(t => t.State == MTaskState.Stopped);

        _vars.Set("task.cycle.runtime.projectName", snapshot.ProjectName);
        _vars.Set("task.cycle.runtime.isRunning", snapshot.IsRunning);

        _vars.Set("task.cycle.io.total", ioTotal);
        _vars.Set("task.cycle.io.online", ioOnline);
        _vars.Set("task.cycle.io.offline", ioOffline);

        _vars.Set("task.cycle.dev.total", devTotal);
        _vars.Set("task.cycle.dev.running", devRunning);
        _vars.Set("task.cycle.dev.fault", devFault);

        _vars.Set("task.cycle.task.total", taskTotal);
        _vars.Set("task.cycle.task.running", taskRunning);
        _vars.Set("task.cycle.task.fault", taskFault);
        _vars.Set("task.cycle.task.stopped", taskStopped);

        var taskStates = tasks
            .OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                t => t.Name,
                t => t.State.ToString(),
                StringComparer.OrdinalIgnoreCase);
        _vars.Set("task.cycle.task.states.json", JsonSerializer.Serialize(taskStates));

        _vars.Set("task.cycle.lastTickUtc", DateTime.UtcNow);
        _vars.Set("task.cycle.alive", true);
        return Task.CompletedTask;
    }
}
