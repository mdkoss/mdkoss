using System.Text.Json;
using MDKOSS.Core;
using MDKOSS.Core.Monitor;
using MDKOSS.Extensions;

namespace MDKOSS.Extensions.Mysql;

/// <summary>MySQL device extension package (config type <c>mysqldev</c>).</summary>
public sealed class MysqlExtension : IMdkExtension
{
    public string Id => "mysql";

    public string DisplayName => "MySQL device";

    public void Register(IExtensionRegistration registration)
    {
        ArgumentNullException.ThrowIfNull(registration);

        registration.Device("mysqldev", (cfg, name, vars, _) =>
        {
            var parameters = MysqlDeviceParameters.ParseConfig(cfg.Parameters);
            return new MysqlDevice(cfg.Id, name, parameters, vars);
        });

        registration.Action(
            device => device is MysqlDevice,
            (device, action, parameters) =>
                MysqlDeviceActions.Execute((MysqlDevice)device, action, parameters));

        registration.MonitoringModule(runtime => new MysqlApiModule(runtime));
    }
}

/// <summary>Call once before creating <see cref="MdkRuntime"/>.</summary>
public static class MysqlExtensionBootstrap
{
    public static void Register() => MdkExtensionHost.Register(new MysqlExtension());
}

/// <summary>Unified action handlers for <see cref="MysqlDevice"/>.</summary>
internal static class MysqlDeviceActions
{
    internal static DeviceActionResult Execute(
        MysqlDevice mysql,
        string action,
        Dictionary<string, JsonElement>? parameters)
    {
        return action.ToLowerInvariant() switch
        {
            "connect" or "open" => ToResult(mysql.Connect(), "connect_failed"),
            "disconnect" or "close" => ToResult(mysql.Disconnect(), "disconnect_failed"),
            "ping" => ToResult(mysql.Ping(), "ping_failed"),
            "status" => DeviceActionResult.Ok(StatusPayload(mysql)),
            "query" => HandleQuery(mysql, parameters),
            "execute" => HandleExecute(mysql, parameters),
            "scalar" => HandleScalar(mysql, parameters),
            _ => DeviceActionResult.Fail("unknown_action"),
        };
    }

    private static object StatusPayload(MysqlDevice mysql)
    {
        var cfg = mysql.Parameters;
        return new
        {
            mysql.Id,
            isConnected = mysql.IsConnected,
            host = cfg.Host,
            port = cfg.Port,
            database = cfg.Database,
            user = cfg.User,
            autoConnect = cfg.AutoConnect,
            lastError = mysql.LastError,
        };
    }

    private static DeviceActionResult HandleQuery(MysqlDevice mysql, Dictionary<string, JsonElement>? parameters)
    {
        if (!TryGetSql(parameters, out var sql, out var error))
        {
            return DeviceActionResult.Fail(error!);
        }

        var sqlParams = TryGetSqlParameters(parameters);
        var maxRows = TryGetInt(parameters, "maxRows") ?? MysqlDevice.DefaultMaxRows;
        var (code, result) = mysql.Query(sql, sqlParams, maxRows);
        return code == MysqlErrorCode.Ok && result is not null
            ? DeviceActionResult.Ok(result)
            : DeviceActionResult.Fail(FailToken("query_failed", code, mysql.LastError));
    }

    private static DeviceActionResult HandleExecute(MysqlDevice mysql, Dictionary<string, JsonElement>? parameters)
    {
        if (!TryGetSql(parameters, out var sql, out var error))
        {
            return DeviceActionResult.Fail(error!);
        }

        var sqlParams = TryGetSqlParameters(parameters);
        var (code, affected, lastInsertId) = mysql.Execute(sql, sqlParams);
        return code == MysqlErrorCode.Ok
            ? DeviceActionResult.Ok(new { affectedRows = affected, lastInsertId })
            : DeviceActionResult.Fail(FailToken("execute_failed", code, mysql.LastError));
    }

    private static DeviceActionResult HandleScalar(MysqlDevice mysql, Dictionary<string, JsonElement>? parameters)
    {
        if (!TryGetSql(parameters, out var sql, out var error))
        {
            return DeviceActionResult.Fail(error!);
        }

        var sqlParams = TryGetSqlParameters(parameters);
        var (code, value) = mysql.Scalar(sql, sqlParams);
        return code == MysqlErrorCode.Ok
            ? DeviceActionResult.Ok(new { value })
            : DeviceActionResult.Fail(FailToken("scalar_failed", code, mysql.LastError));
    }

    private static DeviceActionResult ToResult(MysqlErrorCode code, string failToken)
    {
        return code == MysqlErrorCode.Ok
            ? DeviceActionResult.Ok()
            : DeviceActionResult.Fail($"{failToken}:{code}");
    }

    private static string FailToken(string prefix, MysqlErrorCode code, string? lastError)
    {
        return string.IsNullOrWhiteSpace(lastError)
            ? $"{prefix}:{code}"
            : $"{prefix}:{code}:{lastError}";
    }

    private static bool TryGetSql(Dictionary<string, JsonElement>? parameters, out string sql, out string? error)
    {
        sql = "";
        error = null;
        if (parameters is null
            || !parameters.TryGetValue("sql", out var el)
            || el.ValueKind != JsonValueKind.String)
        {
            error = "missing_sql";
            return false;
        }

        sql = el.GetString() ?? "";
        if (string.IsNullOrWhiteSpace(sql))
        {
            error = "missing_sql";
            return false;
        }

        return true;
    }

    private static int? TryGetInt(Dictionary<string, JsonElement>? parameters, string key)
    {
        if (parameters is null || !parameters.TryGetValue(key, out var el))
        {
            return null;
        }

        if (el.ValueKind == JsonValueKind.Number && el.TryGetInt32(out var n))
        {
            return n;
        }

        if (el.ValueKind == JsonValueKind.String && int.TryParse(el.GetString(), out n))
        {
            return n;
        }

        return null;
    }

    internal static IReadOnlyDictionary<string, object?>? TryGetSqlParameters(
        Dictionary<string, JsonElement>? parameters)
    {
        if (parameters is null || !parameters.TryGetValue("parameters", out var el))
        {
            return null;
        }

        if (el.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var map = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var prop in el.EnumerateObject())
        {
            map[prop.Name] = FromJson(prop.Value);
        }

        return map;
    }

    private static object? FromJson(JsonElement el) => MysqlDeviceApi.FromJson(el);
}
