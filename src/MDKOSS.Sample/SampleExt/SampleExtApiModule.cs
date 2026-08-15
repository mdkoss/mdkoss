using System.Net;
using System.Text.Json;
using MDKOSS.Core;
using MDKOSS.Core.Monitor;

namespace MDKOSS.Sample.SampleExt;

/// <summary>Backend API example for SampleExt: <c>/api/sampleext/*</c>.</summary>
public sealed class SampleExtApiModule : MonitoringApiModule
{
    private static readonly HttpClient SharedHttp = CreateHttpClient();

    public SampleExtApiModule(MdkRuntime runtime) : base(runtime) { }

    public override string RoutePrefix => "/api/sampleext";

    public override async Task<bool> HandleAsync(
        HttpListenerContext context,
        string remainingPath,
        CancellationToken cancellationToken)
    {
        var actionPath = remainingPath.Trim('/');
        var isGet = string.Equals(context.Request.HttpMethod, "GET", StringComparison.OrdinalIgnoreCase);
        var isPost = string.Equals(context.Request.HttpMethod, "POST", StringComparison.OrdinalIgnoreCase);

        if (isGet)
        {
            if (actionPath.Equals("status", StringComparison.OrdinalIgnoreCase)
                || actionPath.Equals("dashboard", StringComparison.OrdinalIgnoreCase)
                || string.IsNullOrEmpty(actionPath))
            {
                await WriteStatusAsync(context.Response, cancellationToken).ConfigureAwait(false);
                return true;
            }

            if (actionPath.Equals("run-screenshot.png", StringComparison.OrdinalIgnoreCase)
                || actionPath.Equals("run-screenshot", StringComparison.OrdinalIgnoreCase))
            {
                await WriteScreenshotPngAsync(context.Response, cancellationToken).ConfigureAwait(false);
                return true;
            }
        }

        if (!isPost)
        {
            await WriteErrorAsync(context.Response, "method_not_allowed", cancellationToken).ConfigureAwait(false);
            return true;
        }

        switch (actionPath.ToLowerInvariant())
        {
            case "pulse":
                if (!TryGetBeacon(out var beacon) || beacon is null)
                {
                    await WriteErrorAsync(context.Response, "beacon_not_found", cancellationToken).ConfigureAwait(false);
                    return true;
                }

                var count = beacon.Pulse("api pulse");
                await WriteJsonAsync(context.Response, new { success = true, action = "pulse", pulseCount = count }, cancellationToken)
                    .ConfigureAwait(false);
                return true;

            case "reset":
                if (TryGetBeacon(out var b) && b is not null)
                {
                    b.Reset();
                }

                Runtime.Vars.Set("sample.motion.command", "reset");
                await WriteSuccessAsync(context.Response, "reset", cancellationToken).ConfigureAwait(false);
                return true;

            case "motionstart":
                Runtime.Vars.Set("sample.motion.command", "start");
                await WriteSuccessAsync(context.Response, "motionstart", cancellationToken).ConfigureAwait(false);
                return true;

            case "motionstop":
                Runtime.Vars.Set("sample.motion.command", "stop");
                await WriteSuccessAsync(context.Response, "motionstop", cancellationToken).ConfigureAwait(false);
                return true;

            case "publish-dingtalk":
                await HandlePublishDingTalkAsync(context, cancellationToken).ConfigureAwait(false);
                return true;

            default:
                await WriteErrorAsync(context.Response, "unknown_action", cancellationToken).ConfigureAwait(false);
                return true;
        }
    }

    private async Task WriteScreenshotPngAsync(HttpListenerResponse response, CancellationToken cancellationToken)
    {
        var png = SampleRunScreenshot.RenderPng(Runtime.GetSnapshot());
        TrySaveScreenshot(png);
        response.StatusCode = (int)HttpStatusCode.OK;
        response.ContentType = "image/png";
        response.ContentLength64 = png.Length;
        await response.OutputStream.WriteAsync(png, cancellationToken).ConfigureAwait(false);
        response.OutputStream.Close();
    }

