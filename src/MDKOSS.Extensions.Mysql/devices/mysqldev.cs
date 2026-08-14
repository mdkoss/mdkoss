using MDKOSS.Core;
using MDKOSS.Core.Drivers;
using MySqlConnector;

namespace MDKOSS.Extensions.Mysql;

/// <summary>MySQL client error / status codes.</summary>
public enum MysqlErrorCode
{
    Ok = 0,
    NotConnected,
    AlreadyConnected,
    ConnectionFailed,
    InvalidParameter,
    Timeout,
    QueryFailed,
    OperationFailed,
}

/// <summary>Result of a SELECT-style query.</summary>
public sealed class MysqlQueryResult
{
    public IReadOnlyList<string> Columns { get; init; } = [];

    public IReadOnlyList<IReadOnlyList<object?>> Rows { get; init; } = [];

    public int RowCount { get; init; }

    public bool Truncated { get; init; }
}

/// <summary>
/// MySQL device (config type <c>mysqldev</c>): wraps a client connection with logical device semantics.
/// Commands: Connect, Disconnect, Ping, SetConfig, Query, Execute, Scalar.
/// </summary>
public sealed class MysqlDevice : MDeviceBase
{
    public const int DefaultMaxRows = 1000;
    public const int AbsoluteMaxRows = 10000;

    private readonly object _lock = new();
    private MysqlDeviceParameters _parameters;
    private MySqlConnection? _connection;
    private string? _lastError;

    public MysqlDevice(string id, string name, MysqlDeviceParameters parameters, MVarStore vars)
        : base(id, name, MDeviceType.Generic, new MysqlLogicalDriver(), vars)
    {
        _parameters = parameters ?? throw new ArgumentNullException(nameof(parameters));
        PublishStatusVarsUnlocked();
    }

    public MysqlDeviceParameters Parameters
    {
        get { lock (_lock) return _parameters; }
    }

    public bool IsConnected
    {
        get { lock (_lock) return IsConnectedUnlocked; }
    }

    private bool IsConnectedUnlocked =>
        _connection is { State: System.Data.ConnectionState.Open };

    public string? LastError
    {
        get { lock (_lock) return _lastError; }
    }

    /// <summary>Opens the MySQL connection with current (or override) parameters.</summary>
    public MysqlErrorCode Connect(MysqlDeviceParameters? overrideParameters = null)
    {
        lock (_lock)
        {
            if (IsConnectedUnlocked)
            {
                return MysqlErrorCode.AlreadyConnected;
            }

            if (overrideParameters is not null)
            {
                _parameters = overrideParameters;
            }

            try
            {
                CleanupUnlocked();
                var connection = new MySqlConnection(_parameters.BuildConnectionString());
                connection.Open();
                _connection = connection;
                _lastError = null;
                State = MDeviceState.Running;
                PublishStatusVarsUnlocked();
                return MysqlErrorCode.Ok;
            }
            catch (MySqlException ex) when (IsTimeout(ex))
            {
                CleanupUnlocked();
                _lastError = ex.Message;
                State = MDeviceState.Fault;
                PublishStatusVarsUnlocked();
                return MysqlErrorCode.Timeout;
            }
            catch (Exception ex)
            {
                CleanupUnlocked();
                _lastError = ex.Message;
                State = MDeviceState.Fault;
                PublishStatusVarsUnlocked();
                return MysqlErrorCode.ConnectionFailed;
            }
        }
    }

    /// <summary>Closes the MySQL connection.</summary>
    public MysqlErrorCode Disconnect()
    {
        lock (_lock)
        {
            if (_connection is null)
            {
                return MysqlErrorCode.NotConnected;
            }

            try
            {
                CleanupUnlocked();
                State = MDeviceState.Stopped;
                PublishStatusVarsUnlocked();
                return MysqlErrorCode.Ok;
            }
            catch (Exception ex)
            {
                _lastError = ex.Message;
                PublishStatusVarsUnlocked();
                return MysqlErrorCode.OperationFailed;
            }
        }
    }

