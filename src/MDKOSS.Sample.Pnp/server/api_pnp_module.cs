using System.Net;
using System.Text.Json;
using MDKOSS.Core;
using MDKOSS.Core.Monitor;

namespace MDKOSS.Pnp;

/// <summary>Handles /api/pnp/* — cycle control, dashboard, and logs for the PNP machine type.</summary>
public sealed class PnpApiModule : MonitoringApiModule
{
    public PnpApiModule(MdkRuntime runtime) : base(runtime) { }

    public override string RoutePrefix => "/api/pnp";

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
                await WriteDashboardAsync(context.Response, cancellationToken).ConfigureAwait(false);
                return true;
            }

            if (actionPath.Equals("logs", StringComparison.OrdinalIgnoreCase))
            {
                await WriteLogsAsync(context.Response, cancellationToken).ConfigureAwait(false);
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
            case "start":
                Runtime.Vars.Set("task.pnp.command", "start");
                Runtime.Vars.Set("task.operation.command", "start");
                Runtime.Vars.Set("machine.command", "start");
                PnpLogStore.Info("api", "operator start");
                await WriteSuccessAsync(context.Response, "start", cancellationToken).ConfigureAwait(false);
                return true;
            case "stop":
                Runtime.Vars.Set("task.pnp.command", "stop");
                Runtime.Vars.Set("task.operation.command", "stop");
                Runtime.Vars.Set("machine.command", "stop");
                PnpLogStore.Warn("api", "operator stop");
                await WriteSuccessAsync(context.Response, "stop", cancellationToken).ConfigureAwait(false);
                return true;
            case "reset":
                Runtime.Vars.Set("task.pnp.command", "reset");
                Runtime.Vars.Set("task.operation.command", "reset");
                Runtime.Vars.Set("machine.command", "reset");
                await WriteSuccessAsync(context.Response, "reset", cancellationToken).ConfigureAwait(false);
                return true;
            case "traychange":
                Runtime.Vars.Set("task.pnp.trayChangeRequest", true);
                PnpLogStore.Info("api", "operator tray change request");
                await WriteSuccessAsync(context.Response, "traychange", cancellationToken).ConfigureAwait(false);
                return true;
            case "clearlogs":
                PnpLogStore.Clear();
                PnpLogStore.Info("api", "logs cleared");
                await WriteSuccessAsync(context.Response, "clearlogs", cancellationToken).ConfigureAwait(false);
                return true;
            default:
                await WriteErrorAsync(context.Response, "unknown_action", cancellationToken).ConfigureAwait(false);
                return true;
        }
    }

    private Task WriteLogsAsync(HttpListenerResponse response, CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.Serialize(new
        {
            success = true,
            count = PnpLogStore.Count,
            logs = PnpLogStore.Snapshot(300).Select(FormatLog),
            timestampUtc = DateTime.UtcNow
        }, SnapshotJsonOptions);

        response.StatusCode = (int)HttpStatusCode.OK;
        return WriteResponseAsync(response, "application/json; charset=utf-8", payload, cancellationToken);
    }

    private Task WriteDashboardAsync(HttpListenerResponse response, CancellationToken cancellationToken)
    {
        var snapshot = Runtime.GetSnapshot();
        var vars = snapshot.Vars;
        object Pick(string key) => vars.TryGetValue(key, out var v) ? v ?? "" : "";

        var modules = BuildModules(snapshot, Runtime.GetTaskSnapshots());
        var logs = PnpLogStore.Snapshot(200).Select(FormatLog).ToList();

        var payload = JsonSerializer.Serialize(new
        {
            success = true,
            projectName = snapshot.ProjectName,
            isRunning = snapshot.IsRunning,
            phase = Pick("task.pnp.phase"),
            message = Pick("task.pnp.message"),
            okCount = Pick("task.pnp.okCount"),
            ngCount = Pick("task.pnp.ngCount"),
            srcTrayPresent = Pick("task.pnp.srcTrayPresent"),
            tgtTrayPresent = Pick("task.pnp.tgtTrayPresent"),
            trayChangeRequest = Pick("task.pnp.trayChangeRequest"),
            conveyorPhase = Pick("task.pnp.conveyor.phase"),
            conveyorMessage = Pick("task.pnp.conveyor.message"),
            operationState = Pick("task.operation.state"),
            machineMode = Pick("machine.mode"),
            vision = new
            {
                topX = Pick("pnp.vision.top.x"),
                topY = Pick("pnp.vision.top.y"),
                topOk = Pick("pnp.vision.top.ok"),
                angleDeg = Pick("pnp.vision.bottom.angleDeg"),
                angleOk = Pick("pnp.vision.bottom.ok")
            },
            modules,
            logs,
            logCount = PnpLogStore.Count,
            timestampUtc = DateTime.UtcNow
        }, SnapshotJsonOptions);

        response.StatusCode = (int)HttpStatusCode.OK;
        return WriteResponseAsync(response, "application/json; charset=utf-8", payload, cancellationToken);
    }

    private static object FormatLog(PnpLogEntry entry) => new
    {
        time = entry.TimestampUtc.ToLocalTime().ToString("HH:mm:ss.fff"),
        timestampUtc = entry.TimestampUtc,
        level = entry.Level,
        source = entry.Source,
        message = entry.Message
    };

    private static List<object> BuildModules(
        RuntimeSnapshot snapshot,
        IReadOnlyList<TaskSnapshot> tasks)
    {
        var modules = new List<object>();

        foreach (var (id, drv) in snapshot.Drivers.OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase))
        {
            modules.Add(new
            {
                id,
                name = id,
                category = "driver",
                state = drv.IsConnected ? "Online" : "Offline",
                ok = drv.IsConnected,
                detail = $"{drv.Type} / {(drv.IsConnected ? "connected" : "disconnected")}"
            });
        }

        foreach (var (id, dev) in snapshot.Devices.OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase))
        {
            var ok = string.Equals(dev.State, "Running", StringComparison.OrdinalIgnoreCase)
                     && dev.DriverConnected;
            modules.Add(new
            {
                id,
                name = dev.Name,
                category = "device",
                type = dev.Type,
                state = dev.State,
                ok,
                detail = $"{dev.Type} / driver {(dev.DriverConnected ? "ok" : "down")}"
            });
        }

        foreach (var task in tasks)
        {
            var ok = string.Equals(task.State, "Running", StringComparison.OrdinalIgnoreCase);
            modules.Add(new
            {
                id = task.Name,
                name = task.Name,
                category = "task",
                type = task.Type,
                state = task.State,
                ok,
                detail = $"interval {task.IntervalMs} ms"
            });
        }

        return modules;
    }
}
