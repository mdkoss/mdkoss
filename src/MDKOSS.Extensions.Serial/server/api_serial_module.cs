using System.Net;
using System.Text.Json;

namespace MDKOSS.Core.Monitor;

/// <summary>
/// Handles /api/serial/* — serial port status, open, close, config, read, write operations.
/// </summary>
public sealed class SerialApiModule : MonitoringApiModule
{
    private sealed class SerialPortConfigRequest
    {
        public string? PortName { get; set; }
        public int? BaudRate { get; set; }
        public int? DataBits { get; set; }
        public string? Parity { get; set; }
        public string? StopBits { get; set; }
        public int? ReadTimeout { get; set; }
        public int? WriteTimeout { get; set; }
        public bool? DtrEnable { get; set; }
        public bool? RtsEnable { get; set; }
    }

    private sealed class SerialOpenRequest
    {
        public string? DeviceId { get; set; }
        public SerialPortConfigRequest? Config { get; set; }
    }

    private sealed class SerialWriteRequest
    {
        public string? DeviceId { get; set; }
        public string? Data { get; set; }
    }

    private sealed class SerialWriteBinRequest
    {
        public string? DeviceId { get; set; }
        public byte[]? Data { get; set; }
    }

    public SerialApiModule(MdkRuntime runtime) : base(runtime) { }

    public override string RoutePrefix => "/api/serial";

    public override async Task<bool> HandleAsync(
        HttpListenerContext context,
        string remainingPath,
        CancellationToken cancellationToken)
    {
        var actionPath = remainingPath.Trim('/');
        var isPost = string.Equals(context.Request.HttpMethod, "POST", StringComparison.OrdinalIgnoreCase);

        try
        {
            // GET /api/serial/status?deviceId=xxx
            if (actionPath.Equals("status", StringComparison.OrdinalIgnoreCase) && !isPost)
            {
                var query = context.Request.QueryString?["deviceId"];
                if (string.IsNullOrWhiteSpace(query))
                {
                    await WriteErrorAsync(context.Response, "missing_device_id", cancellationToken);
                    return true;
                }
                await HandleSerialStatusAsync(context.Response, query, cancellationToken);
                return true;
            }

            if (!isPost)
            {
                await WriteErrorAsync(context.Response, "method_not_allowed", cancellationToken);
                return true;
            }

            var body = await ReadBodyAsync(context.Request, cancellationToken).ConfigureAwait(false);

            // POST /api/serial/open
            if (actionPath.Equals("open", StringComparison.OrdinalIgnoreCase))
            {
                var req = Deserialize<SerialOpenRequest>(body);
                if (req?.DeviceId is null || req.Config is null)
                {
                    await WriteErrorAsync(context.Response, "missing_fields", cancellationToken);
                    return true;
                }
                await HandleSerialOpenAsync(context.Response, req.DeviceId, req.Config, cancellationToken);
                return true;
            }

            // POST /api/serial/close
            if (actionPath.Equals("close", StringComparison.OrdinalIgnoreCase))
            {
                var req = Deserialize<SerialWriteRequest>(body);
                if (req?.DeviceId is null)
                {
                    await WriteErrorAsync(context.Response, "missing_device_id", cancellationToken);
                    return true;
                }
                await HandleSerialCloseAsync(context.Response, req.DeviceId, cancellationToken);
                return true;
            }

            // POST /api/serial/config
            if (actionPath.Equals("config", StringComparison.OrdinalIgnoreCase))
            {
                var req = Deserialize<SerialOpenRequest>(body);
                if (req?.DeviceId is null || req.Config is null)
                {
                    await WriteErrorAsync(context.Response, "missing_fields", cancellationToken);
                    return true;
                }
                await HandleSerialConfigAsync(context.Response, req.DeviceId, req.Config, cancellationToken);
                return true;
            }

            // POST /api/serial/write
            if (actionPath.Equals("write", StringComparison.OrdinalIgnoreCase))
            {
                var req = Deserialize<SerialWriteRequest>(body);
                if (req?.DeviceId is null || req.Data is null)
                {
                    await WriteErrorAsync(context.Response, "missing_fields", cancellationToken);
                    return true;
                }
                await HandleSerialWriteAsync(context.Response, req.DeviceId, req.Data, cancellationToken);
                return true;
            }

            // POST /api/serial/writeBin
            if (actionPath.Equals("writeBin", StringComparison.OrdinalIgnoreCase))
            {
                var req = Deserialize<SerialWriteBinRequest>(body);
                if (req?.DeviceId is null || req.Data is null)
                {
                    await WriteErrorAsync(context.Response, "missing_fields", cancellationToken);
                    return true;
                }
                await HandleSerialWriteBinAsync(context.Response, req.DeviceId, req.Data, cancellationToken);
                return true;
            }

            // POST /api/serial/read
            if (actionPath.Equals("read", StringComparison.OrdinalIgnoreCase))
            {
                var req = Deserialize<SerialWriteRequest>(body);
                if (req?.DeviceId is null)
                {
                    await WriteErrorAsync(context.Response, "missing_device_id", cancellationToken);
                    return true;
                }
                await HandleSerialReadAsync(context.Response, req.DeviceId, cancellationToken);
                return true;
            }

            // POST /api/serial/discard
            if (actionPath.Equals("discard", StringComparison.OrdinalIgnoreCase))
            {
                var req = Deserialize<SerialWriteRequest>(body);
                if (req?.DeviceId is null)
                {
                    await WriteErrorAsync(context.Response, "missing_device_id", cancellationToken);
                    return true;
                }
                await HandleSerialDiscardAsync(context.Response, req.DeviceId, cancellationToken);
                return true;
            }

            return false;
        }
        catch (JsonException)
        {
            await WriteErrorAsync(context.Response, "invalid_json", cancellationToken);
            return true;
        }
    }

