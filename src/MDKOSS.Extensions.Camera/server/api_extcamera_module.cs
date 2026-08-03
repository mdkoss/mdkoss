using System.Net;
using System.Text.Json;
using MDKOSS.Extensions.Camera;

namespace MDKOSS.Core.Monitor;

/// <summary>Handles /api/extcamera/* — open, close, trigger, status for extension cameras.</summary>
public sealed class ExtCameraApiModule : MonitoringApiModule
{
    private sealed class DeviceRequest
    {
        public string? DeviceId { get; set; }
        public string? Recipe { get; set; }
    }

    public ExtCameraApiModule(MdkRuntime runtime) : base(runtime) { }

    public override string RoutePrefix => "/api/extcamera";

    public override async Task<bool> HandleAsync(
        HttpListenerContext context,
        string remainingPath,
        CancellationToken cancellationToken)
    {
        var actionPath = remainingPath.Trim('/');
        var isGet = string.Equals(context.Request.HttpMethod, "GET", StringComparison.OrdinalIgnoreCase);
        var isPost = string.Equals(context.Request.HttpMethod, "POST", StringComparison.OrdinalIgnoreCase);

        if (actionPath.Equals("status", StringComparison.OrdinalIgnoreCase) && isGet)
        {
            var deviceId = context.Request.QueryString?["deviceId"];
            if (string.IsNullOrWhiteSpace(deviceId))
            {
                await WriteErrorAsync(context.Response, "missing_device_id", cancellationToken).ConfigureAwait(false);
                return true;
            }

            await WriteStatusAsync(context.Response, deviceId, cancellationToken).ConfigureAwait(false);
            return true;
        }

        if (!isPost)
        {
            await WriteErrorAsync(context.Response, "method_not_allowed", cancellationToken).ConfigureAwait(false);
            return true;
        }

        var body = await ReadBodyAsync(context.Request, cancellationToken).ConfigureAwait(false);
        var req = Deserialize<DeviceRequest>(body);
        if (req is null || string.IsNullOrWhiteSpace(req.DeviceId))
        {
            await WriteErrorAsync(context.Response, "missing_device_id", cancellationToken).ConfigureAwait(false);
            return true;
        }

        if (!Runtime.TryGetDevice(req.DeviceId, out var dev) || dev is not ExtCameraDevice camera)
        {
            await WriteErrorAsync(context.Response, "device_not_found", cancellationToken).ConfigureAwait(false);
            return true;
        }

        switch (actionPath.ToLowerInvariant())
        {
            case "open":
                camera.Open();
                await WriteSuccessAsync(context.Response, "open", cancellationToken).ConfigureAwait(false);
                return true;
            case "close":
                camera.Close();
                await WriteSuccessAsync(context.Response, "close", cancellationToken).ConfigureAwait(false);
                return true;
            case "trigger":
            case "capture":
            {
                var result = camera.TriggerCapture(req.Recipe ?? "default");
                if (result is null)
                {
                    await WriteErrorAsync(context.Response, "camera_not_open", cancellationToken).ConfigureAwait(false);
                    return true;
                }

                var payload = JsonSerializer.Serialize(new { success = true, action = "trigger", result }, SnapshotJsonOptions);
                await WriteResponseAsync(context.Response, "application/json; charset=utf-8", payload, cancellationToken)
                    .ConfigureAwait(false);
                return true;
            }
            default:
                await WriteErrorAsync(context.Response, "unknown_action", cancellationToken).ConfigureAwait(false);
                return true;
        }
    }

    private async Task WriteStatusAsync(
        HttpListenerResponse response,
        string deviceId,
        CancellationToken cancellationToken)
    {
        if (!Runtime.TryGetDevice(deviceId, out var dev) || dev is not ExtCameraDevice camera)
        {
            await WriteErrorAsync(response, "device_not_found", cancellationToken).ConfigureAwait(false);
            return;
        }

        var payload = JsonSerializer.Serialize(new
        {
            success = true,
            deviceId = camera.Id,
            isOpen = camera.IsOpen,
            backend = camera.Parameters.Backend,
            deviceIndex = camera.Parameters.DeviceIndex,
            width = camera.Parameters.Width,
            height = camera.Parameters.Height,
            exposureMs = camera.Parameters.ExposureMs,
            captureCount = camera.CaptureCount,
            lastResult = camera.LastResult
        }, SnapshotJsonOptions);

        await WriteResponseAsync(response, "application/json; charset=utf-8", payload, cancellationToken)
            .ConfigureAwait(false);
    }
}
