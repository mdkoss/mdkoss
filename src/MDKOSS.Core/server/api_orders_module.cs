using System.Net;
using System.Text.Json;
using MDKOSS.Core.Data;

namespace MDKOSS.Core.Monitor;

/// <summary>Handles GET/POST/DELETE /api/orders — production order queue (排单).</summary>
public sealed class OrdersApiModule : MonitoringApiModule
{
    public OrdersApiModule(MdkRuntime runtime) : base(runtime) { }

    public override string RoutePrefix => "/api/orders";

    public override async Task<bool> HandleAsync(
        HttpListenerContext context,
        string remainingPath,
        CancellationToken cancellationToken)
    {
        var segments = remainingPath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        var method = context.Request.HttpMethod ?? "GET";

        if (segments.Length == 0)
        {
            if (string.Equals(method, "GET", StringComparison.OrdinalIgnoreCase))
            {
                var status = GetQueryValue(context.Request.Url?.Query ?? string.Empty, "status");
                var orders = Runtime.DataStore.ListOrders(status);
                var json = JsonSerializer.Serialize(orders, SnapshotJsonOptions);
                await WriteResponseAsync(context.Response, "application/json; charset=utf-8", json, cancellationToken)
                    .ConfigureAwait(false);
                return true;
            }

            if (string.Equals(method, "POST", StringComparison.OrdinalIgnoreCase))
            {
                var body = await ReadBodyAsync(context.Request, cancellationToken).ConfigureAwait(false);
                var order = Deserialize<ProductionOrderRecord>(body);
                if (order is null)
                {
                    await WriteErrorAsync(context.Response, "invalid_body", cancellationToken).ConfigureAwait(false);
                    return true;
                }

                var ok = Runtime.TryUpsertOrder(order, out var error);
                await WriteMutationResultAsync(context.Response, ok, error, cancellationToken).ConfigureAwait(false);
                return true;
            }

            return false;
        }

        var orderId = Uri.UnescapeDataString(segments[0]);
        if (string.Equals(method, "GET", StringComparison.OrdinalIgnoreCase))
        {
            if (!Runtime.DataStore.TryGetOrder(orderId, out var order) || order is null)
            {
                context.Response.StatusCode = (int)HttpStatusCode.NotFound;
                await WriteErrorAsync(context.Response, "order_not_found", cancellationToken).ConfigureAwait(false);
                return true;
            }

            var json = JsonSerializer.Serialize(order, SnapshotJsonOptions);
            await WriteResponseAsync(context.Response, "application/json; charset=utf-8", json, cancellationToken)
                .ConfigureAwait(false);
            return true;
        }

        if (string.Equals(method, "DELETE", StringComparison.OrdinalIgnoreCase))
        {
            var ok = Runtime.TryDeleteOrder(orderId, out var error);
            await WriteMutationResultAsync(context.Response, ok, error, cancellationToken).ConfigureAwait(false);
            return true;
        }

        return false;
    }

    private static Task WriteMutationResultAsync(
        HttpListenerResponse response,
        bool success,
        string? error,
        CancellationToken cancellationToken)
    {
        response.StatusCode = success ? (int)HttpStatusCode.OK : (int)HttpStatusCode.BadRequest;
        var payload = JsonSerializer.Serialize(new { success, error }, SnapshotJsonOptions);
        return WriteResponseAsync(response, "application/json; charset=utf-8", payload, cancellationToken);
    }
}