    private async Task HandlePublishDingTalkAsync(HttpListenerContext context, CancellationToken cancellationToken)
    {
        string? webhook = null;
        string? imageUploadUrl = null;
        if (context.Request.HasEntityBody)
        {
            using var reader = new StreamReader(context.Request.InputStream, context.Request.ContentEncoding);
            var raw = await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(raw))
            {
                try
                {
                    using var doc = JsonDocument.Parse(raw);
                    if (doc.RootElement.TryGetProperty("webhook", out var wh))
                    {
                        webhook = wh.GetString();
                    }

                    if (doc.RootElement.TryGetProperty("imageUploadUrl", out var up))
                    {
                        imageUploadUrl = up.GetString();
                    }
                }
                catch (JsonException)
                {
                    await WriteErrorAsync(context.Response, "invalid_json", cancellationToken).ConfigureAwait(false);
                    return;
                }
            }
        }

        webhook = SampleDingTalkPublisher.ResolveWebhook(webhook);
        if (string.IsNullOrWhiteSpace(webhook))
        {
            await WriteErrorAsync(
                context.Response,
                $"webhook_missing (set body.webhook or env {SampleDingTalkPublisher.WebhookEnvVar})",
                cancellationToken).ConfigureAwait(false);
            return;
        }

        var snapshot = Runtime.GetSnapshot();
        var png = SampleRunScreenshot.RenderPng(snapshot);
        var savedPath = TrySaveScreenshot(png);
        var result = await SampleDingTalkPublisher.PublishAsync(
            SharedHttp,
            webhook,
            snapshot,
            png,
            imageUploadUrl,
            cancellationToken).ConfigureAwait(false);

        await WriteJsonAsync(
            context.Response,
            new
            {
                success = result.Success,
                action = "publish-dingtalk",
                error = result.Error,
                pngBytes = png.Length,
                savedPath,
                imageUrl = result.ImageUrl,
                dingtalk = result.ResponseBody,
            },
            cancellationToken).ConfigureAwait(false);
    }

    private static string? TrySaveScreenshot(byte[] png)
    {
        try
        {
            var dir = Path.Combine(AppContext.BaseDirectory, "logs");
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, "sample-run-screenshot.png");
            File.WriteAllBytes(path, png);
            return path;
        }
        catch
        {
            return null;
        }
    }

    private static HttpClient CreateHttpClient()
    {
        var handler = new HttpClientHandler { UseProxy = false };
        return new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(20) };
    }

    private bool TryGetBeacon(out SampleBeaconDevice? beacon)
    {
        beacon = null;
        if (!Runtime.TryGetDevice("sample-beacon", out var device) || device is not SampleBeaconDevice typed)
        {
            // Fall back: first samplebeacon device if id differs.
            foreach (var id in Runtime.GetSnapshot().Devices.Keys)
            {
                if (Runtime.TryGetDevice(id, out var d) && d is SampleBeaconDevice found)
                {
                    beacon = found;
                    return true;
                }
            }

            return false;
        }

        beacon = typed;
        return true;
    }

    private Task WriteStatusAsync(HttpListenerResponse response, CancellationToken cancellationToken)
    {
        var vars = Runtime.GetSnapshot().Vars;
        object Pick(string key) => vars.TryGetValue(key, out var v) ? v ?? "" : "";

        SampleBeaconDevice? beacon = null;
        _ = TryGetBeacon(out beacon);

        var payload = JsonSerializer.Serialize(new
        {
            success = true,
            extension = "sample-ext",
            beacon = beacon is null
                ? null
                : new
                {
                    id = beacon.Id,
                    label = beacon.Label,
                    pulseCount = beacon.PulseCount,
                    message = beacon.Message,
                    state = beacon.State.ToString(),
                },
            motion = new
            {
                phase = Pick("sample.motion.phase"),
                message = Pick("sample.motion.message"),
                cycleCount = Pick("sample.motion.cycleCount"),
            },
            timestampUtc = DateTime.UtcNow,
        }, SnapshotJsonOptions);

        response.StatusCode = (int)HttpStatusCode.OK;
        return WriteResponseAsync(response, "application/json; charset=utf-8", payload, cancellationToken);
    }

    private static Task WriteJsonAsync(HttpListenerResponse response, object body, CancellationToken cancellationToken)
    {
        response.StatusCode = (int)HttpStatusCode.OK;
        var payload = JsonSerializer.Serialize(body, SnapshotJsonOptions);
        return WriteResponseAsync(response, "application/json; charset=utf-8", payload, cancellationToken);
    }
}
