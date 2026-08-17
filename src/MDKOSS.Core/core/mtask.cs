using MDKOSS.Core.Drivers;
using System.Diagnostics;

namespace MDKOSS.Core;

public enum MTaskState
{
    Idle,
    Running,
    Fault,
    Stopped
}

/// <summary>
/// Thrown when a motion task observes <c>machine.state</c> of stopped / fault and must abort.
/// </summary>
public sealed class MachineStoppedException : InvalidOperationException
{
    public string MachineState { get; }

    public MachineStoppedException(string taskName, string machineState)
        : base($"Motion task '{taskName}' aborted: machine.state={machineState}")
    {
        MachineState = machineState;
    }
}

/// <summary>
/// Base contract for periodic runtime tasks.
/// </summary>
public abstract class MTaskBase
{
    public string Name { get; }

    public int IntervalMs { get; }

    public MTaskState State { get; internal set; } = MTaskState.Idle;

    protected MTaskBase(string name, int intervalMs)
    {
        Name = name;
        IntervalMs = Math.Max(1, intervalMs);
    }

    /// <summary>
    /// Executes a single task tick and updates task state.
    /// </summary>
    public virtual async Task ExecuteOnceAsync(CancellationToken cancellationToken)
    {
        try
        {
            State = MTaskState.Running;
            await TickAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            State = MTaskState.Fault;
            throw;
        }
    }

    protected abstract Task TickAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Minimal health task: reports driver alive heartbeat into vars.
/// </summary>
public sealed class PollDriverTask : MTaskBase
{
    private readonly IDriver _driver;
    private readonly MVarStore _vars;

    public PollDriverTask(string name, int intervalMs, IDriver driver, MVarStore vars)
        : base(name, intervalMs)
    {
        _driver = driver;
        _vars = vars;
    }

    protected override Task TickAsync(CancellationToken cancellationToken)
    {
        _vars.Set($"{Name}.alive", _driver.IsConnected);
        _vars.Set($"{Name}.lastTickUtc", DateTime.UtcNow);
        return Task.CompletedTask;
    }
}

/// <summary>
/// Lightweight scheduler running each registered task in its own loop.
/// </summary>
public sealed class MTaskScheduler : IDisposable
{
    private readonly List<MTaskBase> _tasks = [];
    private readonly List<Task> _workers = [];
    private CancellationTokenSource? _cts;

    public IReadOnlyList<MTaskBase> Tasks => _tasks;

    /// <summary>Registers a task before scheduler start.</summary>
    public void Register(MTaskBase task)
    {
        _tasks.Add(task);
    }

    /// <summary>Starts all worker loops.</summary>
    public void Start()
    {
        if (_cts is not null)
        {
            throw new InvalidOperationException("Scheduler has already started.");
        }

        _cts = new CancellationTokenSource();
        foreach (var task in _tasks)
        {
            var captured = task;
            _workers.Add(Task.Factory.StartNew(
                    () => RunLoopAsync(captured, _cts.Token),
                    _cts.Token,
                    TaskCreationOptions.LongRunning | TaskCreationOptions.DenyChildAttach,
                    TaskScheduler.Default)
                .Unwrap());
        }
    }

    /// <summary>Stops all loops and marks tasks as stopped.</summary>
    public async Task StopAsync()
    {
        if (_cts is null)
        {
            return;
        }

        _cts.Cancel();
        try
        {
            await Task.WhenAll(_workers).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Expected when the scheduler stops.
        }
        foreach (var task in _tasks)
        {
            task.State = MTaskState.Stopped;
        }

        _workers.Clear();
        _cts.Dispose();
        _cts = null;
    }

    // Core scheduling loop with cooperative cancellation.
    private static async Task RunLoopAsync(MTaskBase task, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var started = Stopwatch.GetTimestamp();
            try
            {
                await task.ExecuteOnceAsync(cancellationToken).ConfigureAwait(false);
                var remaining = task.IntervalMs - (int)Stopwatch.GetElapsedTime(started).TotalMilliseconds;
                if (remaining > 0)
                {
                    await Task.Delay(remaining, cancellationToken).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (MachineStoppedException)
            {
                // Motion tick aborted by machine stop; keep polling so a later start can resume.
            }
        }
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _cts?.Dispose();
    }
}