    /// <summary>Updates connection parameters; reconnects if currently open.</summary>
    public MysqlErrorCode SetParameters(MysqlDeviceParameters parameters)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        lock (_lock)
        {
            var wasConnected = IsConnectedUnlocked;
            if (wasConnected)
            {
                CleanupUnlocked();
            }

            _parameters = parameters;
            PublishStatusVarsUnlocked();

            return wasConnected ? Connect() : MysqlErrorCode.Ok;
        }
    }

    /// <summary>Pings the server (requires an open connection).</summary>
    public MysqlErrorCode Ping()
    {
        lock (_lock)
        {
            if (!IsConnectedUnlocked)
            {
                return MysqlErrorCode.NotConnected;
            }

            try
            {
                var ok = _connection!.Ping();
                if (!ok)
                {
                    _lastError = "ping_failed";
                    State = MDeviceState.Fault;
                    PublishStatusVarsUnlocked();
                    return MysqlErrorCode.OperationFailed;
                }

                _lastError = null;
                PublishStatusVarsUnlocked();
                return MysqlErrorCode.Ok;
            }
            catch (Exception ex)
            {
                _lastError = ex.Message;
                PublishStatusVarsUnlocked();
                return MapException(ex);
            }
        }
    }

    /// <summary>Executes a statement that returns a result set.</summary>
    public (MysqlErrorCode error, MysqlQueryResult? result) Query(
        string sql,
        IReadOnlyDictionary<string, object?>? parameters = null,
        int maxRows = DefaultMaxRows)
    {
        if (string.IsNullOrWhiteSpace(sql))
        {
            return (MysqlErrorCode.InvalidParameter, null);
        }

        maxRows = Math.Clamp(maxRows, 1, AbsoluteMaxRows);

        lock (_lock)
        {
            if (!IsConnectedUnlocked)
            {
                return (MysqlErrorCode.NotConnected, null);
            }

            try
            {
                using var cmd = CreateCommandUnlocked(sql, parameters);
                using var reader = cmd.ExecuteReader();
                var columns = new string[reader.FieldCount];
                for (var i = 0; i < reader.FieldCount; i++)
                {
                    columns[i] = reader.GetName(i);
                }

                var rows = new List<IReadOnlyList<object?>>();
                var truncated = false;
                while (reader.Read())
                {
                    if (rows.Count >= maxRows)
                    {
                        truncated = true;
                        break;
                    }

                    var row = new object?[reader.FieldCount];
                    for (var i = 0; i < reader.FieldCount; i++)
                    {
                        row[i] = NormalizeValue(reader.GetValue(i));
                    }

                    rows.Add(row);
                }

                var result = new MysqlQueryResult
                {
                    Columns = columns,
                    Rows = rows,
                    RowCount = rows.Count,
                    Truncated = truncated,
                };

                _lastError = null;
                Vars.Set(BuildVarKey("lastRowCount"), result.RowCount);
                Vars.Set(BuildVarKey("lastTruncated"), truncated);
                PublishStatusVarsUnlocked();
                return (MysqlErrorCode.Ok, result);
            }
            catch (Exception ex)
            {
                _lastError = ex.Message;
                PublishStatusVarsUnlocked();
                return (MapException(ex), null);
            }
        }
    }

    /// <summary>Executes a non-query statement (INSERT / UPDATE / DELETE / DDL).</summary>
    public (MysqlErrorCode error, int affectedRows, long lastInsertId) Execute(
        string sql,
        IReadOnlyDictionary<string, object?>? parameters = null)
    {
        if (string.IsNullOrWhiteSpace(sql))
        {
            return (MysqlErrorCode.InvalidParameter, 0, 0);
        }

        lock (_lock)
        {
            if (!IsConnectedUnlocked)
            {
                return (MysqlErrorCode.NotConnected, 0, 0);
            }

            try
            {
                using var cmd = CreateCommandUnlocked(sql, parameters);
                var affected = cmd.ExecuteNonQuery();
                var lastInsertId = cmd.LastInsertedId;
                _lastError = null;
                Vars.Set(BuildVarKey("lastAffectedRows"), affected);
                Vars.Set(BuildVarKey("lastInsertId"), lastInsertId);
                PublishStatusVarsUnlocked();
                return (MysqlErrorCode.Ok, affected, lastInsertId);
            }
            catch (Exception ex)
            {
                _lastError = ex.Message;
                PublishStatusVarsUnlocked();
                return (MapException(ex), 0, 0);
            }
        }
    }

    /// <summary>Executes and returns the first column of the first row.</summary>
    public (MysqlErrorCode error, object? value) Scalar(
        string sql,
        IReadOnlyDictionary<string, object?>? parameters = null)
    {
        if (string.IsNullOrWhiteSpace(sql))
        {
            return (MysqlErrorCode.InvalidParameter, null);
        }

        lock (_lock)
        {
            if (!IsConnectedUnlocked)
            {
                return (MysqlErrorCode.NotConnected, null);
            }

            try
            {
                using var cmd = CreateCommandUnlocked(sql, parameters);
                var value = NormalizeValue(cmd.ExecuteScalar());
                _lastError = null;
                Vars.Set(BuildVarKey("lastScalar"), value);
                PublishStatusVarsUnlocked();
                return (MysqlErrorCode.Ok, value);
            }
            catch (Exception ex)
            {
                _lastError = ex.Message;
                PublishStatusVarsUnlocked();
                return (MapException(ex), null);
            }
        }
    }

    public override void Start()
    {
        State = MDeviceState.Initialized;
        WriteState("initialized");
        PublishStatusVars();

        if (Parameters.AutoConnect)
        {
            Connect();
        }
    }

    public override void Stop()
    {
        Disconnect();
        base.Stop();
    }

    public override void Dispose()
    {
        Disconnect();
        base.Dispose();
    }

    public override DeviceSnapshot GetSnapshot()
    {
        lock (_lock)
        {
            return new DeviceSnapshot(
                Id,
                Name,
                "mysqldev",
                State.ToString(),
                "mysql",
                IsConnectedUnlocked);
        }
    }

    private MySqlCommand CreateCommandUnlocked(string sql, IReadOnlyDictionary<string, object?>? parameters)
    {
        var cmd = _connection!.CreateCommand();
        cmd.CommandText = sql;
        cmd.CommandTimeout = Math.Max(1, (_parameters.CommandTimeoutMs + 999) / 1000);
        if (parameters is not null)
        {
            foreach (var (key, value) in parameters)
            {
                if (string.IsNullOrWhiteSpace(key))
                {
                    continue;
                }

                var name = key.StartsWith('@') ? key : "@" + key;
                cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);
            }
        }

        return cmd;
    }

    private void PublishStatusVars()
    {
        lock (_lock)
        {
            PublishStatusVarsUnlocked();
        }
    }

    private void PublishStatusVarsUnlocked()
    {
        Vars.Set(BuildVarKey("host"), _parameters.Host);
        Vars.Set(BuildVarKey("port"), _parameters.Port);
        Vars.Set(BuildVarKey("database"), _parameters.Database);
        Vars.Set(BuildVarKey("user"), _parameters.User);
        Vars.Set(BuildVarKey("isConnected"), IsConnectedUnlocked);
        Vars.Set(BuildVarKey("lastError"), _lastError ?? "");
        WriteState(State.ToString().ToLowerInvariant());
    }

    private void CleanupUnlocked()
    {
        try
        {
            _connection?.Close();
        }
        catch
        {
            // ignore
        }

        try
        {
            _connection?.Dispose();
        }
        catch
        {
            // ignore
        }

        _connection = null;
    }

    private static MysqlErrorCode MapException(Exception ex)
    {
        if (IsTimeout(ex))
        {
            return MysqlErrorCode.Timeout;
        }

        return ex is MySqlException ? MysqlErrorCode.QueryFailed : MysqlErrorCode.OperationFailed;
    }

    private static bool IsTimeout(Exception ex)
    {
        if (ex is TimeoutException)
        {
            return true;
        }

        if (ex is MySqlException mysql)
        {
            var code = mysql.ErrorCode.ToString();
            return code.Contains("Timeout", StringComparison.OrdinalIgnoreCase)
                   || mysql.Message.Contains("timeout", StringComparison.OrdinalIgnoreCase);
        }

        return ex.InnerException is not null && IsTimeout(ex.InnerException);
    }

    private static object? NormalizeValue(object? value)
    {
        if (value is null or DBNull)
        {
            return null;
        }

        return value switch
        {
            DateTime dt => dt.ToString("O"),
            DateTimeOffset dto => dto.ToString("O"),
            TimeSpan ts => ts.ToString(),
            byte[] bytes => Convert.ToBase64String(bytes),
            Guid guid => guid.ToString(),
            decimal or double or float or byte or sbyte or short or ushort or int or uint or long or ulong or bool or string => value,
            _ => value.ToString(),
        };
    }
}

