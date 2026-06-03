using System.Net;
using System.Text.Json;

namespace MDKOSS.Core.Monitor;

/// <summary>
/// Handles POST /api/io/write — writes a digital output value to a device.
/// </summary>
public sealed class IoApiModule : MonitoringApiModule
{
    private sealed class IoWriteRequest
    {
        public string? DeviceId { get; set; }
        public string? Alias { get; set; }
        public bool? Value { get; set; }
    }

    public IoApiModule(MdkRuntime runtime) : base(runtime) { }

    public override string RoutePrefix => "/api/io";

    public override async Task<bool> HandleAsync(
        HttpListenerContext context,
        string remainingPath,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(remainingPath, "/write", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!string.Equals(context.Request.HttpMethod, "POST", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        await HandleIoWriteAsync(context, cancellationToken).ConfigureAwait(false);
        return true;
    }

    private async Task HandleIoWriteAsync(HttpListenerContext context, CancellationToken cancellationToken)
    {
        var body = await ReadBodyAsync(context.Request, cancellationToken).ConfigureAwait(false);

        IoWriteRequest? req;
        try
        {
            req = Deserialize<IoWriteRequest>(body);
        }
        catch (System.Text.Json.JsonException)
        {
            context.Response.StatusCode = (int)HttpStatusCode.BadRequest;
            await WriteResponseAsync(
                    context.Response,
                    "application/json; charset=utf-8",
                    """{"success":false,"error":"invalid_json"}""",
                    cancellationToken)
                .ConfigureAwait(false);
            return;
        }

        if (req is null
            || string.IsNullOrWhiteSpace(req.DeviceId)
            || string.IsNullOrWhiteSpace(req.Alias)
            || req.Value is null)
        {
            context.Response.StatusCode = (int)HttpStatusCode.BadRequest;
            await WriteResponseAsync(
                    context.Response,
                    "application/json; charset=utf-8",
                    """{"success":false,"error":"missing_fields"}""",
                    cancellationToken)
                .ConfigureAwait(false);
            return;
        }

        if (!Runtime.TryWriteDigitalOutput(req.DeviceId.Trim(), req.Alias.Trim(), req.Value.Value, out var err))
        {
            context.Response.StatusCode = (int)HttpStatusCode.BadRequest;
            var payload = System.Text.Json.JsonSerializer.Serialize(
                new { success = false, error = err ?? "write_failed" },
                SnapshotJsonOptions);
            await WriteResponseAsync(context.Response, "application/json; charset=utf-8", payload, cancellationToken)
                .ConfigureAwait(false);
            return;
        }

        var ok = System.Text.Json.JsonSerializer.Serialize(new { success = true }, SnapshotJsonOptions);
        await WriteResponseAsync(context.Response, "application/json; charset=utf-8", ok, cancellationToken)
            .ConfigureAwait(false);
    }
}
