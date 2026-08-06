using System.Net;
using System.Text.Json;

namespace MDKOSS.Core.Monitor;

/// <summary>Handles GET /api/tasks — runtime task snapshots.</summary>
public sealed class TasksApiModule : MonitoringApiModule
{
    public TasksApiModule(MdkRuntime runtime) : base(runtime) { }

    public override string RoutePrefix => "/api/tasks";

    public override async Task<bool> HandleAsync(
        HttpListenerContext context,
        string remainingPath,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrEmpty(remainingPath.Trim('/')))
        {
            return false;
        }

        if (!string.Equals(context.Request.HttpMethod, "GET", StringComparison.OrdinalIgnoreCase))
        {
            await WriteErrorAsync(context.Response, "method_not_allowed", cancellationToken)
                .ConfigureAwait(false);
            return true;
        }

        var tasks = Runtime.GetTaskSnapshots().Select(t => new
        {
            name = t.Name,
            type = t.Type,
            intervalMs = t.IntervalMs,
            state = t.State,
        });

        var payload = JsonSerializer.Serialize(new
        {
            success = true,
            tasks,
            timestampUtc = DateTime.UtcNow,
        }, SnapshotJsonOptions);

        await WriteResponseAsync(context.Response, "application/json; charset=utf-8", payload, cancellationToken)
            .ConfigureAwait(false);
        return true;
    }
}