    private async Task HandleSerialStatusAsync(
        HttpListenerResponse response,
        string deviceId,
        CancellationToken cancellationToken)
    {
        var result = SerialDeviceApi.GetStatus(Runtime, deviceId.Trim());
        if (result is null)
        {
            await WriteErrorAsync(response, "device_not_found", cancellationToken);
            return;
        }

        var payload = JsonSerializer.Serialize(result, SnapshotJsonOptions);
        await WriteResponseAsync(response, "application/json; charset=utf-8", payload, cancellationToken);
    }

    private async Task HandleSerialOpenAsync(
        HttpListenerResponse response,
        string deviceId,
        SerialPortConfigRequest config,
        CancellationToken cancellationToken)
    {
        var err = SerialDeviceApi.OpenPort(Runtime, deviceId.Trim(), ToConfig(config));
        if (err != SerialErrorCode.Ok)
        {
            await WriteErrorAsync(response, $"error_{err}", cancellationToken);
            return;
        }

        await WriteSuccessAsync(response, "port_opened", cancellationToken);
    }

    private async Task HandleSerialCloseAsync(
        HttpListenerResponse response,
        string deviceId,
        CancellationToken cancellationToken)
    {
        var err = SerialDeviceApi.ClosePort(Runtime, deviceId.Trim());
        if (err != SerialErrorCode.Ok)
        {
            await WriteErrorAsync(response, $"error_{err}", cancellationToken);
            return;
        }

        await WriteSuccessAsync(response, "port_closed", cancellationToken);
    }

    private async Task HandleSerialConfigAsync(
        HttpListenerResponse response,
        string deviceId,
        SerialPortConfigRequest config,
        CancellationToken cancellationToken)
    {
        var err = SerialDeviceApi.SetConfig(Runtime, deviceId.Trim(), ToConfig(config));
        if (err != SerialErrorCode.Ok)
        {
            await WriteErrorAsync(response, $"error_{err}", cancellationToken);
            return;
        }

        await WriteSuccessAsync(response, "config_applied", cancellationToken);
    }

    private async Task HandleSerialWriteAsync(
        HttpListenerResponse response,
        string deviceId,
        string data,
        CancellationToken cancellationToken)
    {
        var err = SerialDeviceApi.WriteText(Runtime, deviceId.Trim(), data);
        if (err != SerialErrorCode.Ok)
        {
            await WriteErrorAsync(response, $"error_{err}", cancellationToken);
            return;
        }

        await WriteSuccessAsync(response, "data_sent", cancellationToken);
    }

    private async Task HandleSerialWriteBinAsync(
        HttpListenerResponse response,
        string deviceId,
        byte[] data,
        CancellationToken cancellationToken)
    {
        var err = SerialDeviceApi.WriteBinary(Runtime, deviceId.Trim(), data);
        if (err != SerialErrorCode.Ok)
        {
            await WriteErrorAsync(response, $"error_{err}", cancellationToken);
            return;
        }

        await WriteSuccessAsync(response, "data_sent", cancellationToken);
    }

    private async Task HandleSerialReadAsync(
        HttpListenerResponse response,
        string deviceId,
        CancellationToken cancellationToken)
    {
        var (err, data) = SerialDeviceApi.ReadAll(Runtime, deviceId.Trim());
        if (err != SerialErrorCode.Ok)
        {
            await WriteErrorAsync(response, $"error_{err}", cancellationToken);
            return;
        }

        var payload = JsonSerializer.Serialize(new { success = true, data }, SnapshotJsonOptions);
        await WriteResponseAsync(response, "application/json; charset=utf-8", payload, cancellationToken);
    }

    private async Task HandleSerialDiscardAsync(
        HttpListenerResponse response,
        string deviceId,
        CancellationToken cancellationToken)
    {
        var err = SerialDeviceApi.DiscardBuffers(Runtime, deviceId.Trim());
        if (err != SerialErrorCode.Ok)
        {
            await WriteErrorAsync(response, $"error_{err}", cancellationToken);
            return;
        }

        await WriteSuccessAsync(response, "buffers_discarded", cancellationToken);
    }

    private static SerialPortConfig ToConfig(SerialPortConfigRequest req)
    {
        return new SerialPortConfig
        {
            PortName = req.PortName ?? "COM1",
            BaudRate = req.BaudRate ?? 9600,
            DataBits = req.DataBits ?? 8,
            Parity = Enum.Parse<SerialParity>(req.Parity ?? "None"),
            StopBits = Enum.Parse<SerialStopBits>(req.StopBits ?? "One"),
            ReadTimeout = req.ReadTimeout ?? 5000,
            WriteTimeout = req.WriteTimeout ?? 5000,
            DtrEnable = req.DtrEnable ?? false,
            RtsEnable = req.RtsEnable ?? false
        };
    }
}
