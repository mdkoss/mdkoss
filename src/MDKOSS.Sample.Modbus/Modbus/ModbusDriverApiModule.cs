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

    private sealed class WritePointRequest
    {
        public string? DriverId { get; set; }
        public string? Id { get; set; }
        public JsonElement Value { get; set; }
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
            switch (actionPath.ToLowerInvariant())
            {
                case "catalog":
                    await WriteCatalogAsync(context, cancellationToken).ConfigureAwait(false);
                    return true;
                case "values":
                case "points":
                    await WriteValuesAsync(context, cancellationToken).ConfigureAwait(false);
                    return true;
                case "layout":
                    await WriteLayoutAsync(context, cancellationToken).ConfigureAwait(false);
                    return true;
                case "panels":
                case "plcconfig":
                    await WritePanelsAsync(context, cancellationToken).ConfigureAwait(false);
                    return true;
                case "holding":
                case "status":
                case "":
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
            case "writepoint":
                await HandleWritePointAsync(context, cancellationToken).ConfigureAwait(false);
                return true;
            case "writemany":
                await HandleWriteManyAsync(context, cancellationToken).ConfigureAwait(false);
                return true;
            case "fill":
                await HandleFillAsync(context, cancellationToken).ConfigureAwait(false);
                return true;
            case "layout":
            case "savelayout":
                await HandleSaveLayoutAsync(context, cancellationToken).ConfigureAwait(false);
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
        var drvCfg = Runtime.Setting.Drivers.FirstOrDefault(d =>
            string.Equals(d.Id, driverId, StringComparison.OrdinalIgnoreCase));
        string? Param(string key)
            => drvCfg?.Parameters is { } p && p.TryGetValue(key, out var v) ? v : null;
        var payload = JsonSerializer.Serialize(new
        {
            success = true,
            driverId,
            driverName = driver.Name,
            connected = snap.Connected,
            host = Param("host"),
            port = Param("port"),
            unitId = Param("unitId"),
            simulate = string.Equals(Param("simulate"), "true", StringComparison.OrdinalIgnoreCase),
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

    private async Task WriteCatalogAsync(HttpListenerContext context, CancellationToken cancellationToken)
    {
        var catalog = LoadCatalog();
        var panels = LoadPanels(catalog);
        var payload = JsonSerializer.Serialize(new
        {
            success = true,
            source = catalog.Source,
            groups = catalog.Groups,
            points = catalog.Points,
            panels,
        }, SnapshotJsonOptions);
        context.Response.StatusCode = (int)HttpStatusCode.OK;
        await WriteResponseAsync(context.Response, "application/json; charset=utf-8", payload, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task WriteValuesAsync(HttpListenerContext context, CancellationToken cancellationToken)
    {
        var query = context.Request.Url?.Query;
        var driverId = GetQueryValue(query, "driverId") ?? ResolveDefaultDriverId();
        if (!TryResolveDriver(driverId, out var driver, out var error) || driver is null)
        {
            await WriteErrorAsync(context.Response, error ?? "no_driver", cancellationToken).ConfigureAwait(false);
            return;
        }

        var catalog = LoadCatalog();
        var panels = LoadPanels(catalog);
        catalog = PlcPanelExport.AugmentCatalog(catalog, panels);
        var ids = GetQueryValue(query, "ids");
        var wanted = string.IsNullOrWhiteSpace(ids)
            ? catalog.Points
            : ids.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(catalog.Find)
                .Where(p => p is not null)
                .Cast<PlcRegisterPoint>()
                .ToList();

        var start = 0;
        var count = HoldingRegisterBank.DefaultCount;
        if (wanted.Count > 0)
        {
            start = wanted.Min(p => p.Address);
            var end = wanted.Max(p => p.Address + Math.Max(1, p.WordCount) - 1);
            count = Math.Clamp(end - start + 1, 1, HoldingRegisterBank.MaxCount);
        }

        var snap = HoldingRegisterBank.Read(driver, start, count);
        var values = wanted.Select(p =>
        {
            var raw = PlcRegisterAccess.Decode(p, snap.Values, snap.Start);
            return new
            {
                id = p.Id,
                type = p.Type,
                address = p.Address,
                bit = p.Bit,
                ok = raw is not null,
                value = raw,
            };
        }).ToList();

        var payload = JsonSerializer.Serialize(new
        {
            success = true,
            driverId,
            driverName = driver.Name,
            connected = snap.Connected,
            start = snap.Start,
            okCount = snap.OkCount,
            values,
            timestampUtc = DateTime.UtcNow,
        }, SnapshotJsonOptions);
        context.Response.StatusCode = (int)HttpStatusCode.OK;
        await WriteResponseAsync(context.Response, "application/json; charset=utf-8", payload, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task WritePanelsAsync(HttpListenerContext context, CancellationToken cancellationToken)
    {
        var catalog = LoadCatalog();
        var config = LoadPanels(catalog);
        var payload = JsonSerializer.Serialize(new
        {
            success = true,
            source = config.Source,
            config,
        }, SnapshotJsonOptions);
        context.Response.StatusCode = (int)HttpStatusCode.OK;
        await WriteResponseAsync(context.Response, "application/json; charset=utf-8", payload, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task WriteLayoutAsync(HttpListenerContext context, CancellationToken cancellationToken)
    {
        var catalog = LoadCatalog();
        var path = LayoutPath();
        var layout = ModbusHmiLayoutStore.LoadOrDefault(path, catalog);
        var payload = JsonSerializer.Serialize(new
        {
            success = true,
            path,
            layout,
        }, SnapshotJsonOptions);
        context.Response.StatusCode = (int)HttpStatusCode.OK;
        await WriteResponseAsync(context.Response, "application/json; charset=utf-8", payload, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task HandleWritePointAsync(HttpListenerContext context, CancellationToken cancellationToken)
    {
        var body = await ReadBodyAsync(context.Request, cancellationToken).ConfigureAwait(false);
        WritePointRequest? req;
        try
        {
            req = Deserialize<WritePointRequest>(body);
        }
        catch (JsonException)
        {
            await WriteErrorAsync(context.Response, "invalid_json", cancellationToken).ConfigureAwait(false);
            return;
        }

        if (string.IsNullOrWhiteSpace(req?.Id))
        {
            await WriteErrorAsync(context.Response, "missing_fields", cancellationToken).ConfigureAwait(false);
            return;
        }

        var catalog = LoadCatalog();
        var panels = LoadPanels(catalog);
        catalog = PlcPanelExport.AugmentCatalog(catalog, panels);
        var point = catalog.Find(req.Id);
        if (point is null)
        {
            await WriteErrorAsync(context.Response, "point_not_found", cancellationToken).ConfigureAwait(false);
            return;
        }

        var driverId = string.IsNullOrWhiteSpace(req.DriverId) ? ResolveDefaultDriverId() : req.DriverId.Trim();
        if (!TryResolveDriver(driverId, out var driver, out var error) || driver is null)
        {
            await WriteErrorAsync(context.Response, error ?? "no_driver", cancellationToken).ConfigureAwait(false);
            return;
        }

        if (!PlcRegisterAccess.TryWrite(driver, point, req.Value))
        {
            await WriteErrorAsync(context.Response, "write_failed", cancellationToken).ConfigureAwait(false);
            return;
        }

        var ok = JsonSerializer.Serialize(new
        {
            success = true,
            action = "writepoint",
            driverId,
            id = point.Id,
            type = point.Type,
            address = point.Address,
            bit = point.Bit,
        }, SnapshotJsonOptions);
        context.Response.StatusCode = (int)HttpStatusCode.OK;
        await WriteResponseAsync(context.Response, "application/json; charset=utf-8", ok, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task HandleSaveLayoutAsync(HttpListenerContext context, CancellationToken cancellationToken)
    {
        var body = await ReadBodyAsync(context.Request, cancellationToken).ConfigureAwait(false);
        ModbusHmiLayout? layout;
        try
        {
            layout = Deserialize<ModbusHmiLayout>(body);
        }
        catch (JsonException)
        {
            await WriteErrorAsync(context.Response, "invalid_json", cancellationToken).ConfigureAwait(false);
            return;
        }

        if (layout is null)
        {
            await WriteErrorAsync(context.Response, "invalid_layout", cancellationToken).ConfigureAwait(false);
            return;
        }

        var path = LayoutPath();
        try
        {
            ModbusHmiLayoutStore.Save(path, layout);
        }
        catch (Exception)
        {
            await WriteErrorAsync(context.Response, "save_failed", cancellationToken).ConfigureAwait(false);
            return;
        }

        var ok = JsonSerializer.Serialize(new
        {
            success = true,
            action = "savelayout",
            path,
            count = layout.Widgets.Count,
        }, SnapshotJsonOptions);
        context.Response.StatusCode = (int)HttpStatusCode.OK;
        await WriteResponseAsync(context.Response, "application/json; charset=utf-8", ok, cancellationToken)
            .ConfigureAwait(false);
    }

    private PlcRegisterCatalog LoadCatalog()
        => PlcRegisterCatalog.Load(Runtime.SettingPath, AppContext.BaseDirectory);

    private PlcPanelConfig LoadPanels(PlcRegisterCatalog catalog)
        => PlcPanelExport.LoadOrGenerate(Runtime.SettingPath, AppContext.BaseDirectory, catalog);

    private string LayoutPath()
        => ModbusHmiLayoutStore.ResolvePath(Runtime.SettingPath, AppContext.BaseDirectory);

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
