using System.Net;
using System.Text.Json;

namespace MDKOSS.Core.Monitor;

/// <summary>
/// Handles POST /api/task/* — task operations (start, stop, reset, lamp).
/// </summary>
public sealed class TaskApiModule : MonitoringApiModule
{
    public TaskApiModule(MdkRuntime runtime) : base(runtime) { }

    public override string RoutePrefix => "/api/task";

    public override async Task<bool> HandleAsync(
        HttpListenerContext context,
        string remainingPath,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(context.Request.HttpMethod, "POST", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var actionPath = remainingPath.Trim('/');
        var query = context.Request.Url?.Query ?? string.Empty;
        string command;
        string actionLabel;

        switch (actionPath.ToLowerInvariant())
        {
            case "start":
                command = "start";
                actionLabel = "start";
                break;
            case "stop":
                command = "stop";
                actionLabel = "stop";
                break;
            case "pause":
                command = "pause";
                actionLabel = "pause";
                break;
            case "reset":
                command = "reset";
                actionLabel = "reset";
                break;
            case "lamp":
                var color = GetQueryValue(query, "color") ?? "red";
                command = $"lamp:{color}";
                actionLabel = $"lamp:{color}";
                break;
            default:
                await WriteTaskOperationResultAsync(context.Response, false, "unknown_action", cancellationToken)
                    .ConfigureAwait(false);
                return true;
        }

        if (command is "start" or "stop" or "reset" or "pause")
        {
            Runtime.Vars.Set("machine.command", command);
        }

        if (command is not "pause")
        {
            Runtime.Vars.Set("task.operation.command", command);
        }

        await WriteTaskOperationResultAsync(context.Response, true, actionLabel, cancellationToken)
            .ConfigureAwait(false);
        return true;
    }

    private static Task WriteTaskOperationResultAsync(
        HttpListenerResponse response,
        bool success,
        string action,
        CancellationToken cancellationToken)
    {
        response.StatusCode = success ? (int)HttpStatusCode.OK : (int)HttpStatusCode.BadRequest;
        var payload = JsonSerializer.Serialize(new
        {
            success,
            action,
            timestampUtc = DateTime.UtcNow
        });
        return WriteResponseAsync(response, "application/json; charset=utf-8", payload, cancellationToken);
    }
}
