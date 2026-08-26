using System.Net;

namespace MDKOSS.Core.Monitor;

/// <summary>
/// Handles GET /api/machine — cloud monitor payload matching table <c>machine</c>.
/// </summary>
public sealed class MachineApiModule : MonitoringApiModule
{
    public MachineApiModule(MdkRuntime runtime) : base(runtime) { }

    public override string RoutePrefix => "/api/machine";

    public override async Task<bool> HandleAsync(
        HttpListenerContext context,
        string remainingPath,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrEmpty(remainingPath) && remainingPath != "/")
        {
            return false;
        }

        if (!string.Equals(context.Request.HttpMethod, "GET", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var json = System.Text.Json.JsonSerializer.Serialize(Runtime.GetMachineMonitor(), SnapshotJsonOptions);
        await WriteResponseAsync(context.Response, "application/json; charset=utf-8", json, cancellationToken)
            .ConfigureAwait(false);
        return true;
    }
}
