using System.Net;
using System.Text.Json;

namespace MDKOSS.Core.Monitor;

/// <summary>Handles /api/alarms — evaluate, ack, reset, and optional test trigger.</summary>
public sealed class AlarmsApiModule : MonitoringApiModule
{
    private readonly MdkAlarmHub _hub = new();

    public AlarmsApiModule(MdkRuntime runtime) : base(runtime) { }

    public override string RoutePrefix => "/api/alarms";

    public override async Task<bool> HandleAsync(
        HttpListenerContext context,
        string remainingPath,
        CancellationToken cancellationToken)
    {
        var path = remainingPath.Trim('/');
        var method = context.Request.HttpMethod ?? "GET";
        var isGet = method.Equals("GET", StringComparison.OrdinalIgnoreCase);
        var isPost = method.Equals("POST", StringComparison.OrdinalIgnoreCase);

        try
        {
            if (string.IsNullOrEmpty(path) && isGet)
            {
                await WriteListAsync(context.Response, cancellationToken).ConfigureAwait(false);
                return true;
            }

            if (path.Equals("ack", StringComparison.OrdinalIgnoreCase) && isPost)
            {
                await HandleAckAsync(context, cancellationToken).ConfigureAwait(false);
                return true;
            }

            if (path.Equals("reset", StringComparison.OrdinalIgnoreCase) && isPost)
            {
                _hub.Reset();
                Runtime.AlarmManager.ClearAll();
                Runtime.Vars.Set("alarm.test", false);
                await WriteListAsync(context.Response, cancellationToken).ConfigureAwait(false);
                return true;
            }

            if (path.Equals("test", StringComparison.OrdinalIgnoreCase) && isPost)
            {
                Runtime.Vars.Set("alarm.test", true);
                if (!Runtime.AlarmManager.Trigger("alm-demo", out _))
                {
                    Runtime.AlarmManager.Trigger("alm-demo", out _, allowAdHoc: true, msgOverride: "演示报警");
                }
                await WriteListAsync(context.Response, cancellationToken).ConfigureAwait(false);
                return true;
            }

            return false;
        }
        catch (JsonException)
        {
            await WriteErrorAsync(context.Response, "invalid_json", cancellationToken).ConfigureAwait(false);
            return true;
        }
    }

    private Task WriteListAsync(HttpListenerResponse response, CancellationToken cancellationToken)
    {
        var items = _hub.Evaluate(Runtime);
        return WriteJsonAsync(response, new
        {
            success = true,
            activeCount = items.Count(a => a.Active),
            errorCount = items.Count(a => a.Active && a.Level == "error"),
            warnCount = items.Count(a => a.Active && a.Level == "warn"),
            unackedCount = items.Count(a => a.Active && !a.Acked),
            items,
            timestampUtc = DateTime.UtcNow,
        }, cancellationToken);
    }

    private async Task HandleAckAsync(HttpListenerContext context, CancellationToken cancellationToken)
    {
        var id = GetQueryValue(context.Request.Url?.Query, "id");
        var all = GetQueryValue(context.Request.Url?.Query, "all");
        if (string.IsNullOrWhiteSpace(id))
        {
            var body = await ReadBodyAsync(context.Request, cancellationToken).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(body))
            {
                var req = Deserialize<AckRequest>(body);
                id = req?.Id;
                if (req?.All == true)
                {
                    all = "1";
                }
            }
        }

        if (IsTruthy(all))
        {
            _hub.AckAll();
            Runtime.AlarmManager.ClearAll();
        }
        else if (!string.IsNullOrWhiteSpace(id))
        {
            _hub.TryAck(id.Trim());
            Runtime.AlarmManager.Clear(id.Trim(), out _);
        }
        else
        {
            await WriteErrorAsync(context.Response, "alarm_id_required", cancellationToken).ConfigureAwait(false);
            return;
        }

        await WriteListAsync(context.Response, cancellationToken).ConfigureAwait(false);
    }

    private Task WriteJsonAsync(HttpListenerResponse response, object payload, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(payload, SnapshotJsonOptions);
        return WriteResponseAsync(response, "application/json; charset=utf-8", json, cancellationToken);
    }

    private static bool IsTruthy(string? raw)
    {
        var t = (raw ?? "").Trim().ToLowerInvariant();
        return t is "1" or "true" or "yes" or "on";
    }

    private sealed class AckRequest
    {
        public string? Id { get; set; }
        public bool All { get; set; }
    }
}
