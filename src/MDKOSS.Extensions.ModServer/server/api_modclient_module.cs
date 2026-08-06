using System.Net;
using System.Text.Json;
using MDKOSS.Extensions.ModServer;

namespace MDKOSS.Core.Monitor;

/// <summary>Handles /api/modclient/* — connect/status and Modbus master reads (incl. batch).</summary>
public sealed class ModClientApiModule : MonitoringApiModule
{
    private sealed class DeviceRequest
    {
        public string? DeviceId { get; set; }
        public string? Host { get; set; }
        public int? Port { get; set; }
        public int? UnitId { get; set; }
        public int? ConnectTimeoutMs { get; set; }
        public int? ReadTimeoutMs { get; set; }
        public int? WriteTimeoutMs { get; set; }
        public bool? AutoConnect { get; set; }
        public ushort? Address { get; set; }
        public ushort? Count { get; set; }
        public ushort[]? Values { get; set; }
        public bool[]? BoolValues { get; set; }
        public List<BatchItemDto>? Items { get; set; }
    }

    private sealed class BatchItemDto
    {
        public string? Tag { get; set; }
        public string? Area { get; set; }
        public string? Kind { get; set; }
        public string? Type { get; set; }
        public ushort? Address { get; set; }
        public ushort? Count { get; set; }
    }

    public ModClientApiModule(MdkRuntime runtime) : base(runtime) { }

    public override string RoutePrefix => "/api/modclient";

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

            var status = ModClientDeviceApi.GetStatus(Runtime, deviceId);
            if (status is null)
            {
                await WriteErrorAsync(context.Response, "device_not_found", cancellationToken).ConfigureAwait(false);
                return true;
            }

            var payload = JsonSerializer.Serialize(new { success = true, status }, SnapshotJsonOptions);
            await WriteResponseAsync(context.Response, "application/json; charset=utf-8", payload, cancellationToken)
                .ConfigureAwait(false);
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

