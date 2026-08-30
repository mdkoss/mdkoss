using MDKOSS.Core;
using MDKOSS.Core.Flow;

namespace MDKOSS.Tasks;

/// <summary>
/// Periodic task that loads <c>parameters.flowJson</c> and pumps a <see cref="FlowInterpreter"/>.
/// </summary>
public sealed class FlowTask : MTaskBase
{
    private readonly MVarStore _vars;
    private readonly FlowInterpreter _interpreter;
    private readonly bool _loop; // restart main when completed

    public FlowTask(
        string name,
        int intervalMs,
        FlowDocument document,
        MVarStore vars,
        IFlowRuntimeHost? host = null,
        bool loop = true,
        bool autoStart = true)
        : base(name, intervalMs)
    {
        _vars = vars;
        _loop = loop;
        _interpreter = new FlowInterpreter(document, name, vars, host);
        if (autoStart)
        {
            _interpreter.Reset();
        }
    }

    public FlowRunState FlowState => _interpreter.State;

    public string? ProgramCounter => _interpreter.ProgramCounter;

    public string? LastError => _interpreter.LastError;

    /// <summary>Builds from task config; throws when flowJson invalid.</summary>
    public static FlowTask Create(
        MdkSetting.TaskConfig config,
        MVarStore vars,
        IFlowRuntimeHost? host = null)
    {
        var name = string.IsNullOrWhiteSpace(config.Name) ? "flow" : config.Name.Trim();
        if (!config.Parameters.TryGetValue("flowJson", out var json) || string.IsNullOrWhiteSpace(json))
        {
            if (config.Parameters.TryGetValue("flowFile", out var flowFile)
                && !string.IsNullOrWhiteSpace(flowFile)
                && TryReadFlowFile(flowFile, out var fileJson))
            {
                json = fileJson;
            }
            else
            {
                // allow empty → empty start/end document
                json = FlowDocument.CreateEmpty().ToJson();
            }
        }

        if (!FlowDocument.TryParse(json, out var doc, out var parseError))
        {
            throw new InvalidOperationException($"Invalid flowJson: {parseError}");
        }

        var errors = doc.Validate();
        if (errors.Count > 0)
        {
            throw new InvalidOperationException("flowJson validation failed: " + string.Join("; ", errors));
        }

        var loop = true;
        if (config.Parameters.TryGetValue("loop", out var loopRaw)
            && bool.TryParse(loopRaw, out var loopParsed))
        {
            loop = loopParsed;
        }

        var autoStart = true;
        if (config.Parameters.TryGetValue("autoStart", out var autoRaw)
            && bool.TryParse(autoRaw, out var autoParsed))
        {
            autoStart = autoParsed;
        }

        return new FlowTask(name, config.IntervalMs, doc, vars, host, loop, autoStart);
    }

    /// <summary>Restarts the interpreter so an on-demand flow can run again.</summary>
    public void Reset(bool reinitializeVariables = true)
    {
        _interpreter.Reset(reinitializeVariables);
        State = MTaskState.Idle;
    }

    /// <summary>Stops a running / waiting flow and returns it to idle.</summary>
    public void Halt()
    {
        _interpreter.Halt();
        State = MTaskState.Stopped;
    }

    protected override Task TickAsync(CancellationToken cancellationToken)
    {
        if (_interpreter.State == FlowRunState.Completed && _loop)
        {
            // Keep locals across loop so counters / state persist (only first Start re-inits).
            _interpreter.Reset(reinitializeVariables: false);
        }

        if (_interpreter.State is FlowRunState.Running or FlowRunState.Waiting)
        {
            _interpreter.Pump(256);
        }

        if (_interpreter.State == FlowRunState.Fault)
        {
            State = MTaskState.Fault;
            _vars.Set($"task.{Name}.fault", _interpreter.LastError ?? "fault");
        }

        return Task.CompletedTask;
    }

    /// <summary>Resolves <c>flowFile</c> relative to the process base directory when not rooted.</summary>
    public static bool TryReadFlowFile(string flowFile, out string json)
    {
        json = "";
        if (string.IsNullOrWhiteSpace(flowFile))
        {
            return false;
        }

        var path = Path.IsPathRooted(flowFile)
            ? flowFile
            : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, flowFile.Trim()));
        if (!File.Exists(path))
        {
            return false;
        }

        json = File.ReadAllText(path);
        return !string.IsNullOrWhiteSpace(json);
    }

    public static string ResolveFlowFilePath(string flowFile)
    {
        if (string.IsNullOrWhiteSpace(flowFile))
        {
            throw new ArgumentException("flowFile is empty.", nameof(flowFile));
        }

        return Path.IsPathRooted(flowFile)
            ? flowFile
            : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, flowFile.Trim()));
    }
}
