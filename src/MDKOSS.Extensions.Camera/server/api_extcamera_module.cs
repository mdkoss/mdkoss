using System.Net;
using System.Text.Json;
using MDKOSS.Extensions.Camera;

namespace MDKOSS.Core.Monitor;

/// <summary>
/// Handles /api/extcamera/* — catalog, enumeration, open/close, trigger, live image and
/// runtime exposure/gain/trigger tuning for extension cameras.
/// </summary>
public sealed class ExtCameraApiModule : MonitoringApiModule
{
    private sealed class DeviceRequest
    {
        public string? DeviceId { get; set; }
        public string? Recipe { get; set; }
        public double? ExposureUs { get; set; }
        public double? ExposureMs { get; set; }
        public double? Gain { get; set; }
        public string? TriggerMode { get; set; }
    }

    public ExtCameraApiModule(MdkRuntime runtime) : base(runtime) { }

    public override string RoutePrefix => "/api/extcamera";

    public override async Task<bool> HandleAsync(
        HttpListenerContext context,
        string remainingPath,
        CancellationToken cancellationToken)
    {
        var actionPath = remainingPath.Trim('/').ToLowerInvariant();
        var isGet = string.Equals(context.Request.HttpMethod, "GET", StringComparison.OrdinalIgnoreCase);
        var isPost = string.Equals(context.Request.HttpMethod, "POST", StringComparison.OrdinalIgnoreCase);

        if (isGet)
        {
            switch (actionPath)
            {
                case "catalog":
                case "backends":
                    await WriteJsonAsync(
                            context.Response,
                            new { success = true, backends = CameraCatalog.All },
                            cancellationToken)
                        .ConfigureAwait(false);
                    return true;
                case "status":
                case "list":
                case "image":
                {
                    var deviceId = context.Request.QueryString?["deviceId"];
                    if (!TryResolve(deviceId, out var camera))
                    {
                        await WriteErrorAsync(
                                context.Response,
                                string.IsNullOrWhiteSpace(deviceId) ? "missing_device_id" : "device_not_found",
                                cancellationToken)
                            .ConfigureAwait(false);
                        return true;
                    }

                    if (actionPath == "status")
                    {
                        await WriteJsonAsync(
                                context.Response,
                                Merge(ExtCameraDeviceActions.Snapshot(camera)),
                                cancellationToken)
                            .ConfigureAwait(false);
                        return true;
                    }

                    if (actionPath == "list")
                    {
                        await WriteJsonAsync(
                                context.Response,
                                new { success = true, deviceId = camera.Id, devices = camera.Enumerate() },
                                cancellationToken)
                            .ConfigureAwait(false);
                        return true;
                    }

                    await WriteImageAsync(context, camera, cancellationToken).ConfigureAwait(false);
                    return true;
                }
            }
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

        if (!TryResolve(req.DeviceId, out var device))
        {
            await WriteErrorAsync(context.Response, "device_not_found", cancellationToken).ConfigureAwait(false);
            return true;
        }

        switch (actionPath)
        {
            case "open":
                if (!device.Open())
                {
                    await WriteErrorAsync(context.Response, Reason(device, "open_failed"), cancellationToken)
                        .ConfigureAwait(false);
                    return true;
                }

                await WriteSuccessAsync(context.Response, "open", cancellationToken).ConfigureAwait(false);
                return true;
            case "close":
                device.Close();
                await WriteSuccessAsync(context.Response, "close", cancellationToken).ConfigureAwait(false);
                return true;
            case "startgrab":
                if (!device.StartGrab())
                {
                    await WriteErrorAsync(context.Response, Reason(device, "start_grab_failed"), cancellationToken)
                        .ConfigureAwait(false);
                    return true;
                }

                await WriteSuccessAsync(context.Response, "startgrab", cancellationToken).ConfigureAwait(false);
                return true;
            case "stopgrab":
                device.StopGrab();
                await WriteSuccessAsync(context.Response, "stopgrab", cancellationToken).ConfigureAwait(false);
                return true;
            case "param":
                await WriteParamAsync(context.Response, device, req, cancellationToken).ConfigureAwait(false);
                return true;
            case "trigger":
            case "capture":
            {
                var result = device.TriggerCapture(req.Recipe ?? "default");
                if (result is null)
                {
                    await WriteErrorAsync(
                            context.Response,
                            device.IsOpen ? Reason(device, "grab_failed") : "camera_not_open",
                            cancellationToken)
                        .ConfigureAwait(false);
                    return true;
                }

                await WriteJsonAsync(
                        context.Response,
                        new { success = true, action = "trigger", result },
                        cancellationToken)
                    .ConfigureAwait(false);
                return true;
            }
            default:
                await WriteErrorAsync(context.Response, "unknown_action", cancellationToken).ConfigureAwait(false);
                return true;
        }
    }

    private async Task WriteParamAsync(
        HttpListenerResponse response,
        ExtCameraDevice camera,
        DeviceRequest req,
        CancellationToken cancellationToken)
    {
        if (!camera.IsOpen)
        {
            await WriteErrorAsync(response, "camera_not_open", cancellationToken).ConfigureAwait(false);
            return;
        }

        var applied = new List<string>();
        var exposureUs = req.ExposureUs ?? (req.ExposureMs is { } ms ? ms * 1000 : null);
        if (exposureUs is { } us && camera.SetExposure(us))
        {
            applied.Add("exposureUs");
        }

        if (req.Gain is { } gain && camera.SetGain(gain))
        {
            applied.Add("gain");
        }

        if (!string.IsNullOrWhiteSpace(req.TriggerMode)
            && camera.SetTrigger(ExtCameraDeviceParameters.ParseTrigger(req.TriggerMode)))
        {
            applied.Add("triggerMode");
        }

        if (applied.Count == 0)
        {
            await WriteErrorAsync(response, Reason(camera, "no_parameter_applied"), cancellationToken)
                .ConfigureAwait(false);
            return;
        }

        await WriteJsonAsync(response, new { success = true, action = "param", applied }, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task WriteImageAsync(
        HttpListenerContext context,
        ExtCameraDevice camera,
        CancellationToken cancellationToken)
    {
        var format = context.Request.QueryString?["format"] ?? "png";
        var bytes = camera.EncodeLastFrame(format);
        if (bytes.Length == 0)
        {
            await WriteErrorAsync(context.Response, "no_frame", cancellationToken).ConfigureAwait(false);
            return;
        }

        var response = context.Response;
        response.ContentType = CameraPixel.ContentType(format);
        response.ContentLength64 = bytes.Length;
        await response.OutputStream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
        response.OutputStream.Close();
    }

    private bool TryResolve(string? deviceId, out ExtCameraDevice camera)
    {
        camera = null!;
        if (string.IsNullOrWhiteSpace(deviceId) || !Runtime.TryGetDevice(deviceId, out var dev))
        {
            return false;
        }

        if (dev is not ExtCameraDevice found)
        {
            return false;
        }

        camera = found;
        return true;
    }

    private static string Reason(ExtCameraDevice camera, string fallback) =>
        string.IsNullOrWhiteSpace(camera.LastError) ? fallback : camera.LastError;

    private static Dictionary<string, object?> Merge(object snapshot)
    {
        var map = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase) { ["success"] = true };
        foreach (var property in snapshot.GetType().GetProperties())
        {
            map[property.Name] = property.GetValue(snapshot);
        }

        return map;
    }

    private Task WriteJsonAsync(HttpListenerResponse response, object payload, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(payload, SnapshotJsonOptions);
        return WriteResponseAsync(response, "application/json; charset=utf-8", json, cancellationToken);
    }
}
