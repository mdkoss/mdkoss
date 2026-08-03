using System.Net;
using System.Text.Json;

namespace MDKOSS.Core.Monitor;

/// <summary>
/// Handles /api/tcp/* — TCP connection status, connect, disconnect, config, read, write operations.
/// </summary>
public sealed class TcpApiModule : MonitoringApiModule
{
    private sealed class TcpConfigRequest
    {
        public string? Host { get; set; }
        public int? Port { get; set; }
        public int? ConnectTimeout { get; set; }
        public int? ReadTimeout { get; set; }
        public int? WriteTimeout { get; set; }
        public bool? NoDelay { get; set; }
        public bool? KeepAlive { get; set; }
    }

    private sealed class TcpDeviceRequest
    {
        public string? DeviceId { get; set; }
        public TcpConfigRequest? Config { get; set; }
    }

    private sealed class TcpWriteRequest
    {
        public string? DeviceId { get; set; }
        public string? Data { get; set; }
    }

    private sealed class TcpWriteBinRequest
    {
        public string? DeviceId { get; set; }
        public byte[]? Data { get; set; }
    }

    public TcpApiModule(MdkRuntime runtime) : base(runtime) { }

    public override string RoutePrefix => "/api/tcp";

    public override async Task<bool> HandleAsync(
        HttpListenerContext context,
        string remainingPath,
        CancellationToken cancellationToken)
    {
        var actionPath = remainingPath.Trim('/');
        var isPost = string.Equals(context.Request.HttpMethod, "POST", StringComparison.OrdinalIgnoreCase);

        try
        {
            // GET /api/tcp/status?deviceId=xxx
            if (actionPath.Equals("status", StringComparison.OrdinalIgnoreCase) && !isPost)
            {
                var query = context.Request.QueryString?["deviceId"];
                if (string.IsNullOrWhiteSpace(query))
                {
                    await WriteErrorAsync(context.Response, "missing_device_id", cancellationToken);
                    return true;
                }
                await HandleTcpStatusAsync(context.Response, query, cancellationToken);
                return true;
            }

            if (!isPost)
            {
                await WriteErrorAsync(context.Response, "method_not_allowed", cancellationToken);
                return true;
            }

            var body = await ReadBodyAsync(context.Request, cancellationToken).ConfigureAwait(false);

            // POST /api/tcp/connect
            if (actionPath.Equals("connect", StringComparison.OrdinalIgnoreCase))
            {
                var req = Deserialize<TcpDeviceRequest>(body);
                if (req?.DeviceId is null || req.Config is null)
                {
                    await WriteErrorAsync(context.Response, "missing_fields", cancellationToken);
                    return true;
                }
                await HandleTcpConnectAsync(context.Response, req.DeviceId, req.Config, cancellationToken);
                return true;
            }

            // POST /api/tcp/disconnect
            if (actionPath.Equals("disconnect", StringComparison.OrdinalIgnoreCase))
            {
                var req = Deserialize<TcpDeviceRequest>(body);
                if (req?.DeviceId is null)
                {
                    await WriteErrorAsync(context.Response, "missing_device_id", cancellationToken);
                    return true;
                }
                await HandleTcpDisconnectAsync(context.Response, req.DeviceId, cancellationToken);
                return true;
            }

            // POST /api/tcp/config
            if (actionPath.Equals("config", StringComparison.OrdinalIgnoreCase))
            {
                var req = Deserialize<TcpDeviceRequest>(body);
                if (req?.DeviceId is null || req.Config is null)
                {
                    await WriteErrorAsync(context.Response, "missing_fields", cancellationToken);
                    return true;
                }
                await HandleTcpConfigAsync(context.Response, req.DeviceId, req.Config, cancellationToken);
                return true;
            }

            // POST /api/tcp/write
            if (actionPath.Equals("write", StringComparison.OrdinalIgnoreCase))
            {
                var req = Deserialize<TcpWriteRequest>(body);
                if (req?.DeviceId is null || req.Data is null)
                {
                    await WriteErrorAsync(context.Response, "missing_fields", cancellationToken);
                    return true;
                }
                await HandleTcpWriteAsync(context.Response, req.DeviceId, req.Data, cancellationToken);
                return true;
            }

            // POST /api/tcp/writeBin
            if (actionPath.Equals("writeBin", StringComparison.OrdinalIgnoreCase))
            {
                var req = Deserialize<TcpWriteBinRequest>(body);
                if (req?.DeviceId is null || req.Data is null)
                {
                    await WriteErrorAsync(context.Response, "missing_fields", cancellationToken);
                    return true;
                }
                await HandleTcpWriteBinAsync(context.Response, req.DeviceId, req.Data, cancellationToken);
                return true;
            }

            // POST /api/tcp/read
            if (actionPath.Equals("read", StringComparison.OrdinalIgnoreCase))
            {
                var req = Deserialize<TcpWriteRequest>(body);
                if (req?.DeviceId is null)
                {
                    await WriteErrorAsync(context.Response, "missing_device_id", cancellationToken);
                    return true;
                }
                await HandleTcpReadAsync(context.Response, req.DeviceId, cancellationToken);
                return true;
            }

            // POST /api/tcp/discard
            if (actionPath.Equals("discard", StringComparison.OrdinalIgnoreCase))
            {
                var req = Deserialize<TcpWriteRequest>(body);
                if (req?.DeviceId is null)
                {
                    await WriteErrorAsync(context.Response, "missing_device_id", cancellationToken);
                    return true;
                }
                await HandleTcpDiscardAsync(context.Response, req.DeviceId, cancellationToken);
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

    private async Task HandleTcpStatusAsync(
        HttpListenerResponse response,
        string deviceId,
        CancellationToken cancellationToken)
    {
        var result = TcpDeviceApi.GetStatus(Runtime, deviceId.Trim());
        if (result is null)
        {
            await WriteErrorAsync(response, "device_not_found", cancellationToken);
            return;
        }

        var payload = JsonSerializer.Serialize(result, SnapshotJsonOptions);
        await WriteResponseAsync(response, "application/json; charset=utf-8", payload, cancellationToken);
    }

    private async Task HandleTcpConnectAsync(
        HttpListenerResponse response,
        string deviceId,
        TcpConfigRequest configReq,
        CancellationToken cancellationToken)
    {
        var config = ToConfig(configReq);
        var err = TcpDeviceApi.OpenConnection(Runtime, deviceId.Trim(), config);
        if (err != TcpErrorCode.Ok)
        {
            await WriteErrorAsync(response, $"error_{err}", cancellationToken);
            return;
        }

        await WriteSuccessAsync(response, "connected", cancellationToken);
    }

    private async Task HandleTcpDisconnectAsync(
        HttpListenerResponse response,
        string deviceId,
        CancellationToken cancellationToken)
    {
        var err = TcpDeviceApi.CloseConnection(Runtime, deviceId.Trim());
        if (err != TcpErrorCode.Ok)
        {
            await WriteErrorAsync(response, $"error_{err}", cancellationToken);
            return;
        }

        await WriteSuccessAsync(response, "disconnected", cancellationToken);
    }

    private async Task HandleTcpConfigAsync(
        HttpListenerResponse response,
        string deviceId,
        TcpConfigRequest configReq,
        CancellationToken cancellationToken)
    {
        var config = ToConfig(configReq);
        var err = TcpDeviceApi.SetConfig(Runtime, deviceId.Trim(), config);
        if (err != TcpErrorCode.Ok)
        {
            await WriteErrorAsync(response, $"error_{err}", cancellationToken);
            return;
        }

        await WriteSuccessAsync(response, "config_applied", cancellationToken);
    }

    private async Task HandleTcpWriteAsync(
        HttpListenerResponse response,
        string deviceId,
        string data,
        CancellationToken cancellationToken)
    {
        var err = TcpDeviceApi.WriteText(Runtime, deviceId.Trim(), data);
        if (err != TcpErrorCode.Ok)
        {
            await WriteErrorAsync(response, $"error_{err}", cancellationToken);
            return;
        }

        await WriteSuccessAsync(response, "data_sent", cancellationToken);
    }

    private async Task HandleTcpWriteBinAsync(
        HttpListenerResponse response,
        string deviceId,
        byte[] data,
        CancellationToken cancellationToken)
    {
        var err = TcpDeviceApi.WriteBinary(Runtime, deviceId.Trim(), data);
        if (err != TcpErrorCode.Ok)
        {
            await WriteErrorAsync(response, $"error_{err}", cancellationToken);
            return;
        }

        await WriteSuccessAsync(response, "data_sent", cancellationToken);
    }

    private async Task HandleTcpReadAsync(
        HttpListenerResponse response,
        string deviceId,
        CancellationToken cancellationToken)
    {
        var (err, data) = TcpDeviceApi.ReadAll(Runtime, deviceId.Trim());
        if (err != TcpErrorCode.Ok)
        {
            await WriteErrorAsync(response, $"error_{err}", cancellationToken);
            return;
        }

        var payload = JsonSerializer.Serialize(new { success = true, data }, SnapshotJsonOptions);
        await WriteResponseAsync(response, "application/json; charset=utf-8", payload, cancellationToken);
    }

    private async Task HandleTcpDiscardAsync(
        HttpListenerResponse response,
        string deviceId,
        CancellationToken cancellationToken)
    {
        var err = TcpDeviceApi.DiscardBuffers(Runtime, deviceId.Trim());
        if (err != TcpErrorCode.Ok)
        {
            await WriteErrorAsync(response, $"error_{err}", cancellationToken);
            return;
        }

        await WriteSuccessAsync(response, "buffers_discarded", cancellationToken);
    }

    private static TcpPortConfig ToConfig(TcpConfigRequest req)
    {
        return new TcpPortConfig
        {
            Host = req.Host ?? "127.0.0.1",
            Port = req.Port ?? 5000,
            ConnectTimeout = req.ConnectTimeout ?? 5000,
            ReadTimeout = req.ReadTimeout ?? 5000,
            WriteTimeout = req.WriteTimeout ?? 5000,
            NoDelay = req.NoDelay ?? false,
            KeepAlive = req.KeepAlive ?? true
        };
    }
}