        switch (actionPath.ToLowerInvariant())
        {
            case "connect":
            case "open":
            {
                ModClientDeviceParameters? config = null;
                if (req.Host is not null || req.Port is not null || req.UnitId is not null
                    || req.ConnectTimeoutMs is not null || req.ReadTimeoutMs is not null
                    || req.WriteTimeoutMs is not null || req.AutoConnect is not null)
                {
                    var current = Runtime.TryGetDevice(req.DeviceId, out var d) && d is ModClientDevice m
                        ? m.Parameters
                        : new ModClientDeviceParameters();
                    config = new ModClientDeviceParameters
                    {
                        Host = req.Host ?? current.Host,
                        Port = req.Port ?? current.Port,
                        UnitId = req.UnitId.HasValue
                            ? (byte)Math.Clamp(req.UnitId.Value, 0, 255)
                            : current.UnitId,
                        ConnectTimeoutMs = req.ConnectTimeoutMs ?? current.ConnectTimeoutMs,
                        ReadTimeoutMs = req.ReadTimeoutMs ?? current.ReadTimeoutMs,
                        WriteTimeoutMs = req.WriteTimeoutMs ?? current.WriteTimeoutMs,
                        AutoConnect = req.AutoConnect ?? current.AutoConnect,
                    };
                }

                var code = ModClientDeviceApi.Connect(Runtime, req.DeviceId, config);
                if (code != ModClientErrorCode.Ok)
                {
                    await WriteErrorAsync(context.Response, code.ToString(), cancellationToken).ConfigureAwait(false);
                    return true;
                }

                await WriteSuccessAsync(context.Response, "connect", cancellationToken).ConfigureAwait(false);
                return true;
            }
            case "disconnect":
            case "close":
            {
                var code = ModClientDeviceApi.Disconnect(Runtime, req.DeviceId);
                if (code != ModClientErrorCode.Ok && code != ModClientErrorCode.NotConnected)
                {
                    await WriteErrorAsync(context.Response, code.ToString(), cancellationToken).ConfigureAwait(false);
                    return true;
                }

                await WriteSuccessAsync(context.Response, "disconnect", cancellationToken).ConfigureAwait(false);
                return true;
            }
            case "readholding":
                await HandleReadHoldingAsync(context.Response, req, cancellationToken).ConfigureAwait(false);
                return true;
            case "writeholding":
                await HandleWriteHoldingAsync(context.Response, req, cancellationToken).ConfigureAwait(false);
                return true;
            case "readinput":
                await HandleReadInputAsync(context.Response, req, cancellationToken).ConfigureAwait(false);
                return true;
            case "readcoils":
                await HandleReadCoilsAsync(context.Response, req, cancellationToken).ConfigureAwait(false);
                return true;
            case "writecoils":
                await HandleWriteCoilsAsync(context.Response, req, cancellationToken).ConfigureAwait(false);
                return true;
            case "readdiscrete":
                await HandleReadDiscreteAsync(context.Response, req, cancellationToken).ConfigureAwait(false);
                return true;
            case "readbatch":
            case "batchread":
                await HandleReadBatchAsync(context.Response, req, cancellationToken).ConfigureAwait(false);
                return true;
            default:
                await WriteErrorAsync(context.Response, "unknown_action", cancellationToken).ConfigureAwait(false);
                return true;
        }
    }

    private async Task HandleReadHoldingAsync(HttpListenerResponse response, DeviceRequest req, CancellationToken ct)
    {
        if (!TryAddressCount(req, out var address, out var count, out var error))
        {
            await WriteErrorAsync(response, error!, ct).ConfigureAwait(false);
            return;
        }

        var (code, values) = ModClientDeviceApi.ReadHolding(Runtime, req.DeviceId!, address, count);
        await WriteReadResultAsync(response, code, address, count, values, ct).ConfigureAwait(false);
    }

    private async Task HandleWriteHoldingAsync(HttpListenerResponse response, DeviceRequest req, CancellationToken ct)
    {
        if (req.Address is null || req.Values is null || req.Values.Length == 0)
        {
            await WriteErrorAsync(response, "missing_fields", ct).ConfigureAwait(false);
            return;
        }

        var code = ModClientDeviceApi.WriteHolding(Runtime, req.DeviceId!, req.Address.Value, req.Values);
        await WriteWriteResultAsync(response, code, "writeholding", ct).ConfigureAwait(false);
    }

    private async Task HandleReadInputAsync(HttpListenerResponse response, DeviceRequest req, CancellationToken ct)
    {
        if (!TryAddressCount(req, out var address, out var count, out var error))
        {
            await WriteErrorAsync(response, error!, ct).ConfigureAwait(false);
            return;
        }

        var (code, values) = ModClientDeviceApi.ReadInput(Runtime, req.DeviceId!, address, count);
        await WriteReadResultAsync(response, code, address, count, values, ct).ConfigureAwait(false);
    }

    private async Task HandleReadCoilsAsync(HttpListenerResponse response, DeviceRequest req, CancellationToken ct)
    {
        if (!TryAddressCount(req, out var address, out var count, out var error))
        {
            await WriteErrorAsync(response, error!, ct).ConfigureAwait(false);
            return;
        }

        var (code, values) = ModClientDeviceApi.ReadCoils(Runtime, req.DeviceId!, address, count);
        await WriteReadResultAsync(response, code, address, count, values, ct).ConfigureAwait(false);
    }

    private async Task HandleWriteCoilsAsync(HttpListenerResponse response, DeviceRequest req, CancellationToken ct)
    {
        if (req.Address is null || req.BoolValues is null || req.BoolValues.Length == 0)
        {
            await WriteErrorAsync(response, "missing_fields", ct).ConfigureAwait(false);
            return;
        }

        var code = ModClientDeviceApi.WriteCoils(Runtime, req.DeviceId!, req.Address.Value, req.BoolValues);
        await WriteWriteResultAsync(response, code, "writecoils", ct).ConfigureAwait(false);
    }

    private async Task HandleReadDiscreteAsync(HttpListenerResponse response, DeviceRequest req, CancellationToken ct)
    {
        if (!TryAddressCount(req, out var address, out var count, out var error))
        {
            await WriteErrorAsync(response, error!, ct).ConfigureAwait(false);
            return;
        }

        var (code, values) = ModClientDeviceApi.ReadDiscrete(Runtime, req.DeviceId!, address, count);
        await WriteReadResultAsync(response, code, address, count, values, ct).ConfigureAwait(false);
    }

    private async Task HandleReadBatchAsync(HttpListenerResponse response, DeviceRequest req, CancellationToken ct)
    {
        if (req.Items is null || req.Items.Count == 0)
        {
            await WriteErrorAsync(response, "missing_items", ct).ConfigureAwait(false);
            return;
        }

        var items = new List<ModClientReadItem>(req.Items.Count);
        foreach (var dto in req.Items)
        {
            if (dto.Address is null)
            {
                await WriteErrorAsync(response, "missing_address", ct).ConfigureAwait(false);
                return;
            }

            var areaToken = dto.Area ?? dto.Kind ?? dto.Type;
            if (!ModClientDeviceActions.TryParseArea(areaToken, out var area))
            {
                await WriteErrorAsync(response, "invalid_area", ct).ConfigureAwait(false);
                return;
            }

            var count = dto.Count is null or 0 ? (ushort)1 : dto.Count.Value;
            items.Add(new ModClientReadItem
            {
                Tag = dto.Tag,
                Area = area,
                Address = dto.Address.Value,
                Count = count,
            });
        }

        var results = ModClientDeviceApi.ReadBatch(Runtime, req.DeviceId!, items);
        if (results is null)
        {
            await WriteErrorAsync(response, "device_not_found", ct).ConfigureAwait(false);
            return;
        }

        var payload = JsonSerializer.Serialize(new
        {
            success = true,
            count = results.Count,
            ok = results.Count(r => r.Error == ModClientErrorCode.Ok),
            results = results.Select(r => new
            {
                tag = r.Tag,
                area = ModClientDeviceActions.AreaToToken(r.Area),
                address = r.Address,
                count = r.Count,
                values = r.Registers is not null ? (object?)r.Registers : r.Bits,
                error = r.Error == ModClientErrorCode.Ok ? null : (r.ErrorMessage ?? r.Error.ToString()),
                success = r.Error == ModClientErrorCode.Ok,
            }),
        }, SnapshotJsonOptions);

        await WriteResponseAsync(response, "application/json; charset=utf-8", payload, ct).ConfigureAwait(false);
    }

    private static bool TryAddressCount(DeviceRequest req, out ushort address, out ushort count, out string? error)
    {
        address = 0;
        count = 1;
        error = null;
        if (req.Address is null)
        {
            error = "missing_address";
            return false;
        }

        address = req.Address.Value;
        count = req.Count is null or 0 ? (ushort)1 : req.Count.Value;
        return true;
    }

    private async Task WriteReadResultAsync(
        HttpListenerResponse response,
        ModClientErrorCode code,
        ushort address,
        ushort count,
        object? values,
        CancellationToken ct)
    {
        if (code != ModClientErrorCode.Ok)
        {
            await WriteErrorAsync(response, code.ToString(), ct).ConfigureAwait(false);
            return;
        }

        var payload = JsonSerializer.Serialize(new { success = true, address, count, values }, SnapshotJsonOptions);
        await WriteResponseAsync(response, "application/json; charset=utf-8", payload, ct).ConfigureAwait(false);
    }

    private async Task WriteWriteResultAsync(
        HttpListenerResponse response,
        ModClientErrorCode code,
        string action,
        CancellationToken ct)
    {
        if (code != ModClientErrorCode.Ok)
        {
            await WriteErrorAsync(response, code.ToString(), ct).ConfigureAwait(false);
            return;
        }

        await WriteSuccessAsync(response, action, ct).ConfigureAwait(false);
    }
}
