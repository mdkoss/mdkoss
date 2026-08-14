using System.Text.Json;
using MDKOSS.Core;

namespace MDKOSS.Extensions.Mysql;

/// <summary>MySQL device runtime API for monitoring modules.</summary>
public static class MysqlDeviceApi
{
    public static object? GetStatus(MdkRuntime runtime, string deviceId)
    {
        if (!TryGet(runtime, deviceId, out var mysql))
        {
            return null;
        }

        var cfg = mysql.Parameters;
        return new
        {
            deviceId = mysql.Id,
            isConnected = mysql.IsConnected,
            host = cfg.Host,
            port = cfg.Port,
            database = cfg.Database,
            user = cfg.User,
            charset = cfg.Charset,
            pooling = cfg.Pooling,
            sslMode = cfg.SslMode.ToString(),
            connectTimeoutMs = cfg.ConnectTimeoutMs,
            commandTimeoutMs = cfg.CommandTimeoutMs,
            autoConnect = cfg.AutoConnect,
            lastError = mysql.LastError,
        };
    }

    public static MysqlErrorCode OpenConnection(
        MdkRuntime runtime,
        string deviceId,
        MysqlDeviceParameters? config = null)
    {
        if (!TryGet(runtime, deviceId, out var mysql))
        {
            return MysqlErrorCode.ConnectionFailed;
        }

        var original = mysql.Parameters;
        if (mysql.IsConnected)
        {
            mysql.Disconnect();
        }

        if (config is not null)
        {
            mysql.SetParameters(config);
        }

        var result = mysql.Connect();
        if (result != MysqlErrorCode.Ok)
        {
            mysql.SetParameters(original);
        }

        return result;
    }

    public static MysqlErrorCode CloseConnection(MdkRuntime runtime, string deviceId)
    {
        return TryGet(runtime, deviceId, out var mysql)
            ? mysql.Disconnect()
            : MysqlErrorCode.NotConnected;
    }

    public static MysqlErrorCode SetConfig(MdkRuntime runtime, string deviceId, MysqlDeviceParameters config)
    {
        return TryGet(runtime, deviceId, out var mysql)
            ? mysql.SetParameters(config)
            : MysqlErrorCode.NotConnected;
    }

    public static MysqlErrorCode Ping(MdkRuntime runtime, string deviceId)
    {
        return TryGet(runtime, deviceId, out var mysql)
            ? mysql.Ping()
            : MysqlErrorCode.NotConnected;
    }

    public static (MysqlErrorCode error, MysqlQueryResult? result) Query(
        MdkRuntime runtime,
        string deviceId,
        string sql,
        IReadOnlyDictionary<string, object?>? parameters = null,
        int maxRows = MysqlDevice.DefaultMaxRows)
    {
        return TryGet(runtime, deviceId, out var mysql)
            ? mysql.Query(sql, parameters, maxRows)
            : (MysqlErrorCode.NotConnected, null);
    }

    public static (MysqlErrorCode error, int affectedRows, long lastInsertId) Execute(
        MdkRuntime runtime,
        string deviceId,
        string sql,
        IReadOnlyDictionary<string, object?>? parameters = null)
    {
        return TryGet(runtime, deviceId, out var mysql)
            ? mysql.Execute(sql, parameters)
            : (MysqlErrorCode.NotConnected, 0, 0);
    }

    public static (MysqlErrorCode error, object? value) Scalar(
        MdkRuntime runtime,
        string deviceId,
        string sql,
        IReadOnlyDictionary<string, object?>? parameters = null)
    {
        return TryGet(runtime, deviceId, out var mysql)
            ? mysql.Scalar(sql, parameters)
            : (MysqlErrorCode.NotConnected, null);
    }

    public static object? FromJson(JsonElement el) => el.ValueKind switch
    {
        JsonValueKind.Null or JsonValueKind.Undefined => null,
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.String => el.GetString(),
        JsonValueKind.Number when el.TryGetInt64(out var l) => l,
        JsonValueKind.Number when el.TryGetDouble(out var d) => d,
        _ => el.GetRawText(),
    };

    private static bool TryGet(MdkRuntime runtime, string deviceId, out MysqlDevice mysql)
    {
        mysql = null!;
        if (!runtime.TryGetDevice(deviceId, out var dev) || dev is not MysqlDevice device)
        {
            return false;
        }

        mysql = device;
        return true;
    }
}
