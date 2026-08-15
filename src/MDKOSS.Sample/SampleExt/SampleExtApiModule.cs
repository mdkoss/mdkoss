using System.Net;
using System.Text.Json;
using MDKOSS.Core;
using MDKOSS.Core.Monitor;

namespace MDKOSS.Sample.SampleExt;

/// <summary>Backend API example for SampleExt: <c>/api/sampleext/*</c>.</summary>
public sealed class SampleExtApiModule : MonitoringApiModule
{
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

            default:
                await WriteErrorAsync(context.Response, "unknown_action", cancellationToken).ConfigureAwait(false);
                return true;
        }
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
