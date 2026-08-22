using System.Globalization;
using System.Net;
using System.Text.Json;
using MDKOSS.Core;
using MDKOSS.Core.Drivers;
using MDKOSS.Core.Monitor;

namespace MDKOSS.Sample.Modbus.Machine;

/// <summary>
/// Handles <c>/api/modbusdrv/*</c> — batch holding-register panel for Modbus IDriver (default 200 words).
/// </summary>
public sealed class ModbusDriverApiModule : MonitoringApiModule
{
    private sealed class WriteOneRequest
    {
        public string? DriverId { get; set; }
        public int? Address { get; set; }
        public ushort? Value { get; set; }
    }

    private sealed class WriteManyRequest
    {
        public string? DriverId { get; set; }
        public int? Start { get; set; }
        public ushort[]? Values { get; set; }
    }

    private sealed class FillRequest
    {
        public string? DriverId { get; set; }
        public int? Start { get; set; }
        public int? Count { get; set; }
    }

    public ModbusDriverApiModule(MdkRuntime runtime) : base(runtime) { }

    public override string RoutePrefix => "/api/modbusdrv";

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
            if (actionPath.Equals("holding", StringComparison.OrdinalIgnoreCase)
                || actionPath.Equals("status", StringComparison.OrdinalIgnoreCase)
                || string.IsNullOrEmpty(actionPath))
            {
                await WriteHoldingAsync(context, cancellationToken).ConfigureAwait(false);
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
            case "write":
            case "writeone":
                await HandleWriteOneAsync(context, cancellationToken).ConfigureAwait(false);
                return true;
            case "writemany":
                await HandleWriteManyAsync(context, cancellationToken).ConfigureAwait(false);
                return true;
            case "fill":
                await HandleFillAsync(context, cancellationToken).ConfigureAwait(false);
                return true;
            default:
                await WriteErrorAsync(context.Response, "unknown_action", cancellationToken).ConfigureAwait(false);
                return true;
        }
    }

    private async Task WriteHoldingAsync(HttpListenerContext context, CancellationToken cancellationToken)
    {
        var query = context.Request.Url?.Query;
        var driverId = GetQueryValue(query, "driverId") ?? ResolveDefaultDriverId();
        var start = ParseInt(GetQueryValue(query, "start"), 0);
        var count = ParseInt(GetQueryValue(query, "count"), HoldingRegisterBank.DefaultCount);

        if (!TryResolveDriver(driverId, out var driver, out var error) || driver is null)
        {
            await WriteErrorAsync(context.Response, error ?? "no_driver", cancellationToken).ConfigureAwait(false);
            return;
        }

        var snap = HoldingRegisterBank.Read(driver, start, count);
        var payload = JsonSerializer.Serialize(new
        {
            success = true,
            driverId,
            driverName = driver.Name,
            connected = snap.Connected,
            start = snap.Start,
            count = snap.Values.Count,
            okCount = snap.OkCount,
            values = snap.Values,
            timestampUtc = DateTime.UtcNow,
        }, SnapshotJsonOptions);

        context.Response.StatusCode = (int)HttpStatusCode.OK;
        await WriteResponseAsync(context.Response, "application/json; charset=utf-8", payload, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task HandleWriteOneAsync(HttpListenerContext context, CancellationToken cancellationToken)
    {
        var body = await ReadBodyAsync(context.Request, cancellationToken).ConfigureAwait(false);
        WriteOneRequest? req;
        try
        {
            req = Deserialize<WriteOneRequest>(body);
        }
        catch (JsonException)
        {
            await WriteErrorAsync(context.Response, "invalid_json", cancellationToken).ConfigureAwait(false);
            return;
        }

        if (req?.Address is null || req.Value is null)
        {
            await WriteErrorAsync(context.Response, "missing_fields", cancellationToken).ConfigureAwait(false);
            return;
        }

        var driverId = string.IsNullOrWhiteSpace(req.DriverId) ? ResolveDefaultDriverId() : req.DriverId.Trim();
        if (!TryResolveDriver(driverId, out var driver, out var error) || driver is null)
        {
            await WriteErrorAsync(context.Response, error ?? "no_driver", cancellationToken).ConfigureAwait(false);
            return;
        }

        if (!HoldingRegisterBank.WriteOne(driver, req.Address.Value, req.Value.Value))
        {
            await WriteErrorAsync(context.Response, "write_failed", cancellationToken).ConfigureAwait(false);
            return;
        }

        var ok = JsonSerializer.Serialize(new
        {
            success = true,
            action = "write",
            driverId,
            address = req.Address.Value,
            value = req.Value.Value,
        }, SnapshotJsonOptions);
        context.Response.StatusCode = (int)HttpStatusCode.OK;
        await WriteResponseAsync(context.Response, "application/json; charset=utf-8", ok, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task HandleWriteManyAsync(HttpListenerContext context, CancellationToken cancellationToken)
    {
        var body = await ReadBodyAsync(context.Request, cancellationToken).ConfigureAwait(false);
        WriteManyRequest? req;
        try
        {
            req = Deserialize<WriteManyRequest>(body);
        }
        catch (JsonException)
        {
            await WriteErrorAsync(context.Response, "invalid_json", cancellationToken).ConfigureAwait(false);
            return;
        }

        if (req?.Values is null || req.Values.Length == 0)
        {
            await WriteErrorAsync(context.Response, "missing_fields", cancellationToken).ConfigureAwait(false);
            return;
        }

        var driverId = string.IsNullOrWhiteSpace(req.DriverId) ? ResolveDefaultDriverId() : req.DriverId.Trim();
        if (!TryResolveDriver(driverId, out var driver, out var error) || driver is null)
        {
            await WriteErrorAsync(context.Response, error ?? "no_driver", cancellationToken).ConfigureAwait(false);
            return;
        }

        var start = req.Start ?? 0;
        var written = HoldingRegisterBank.WriteMany(driver, start, req.Values);
        var ok = JsonSerializer.Serialize(new
        {
            success = written == req.Values.Length,
            action = "writemany",
            driverId,
            start,
            requested = req.Values.Length,
            written,
        }, SnapshotJsonOptions);
        context.Response.StatusCode = (int)HttpStatusCode.OK;
        await WriteResponseAsync(context.Response, "application/json; charset=utf-8", ok, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task HandleFillAsync(HttpListenerContext context, CancellationToken cancellationToken)
    {
        var body = await ReadBodyAsync(context.Request, cancellationToken).ConfigureAwait(false);
        FillRequest? req;
        try
        {
            req = string.IsNullOrWhiteSpace(body) ? new FillRequest() : Deserialize<FillRequest>(body);
        }
        catch (JsonException)
        {
            await WriteErrorAsync(context.Response, "invalid_json", cancellationToken).ConfigureAwait(false);
            return;
        }

        var driverId = string.IsNullOrWhiteSpace(req?.DriverId) ? ResolveDefaultDriverId() : req!.DriverId!.Trim();
        if (!TryResolveDriver(driverId, out var driver, out var error) || driver is null)
        {
            await WriteErrorAsync(context.Response, error ?? "no_driver", cancellationToken).ConfigureAwait(false);
            return;
        }

        var start = req?.Start ?? 0;
        var count = req?.Count ?? HoldingRegisterBank.DefaultCount;
        var written = HoldingRegisterBank.FillPattern(driver, start, count);
        var ok = JsonSerializer.Serialize(new
        {
            success = written == count,
            action = "fill",
            driverId,
            start,
            count,
            written,
        }, SnapshotJsonOptions);
        context.Response.StatusCode = (int)HttpStatusCode.OK;
        await WriteResponseAsync(context.Response, "application/json; charset=utf-8", ok, cancellationToken)
            .ConfigureAwait(false);
    }

    private string ResolveDefaultDriverId()
    {
        var preferred = Runtime.Setting.Drivers.FirstOrDefault(d =>
            d.Enabled
            && (string.Equals(d.Type, "modbus", StringComparison.OrdinalIgnoreCase)
                || string.Equals(d.Type, "modbus-tcp", StringComparison.OrdinalIgnoreCase)));
        if (preferred is not null)
        {
            return preferred.Id;
        }

        return Runtime.Setting.Drivers.FirstOrDefault(d => d.Enabled)?.Id ?? string.Empty;
    }

    private bool TryResolveDriver(string? driverId, out IDriver? driver, out string? error)
    {
        driver = null;
        error = null;
        if (string.IsNullOrWhiteSpace(driverId))
        {
            error = "no_driver";
            return false;
        }

        if (!Runtime.TryGetDriver(driverId.Trim(), out var resolved))
        {
            error = "driver_not_found";
            return false;
        }

        driver = resolved;
        return true;
    }

    private static int ParseInt(string? text, int fallback)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return fallback;
        }

        return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n)
            ? n
            : fallback;
    }
}
