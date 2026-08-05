using System.Net;
using System.Text.Json;
using MDKOSS.Extensions.PyScript;

namespace MDKOSS.Core.Monitor;

/// <summary>Handles /api/pyscript/* — run, kill, status for Python script devices.</summary>
public sealed class PyScriptApiModule : MonitoringApiModule
{
    private sealed class DeviceRequest
    {
        public string? DeviceId { get; set; }
        public string? ScriptPath { get; set; }
        public string? Arguments { get; set; }
        public int? TimeoutMs { get; set; }
    }

    public PyScriptApiModule(MdkRuntime runtime) : base(runtime) { }

    public override string RoutePrefix => "/api/pyscript";

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

        if (!Runtime.TryGetDevice(req.DeviceId, out var dev) || dev is not PyScriptDevice device)
        {
            await WriteErrorAsync(context.Response, "device_not_found", cancellationToken).ConfigureAwait(false);
            return true;
        }

        switch (actionPath.ToLowerInvariant())
        {
            case "run":
            case "execute":
            {
                var result = device.Run(req.ScriptPath, req.Arguments, req.TimeoutMs);
                if (result is null)
                {
                    await WriteErrorAsync(context.Response, device.LastError ?? "run_failed", cancellationToken)
                        .ConfigureAwait(false);
                    return true;
                }

                var payload = JsonSerializer.Serialize(
                    new { success = true, action = "run", result },
                    SnapshotJsonOptions);
                await WriteResponseAsync(context.Response, "application/json; charset=utf-8", payload, cancellationToken)
                    .ConfigureAwait(false);
                return true;
            }
            case "kill":
            case "cancel":
            {
                if (!device.Kill())
                {
                    await WriteErrorAsync(context.Response, "not_running", cancellationToken).ConfigureAwait(false);
                    return true;
                }

                await WriteSuccessAsync(context.Response, "kill", cancellationToken).ConfigureAwait(false);
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
        if (!Runtime.TryGetDevice(deviceId, out var dev) || dev is not PyScriptDevice device)
        {
            await WriteErrorAsync(response, "device_not_found", cancellationToken).ConfigureAwait(false);
            return;
        }

        var payload = JsonSerializer.Serialize(new
        {
            success = true,
            deviceId = device.Id,
            isRunning = device.IsRunning,
            pythonPath = device.Parameters.PythonPath,
            scriptPath = device.Parameters.ScriptPath,
            workingDirectory = device.Parameters.WorkingDirectory,
            arguments = device.Parameters.Arguments,
            timeoutMs = device.Parameters.TimeoutMs,
            runCount = device.RunCount,
            lastError = device.LastError,
            lastResult = device.LastResult
        }, SnapshotJsonOptions);

        await WriteResponseAsync(response, "application/json; charset=utf-8", payload, cancellationToken)
            .ConfigureAwait(false);
    }
}
