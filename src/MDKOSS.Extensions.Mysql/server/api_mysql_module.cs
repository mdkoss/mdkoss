using System.Net;
using System.Text.Json;
using MDKOSS.Extensions.Mysql;

namespace MDKOSS.Core.Monitor;

/// <summary>
/// Handles /api/mysql/* — connect, disconnect, config, ping, query, execute, scalar.
/// </summary>
public sealed class MysqlApiModule : MonitoringApiModule
{
    private sealed class MysqlConfigRequest
    {
        public string? Host { get; set; }
        public int? Port { get; set; }
        public string? Database { get; set; }
        public string? User { get; set; }
        public string? Password { get; set; }
        public int? ConnectTimeout { get; set; }
        public int? ConnectTimeoutMs { get; set; }
        public int? CommandTimeout { get; set; }
        public int? CommandTimeoutMs { get; set; }
        public string? Charset { get; set; }
        public bool? Pooling { get; set; }
        public string? SslMode { get; set; }
        public bool? AutoConnect { get; set; }
    }

    private sealed class MysqlRequest
    {
        public string? DeviceId { get; set; }
        public string? Sql { get; set; }
        public int? MaxRows { get; set; }
        public Dictionary<string, JsonElement>? Parameters { get; set; }
        public MysqlConfigRequest? Config { get; set; }
    }

    public MysqlApiModule(MdkRuntime runtime) : base(runtime) { }

    public override string RoutePrefix => "/api/mysql";

