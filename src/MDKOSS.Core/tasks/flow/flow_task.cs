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
        bool loop = true)
        : base(name, intervalMs)
    {
        _vars = vars;
        _loop = loop;
        _interpreter = new FlowInterpreter(document, name, vars, host);
        _interpreter.Reset();
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
            // allow empty → empty start/end document
            json = FlowDocument.CreateEmpty().ToJson();
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

        return new FlowTask(name, config.IntervalMs, doc, vars, host, loop);
    }

    protected override Task TickAsync(CancellationToken cancellationToken)
    {
        if (_interpreter.State == FlowRunState.Completed && _loop)
        {
            _interpreter.Reset();
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
}
