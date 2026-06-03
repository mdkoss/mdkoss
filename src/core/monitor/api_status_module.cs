using System.Net;

namespace MDKOSS.Core.Monitor;

/// <summary>
/// Handles GET /api/status — returns the full runtime snapshot as JSON.
/// </summary>
public sealed class StatusApiModule : MonitoringApiModule
{
    public StatusApiModule(MdkRuntime runtime) : base(runtime) { }

    public override string RoutePrefix => "/api/status";

    public override async Task<bool> HandleAsync(
        HttpListenerContext context,
        string remainingPath,
        CancellationToken cancellationToken)
    {
        // Exact match only: remainingPath must be empty or "/"
        if (!string.IsNullOrEmpty(remainingPath) && remainingPath != "/")
        {
            return false;
        }

        if (!string.Equals(context.Request.HttpMethod, "GET", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var json = System.Text.Json.JsonSerializer.Serialize(Runtime.GetSnapshot(), SnapshotJsonOptions);
        await WriteResponseAsync(context.Response, "application/json; charset=utf-8", json, cancellationToken)
            .ConfigureAwait(false);
        return true;
    }
}