    public override async Task<bool> HandleAsync(
        HttpListenerContext context,
        string remainingPath,
        CancellationToken cancellationToken)
    {
        var actionPath = remainingPath.Trim('/');
        var isPost = string.Equals(context.Request.HttpMethod, "POST", StringComparison.OrdinalIgnoreCase);

        try
        {
            if (actionPath.Equals("status", StringComparison.OrdinalIgnoreCase) && !isPost)
            {
                var query = context.Request.QueryString?["deviceId"];
                if (string.IsNullOrWhiteSpace(query))
                {
                    await WriteErrorAsync(context.Response, "missing_device_id", cancellationToken);
                    return true;
                }

                await HandleStatusAsync(context.Response, query, cancellationToken);
                return true;
            }

            if (!isPost)
            {
                await WriteErrorAsync(context.Response, "method_not_allowed", cancellationToken);
                return true;
            }

            var body = await ReadBodyAsync(context.Request, cancellationToken).ConfigureAwait(false);
            var req = Deserialize<MysqlRequest>(body);
            if (req?.DeviceId is null)
            {
                await WriteErrorAsync(context.Response, "missing_device_id", cancellationToken);
                return true;
            }

            var deviceId = req.DeviceId.Trim();

            if (actionPath.Equals("connect", StringComparison.OrdinalIgnoreCase))
            {
                var config = ToConfig(Runtime, deviceId, req.Config);
                var err = MysqlDeviceApi.OpenConnection(Runtime, deviceId, config);
                if (err != MysqlErrorCode.Ok)
                {
                    await WriteErrorAsync(context.Response, $"error_{err}", cancellationToken);
                    return true;
                }

                await WriteSuccessAsync(context.Response, "connected", cancellationToken);
                return true;
            }

            if (actionPath.Equals("disconnect", StringComparison.OrdinalIgnoreCase))
            {
                var err = MysqlDeviceApi.CloseConnection(Runtime, deviceId);
                if (err != MysqlErrorCode.Ok && err != MysqlErrorCode.NotConnected)
                {
                    await WriteErrorAsync(context.Response, $"error_{err}", cancellationToken);
                    return true;
                }

                await WriteSuccessAsync(context.Response, "disconnected", cancellationToken);
                return true;
            }

            if (actionPath.Equals("config", StringComparison.OrdinalIgnoreCase))
            {
                if (req.Config is null)
                {
                    await WriteErrorAsync(context.Response, "missing_fields", cancellationToken);
                    return true;
                }

                var config = ToConfig(Runtime, deviceId, req.Config);
                var err = MysqlDeviceApi.SetConfig(Runtime, deviceId, config);
                if (err != MysqlErrorCode.Ok)
                {
                    await WriteErrorAsync(context.Response, $"error_{err}", cancellationToken);
                    return true;
                }

                await WriteSuccessAsync(context.Response, "config_applied", cancellationToken);
                return true;
            }

            if (actionPath.Equals("ping", StringComparison.OrdinalIgnoreCase))
            {
                var err = MysqlDeviceApi.Ping(Runtime, deviceId);
                if (err != MysqlErrorCode.Ok)
                {
                    await WriteErrorAsync(context.Response, $"error_{err}", cancellationToken);
                    return true;
                }

                await WriteSuccessAsync(context.Response, "pong", cancellationToken);
                return true;
            }

            if (actionPath.Equals("query", StringComparison.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(req.Sql))
                {
                    await WriteErrorAsync(context.Response, "missing_sql", cancellationToken);
                    return true;
                }

                var sqlParams = ToSqlParameters(req.Parameters);
                var maxRows = req.MaxRows ?? MysqlDevice.DefaultMaxRows;
                var (err, result) = MysqlDeviceApi.Query(Runtime, deviceId, req.Sql, sqlParams, maxRows);
                if (err != MysqlErrorCode.Ok || result is null)
                {
                    await WriteErrorAsync(context.Response, $"error_{err}", cancellationToken);
                    return true;
                }

                var payload = JsonSerializer.Serialize(new
                {
                    success = true,
                    columns = result.Columns,
                    rows = result.Rows,
                    rowCount = result.RowCount,
                    truncated = result.Truncated,
                }, SnapshotJsonOptions);
                await WriteResponseAsync(context.Response, "application/json; charset=utf-8", payload, cancellationToken);
                return true;
            }

            if (actionPath.Equals("execute", StringComparison.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(req.Sql))
                {
                    await WriteErrorAsync(context.Response, "missing_sql", cancellationToken);
                    return true;
                }

                var sqlParams = ToSqlParameters(req.Parameters);
                var (err, affected, lastInsertId) = MysqlDeviceApi.Execute(Runtime, deviceId, req.Sql, sqlParams);
                if (err != MysqlErrorCode.Ok)
                {
                    await WriteErrorAsync(context.Response, $"error_{err}", cancellationToken);
                    return true;
                }

                var payload = JsonSerializer.Serialize(new
                {
                    success = true,
                    affectedRows = affected,
                    lastInsertId,
                }, SnapshotJsonOptions);
                await WriteResponseAsync(context.Response, "application/json; charset=utf-8", payload, cancellationToken);
                return true;
            }

            if (actionPath.Equals("scalar", StringComparison.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(req.Sql))
                {
                    await WriteErrorAsync(context.Response, "missing_sql", cancellationToken);
                    return true;
                }

                var sqlParams = ToSqlParameters(req.Parameters);
                var (err, value) = MysqlDeviceApi.Scalar(Runtime, deviceId, req.Sql, sqlParams);
                if (err != MysqlErrorCode.Ok)
                {
                    await WriteErrorAsync(context.Response, $"error_{err}", cancellationToken);
                    return true;
                }

                var payload = JsonSerializer.Serialize(new { success = true, value }, SnapshotJsonOptions);
                await WriteResponseAsync(context.Response, "application/json; charset=utf-8", payload, cancellationToken);
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

    private async Task HandleStatusAsync(
        HttpListenerResponse response,
        string deviceId,
        CancellationToken cancellationToken)
    {
        var result = MysqlDeviceApi.GetStatus(Runtime, deviceId.Trim());
        if (result is null)
        {
            await WriteErrorAsync(response, "device_not_found", cancellationToken);
            return;
        }

        var payload = JsonSerializer.Serialize(result, SnapshotJsonOptions);
        await WriteResponseAsync(response, "application/json; charset=utf-8", payload, cancellationToken);
    }

    private static MysqlDeviceParameters ToConfig(MdkRuntime runtime, string deviceId, MysqlConfigRequest? req)
    {
        var current = runtime.TryGetDevice(deviceId, out var dev) && dev is MysqlDevice mysql
            ? mysql.Parameters
            : new MysqlDeviceParameters();

        if (req is null)
        {
            return current;
        }

        return current.WithOverrides(
            req.Host,
            req.Port,
            req.Database,
            req.User,
            req.Password,
            req.ConnectTimeoutMs ?? req.ConnectTimeout,
            req.CommandTimeoutMs ?? req.CommandTimeout,
            req.Charset,
            req.Pooling,
            req.SslMode,
            req.AutoConnect);
    }

    private static IReadOnlyDictionary<string, object?>? ToSqlParameters(
        Dictionary<string, JsonElement>? parameters)
    {
        if (parameters is null || parameters.Count == 0)
        {
            return null;
        }

        var map = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in parameters)
        {
            map[key] = MysqlDeviceApi.FromJson(value);
        }

        return map;
    }
}
