using System.Net;
using System.Text.Json;
using MDKOSS.Extensions.ModServer;

namespace MDKOSS.Core.Monitor;

/// <summary>Handles /api/modserver/* — start/stop/status and local register access.</summary>
public sealed class ModServerApiModule : MonitoringApiModule
{
    private sealed class DeviceRequest
    {
        public string? DeviceId { get; set; }
        public string? BindAddress { get; set; }
        public int? Port { get; set; }
        public int? UnitId { get; set; }
        public bool? AutoStart { get; set; }
        public ushort? Address { get; set; }
        public ushort? Count { get; set; }
        public ushort[]? Values { get; set; }
        public bool[]? BoolValues { get; set; }
    }

    public ModServerApiModule(MdkRuntime runtime) : base(runtime) { }

    public override string RoutePrefix => "/api/modserver";

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

            var status = ModServerDeviceApi.GetStatus(Runtime, deviceId);
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
            case "start":
            case "listen":
            {
                ModServerDeviceParameters? config = null;
                if (req.BindAddress is not null || req.Port is not null || req.UnitId is not null || req.AutoStart is not null)
                {
                    var current = Runtime.TryGetDevice(req.DeviceId, out var d) && d is ModServerDevice m
                        ? m.Parameters
                        : new ModServerDeviceParameters();
                    config = new ModServerDeviceParameters
                    {
                        BindAddress = req.BindAddress ?? current.BindAddress,
                        Port = req.Port ?? current.Port,
                        UnitId = req.UnitId.HasValue
                            ? (byte)Math.Clamp(req.UnitId.Value, 0, 255)
                            : current.UnitId,
                        AutoStart = req.AutoStart ?? current.AutoStart,
                    };
                }

                var code = ModServerDeviceApi.StartServer(Runtime, req.DeviceId, config);
                if (code != ModServerErrorCode.Ok)
                {
                    await WriteErrorAsync(context.Response, code.ToString(), cancellationToken).ConfigureAwait(false);
                    return true;
                }

                await WriteSuccessAsync(context.Response, "start", cancellationToken).ConfigureAwait(false);
                return true;
            }
            case "stop":
            {
                var code = ModServerDeviceApi.StopServer(Runtime, req.DeviceId);
                if (code != ModServerErrorCode.Ok && code != ModServerErrorCode.NotListening)
                {
                    await WriteErrorAsync(context.Response, code.ToString(), cancellationToken).ConfigureAwait(false);
                    return true;
                }

                await WriteSuccessAsync(context.Response, "stop", cancellationToken).ConfigureAwait(false);
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
            case "writeinput":
                await HandleWriteInputAsync(context.Response, req, cancellationToken).ConfigureAwait(false);
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
            case "writediscrete":
                await HandleWriteDiscreteAsync(context.Response, req, cancellationToken).ConfigureAwait(false);
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

        var (code, values) = ModServerDeviceApi.ReadHolding(Runtime, req.DeviceId!, address, count);
        await WriteReadResultAsync(response, code, address, count, values, ct).ConfigureAwait(false);
    }

    private async Task HandleWriteHoldingAsync(HttpListenerResponse response, DeviceRequest req, CancellationToken ct)
    {
        if (req.Address is null || req.Values is null || req.Values.Length == 0)
        {
            await WriteErrorAsync(response, "missing_fields", ct).ConfigureAwait(false);
            return;
        }

        var code = ModServerDeviceApi.WriteHolding(Runtime, req.DeviceId!, req.Address.Value, req.Values);
        await WriteWriteResultAsync(response, code, "writeholding", ct).ConfigureAwait(false);
    }

    private async Task HandleReadInputAsync(HttpListenerResponse response, DeviceRequest req, CancellationToken ct)
    {
        if (!TryAddressCount(req, out var address, out var count, out var error))
        {
            await WriteErrorAsync(response, error!, ct).ConfigureAwait(false);
            return;
        }

        var (code, values) = ModServerDeviceApi.ReadInput(Runtime, req.DeviceId!, address, count);
        await WriteReadResultAsync(response, code, address, count, values, ct).ConfigureAwait(false);
    }