/// <summary>Minimal IDriver stub — MySQL I/O lives on the device, not a motion card.</summary>
internal sealed class MysqlLogicalDriver : IDriver
{
    public string Name => "MYSQL";

    public bool IsConnected => true;

    public void Initialize(MdkSetting.DriverConfig config) { }

    public bool TryRead(string address, out object? value)
    {
        value = null;
        return false;
    }

    public bool Write(string address, object? value) => false;

    public bool TryReadDi(short diType, out int value)
    {
        value = 0;
        return false;
    }

    public bool TryReadDo(short doType, out int value)
    {
        value = 0;
        return false;
    }

    public bool WriteDo(short doType, int value) => false;

    public bool WriteDoBit(short doType, short doIndex, bool value) => false;

    public bool EnableAxis(short axis) => false;

    public bool DisableAxis(short axis) => false;

    public bool IsAxisEnabled(short axis) => false;

    public bool TryGetAxisStatus(short axis, out int status)
    {
        status = 0;
        return false;
    }

    public bool TryGetAxisPrfPosition(short axis, out double position)
    {
        position = 0;
        return false;
    }

    public bool TryGetAxisEncPosition(short axis, out double position)
    {
        position = 0;
        return false;
    }

    public bool TryGetAxisVelocity(short axis, out double velocity)
    {
        velocity = 0;
        return false;
    }

    public bool SetAxisPosition(short axis, double position) => false;

    public bool SetAxisVelocity(short axis, double velocity) => false;

    public bool SetAxisAcceleration(short axis, double acceleration) => false;

    public bool SetAxisDeceleration(short axis, double deceleration) => false;

    public bool MoveAxisTrap(short axis, int targetPosition, double velocity, double acceleration, double deceleration)
        => false;

    public bool MoveAxisJog(short axis, double velocity, double acceleration, double deceleration) => false;

    public bool MoveAxisHome(short axis, short homeMode, double velocity, double acceleration, double deceleration)
        => false;

    public bool Stop(int axisMask, int option = 0) => false;

    public void Dispose() { }
}