    private async Task HandleWriteInputAsync(HttpListenerResponse response, DeviceRequest req, CancellationToken ct)
    {
        if (req.Address is null || req.Values is null || req.Values.Length == 0)
        {
            await WriteErrorAsync(response, "missing_fields", ct).ConfigureAwait(false);
            return;
        }

        var code = ModServerDeviceApi.WriteInput(Runtime, req.DeviceId!, req.Address.Value, req.Values);
        await WriteWriteResultAsync(response, code, "writeinput", ct).ConfigureAwait(false);
    }

    private async Task HandleReadCoilsAsync(HttpListenerResponse response, DeviceRequest req, CancellationToken ct)
    {
        if (!TryAddressCount(req, out var address, out var count, out var error))
        {
            await WriteErrorAsync(response, error!, ct).ConfigureAwait(false);
            return;
        }

        var (code, values) = ModServerDeviceApi.ReadCoils(Runtime, req.DeviceId!, address, count);
        await WriteReadResultAsync(response, code, address, count, values, ct).ConfigureAwait(false);
    }

    private async Task HandleWriteCoilsAsync(HttpListenerResponse response, DeviceRequest req, CancellationToken ct)
    {
        if (req.Address is null || req.BoolValues is null || req.BoolValues.Length == 0)
        {
            await WriteErrorAsync(response, "missing_fields", ct).ConfigureAwait(false);
            return;
        }

        var code = ModServerDeviceApi.WriteCoils(Runtime, req.DeviceId!, req.Address.Value, req.BoolValues);
        await WriteWriteResultAsync(response, code, "writecoils", ct).ConfigureAwait(false);
    }

    private async Task HandleReadDiscreteAsync(HttpListenerResponse response, DeviceRequest req, CancellationToken ct)
    {
        if (!TryAddressCount(req, out var address, out var count, out var error))
        {
            await WriteErrorAsync(response, error!, ct).ConfigureAwait(false);
            return;
        }

        var (code, values) = ModServerDeviceApi.ReadDiscrete(Runtime, req.DeviceId!, address, count);
        await WriteReadResultAsync(response, code, address, count, values, ct).ConfigureAwait(false);
    }

    private async Task HandleWriteDiscreteAsync(HttpListenerResponse response, DeviceRequest req, CancellationToken ct)
    {
        if (req.Address is null || req.BoolValues is null || req.BoolValues.Length == 0)
        {
            await WriteErrorAsync(response, "missing_fields", ct).ConfigureAwait(false);
            return;
        }

        var code = ModServerDeviceApi.WriteDiscrete(Runtime, req.DeviceId!, req.Address.Value, req.BoolValues);
        await WriteWriteResultAsync(response, code, "writediscrete", ct).ConfigureAwait(false);
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
        ModServerErrorCode code,
        ushort address,
        ushort count,
        object? values,
        CancellationToken ct)
    {
        if (code != ModServerErrorCode.Ok)
        {
            await WriteErrorAsync(response, code.ToString(), ct).ConfigureAwait(false);
            return;
        }

        var payload = JsonSerializer.Serialize(new { success = true, address, count, values }, SnapshotJsonOptions);
        await WriteResponseAsync(response, "application/json; charset=utf-8", payload, ct).ConfigureAwait(false);
    }

    private async Task WriteWriteResultAsync(
        HttpListenerResponse response,
        ModServerErrorCode code,
        string action,
        CancellationToken ct)
    {
        if (code != ModServerErrorCode.Ok)
        {
            await WriteErrorAsync(response, code.ToString(), ct).ConfigureAwait(false);
            return;
        }

        await WriteSuccessAsync(response, action, ct).ConfigureAwait(false);
    }
}
