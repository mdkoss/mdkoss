using System.Globalization;
using MySqlConnector;

namespace MDKOSS.Extensions.Mysql;

/// <summary>MySQL connection configuration from device parameters.</summary>
public sealed class MysqlDeviceParameters
{
    public string Host { get; init; } = "127.0.0.1";

    public int Port { get; init; } = 3306;

    public string Database { get; init; } = "";

    public string User { get; init; } = "root";

    public string Password { get; init; } = "";

    public int ConnectTimeoutMs { get; init; } = 5000;

    public int CommandTimeoutMs { get; init; } = 30000;

    public string Charset { get; init; } = "utf8mb4";

    public bool Pooling { get; init; } = true;

    public MySqlSslMode SslMode { get; init; } = MySqlSslMode.None;

    /// <summary>When true, connect in <see cref="MysqlDevice.Start"/>.</summary>
    public bool AutoConnect { get; init; }

    public static MysqlDeviceParameters ParseConfig(IReadOnlyDictionary<string, string>? parameters)
    {
        parameters ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        return new MysqlDeviceParameters
        {
            Host = ReadString(parameters, "host", "127.0.0.1"),
            Port = Math.Clamp(ReadInt(parameters, "port", 3306), 1, 65535),
            Database = ReadString(parameters, "database", ""),
            User = ReadString(parameters, "user", "root"),
            Password = ReadString(parameters, "password", ""),
            ConnectTimeoutMs = Math.Max(100, ReadInt(parameters, "connectTimeout", ReadInt(parameters, "connectTimeoutMs", 5000))),
            CommandTimeoutMs = Math.Max(100, ReadInt(parameters, "commandTimeout", ReadInt(parameters, "commandTimeoutMs", 30000))),
            Charset = ReadString(parameters, "charset", "utf8mb4"),
            Pooling = ReadBool(parameters, "pooling", true),
            SslMode = ParseSslMode(ReadString(parameters, "sslMode", "None")),
            AutoConnect = ReadBool(parameters, "autoConnect", false),
        };
    }

    public string BuildConnectionString()
    {
        var builder = new MySqlConnectionStringBuilder
        {
            Server = Host,
            Port = (uint)Port,
            UserID = User,
            Password = Password,
            CharacterSet = Charset,
            Pooling = Pooling,
            SslMode = SslMode,
            ConnectionTimeout = ToSeconds(ConnectTimeoutMs),
            DefaultCommandTimeout = ToSeconds(CommandTimeoutMs),
        };

        if (!string.IsNullOrWhiteSpace(Database))
        {
            builder.Database = Database;
        }

        return builder.ConnectionString;
    }

    internal MysqlDeviceParameters WithOverrides(
        string? host,
        int? port,
        string? database,
        string? user,
        string? password,
        int? connectTimeoutMs,
        int? commandTimeoutMs,
        string? charset,
        bool? pooling,
        string? sslMode,
        bool? autoConnect)
    {
        return new MysqlDeviceParameters
        {
            Host = string.IsNullOrWhiteSpace(host) ? Host : host.Trim(),
            Port = port is int p ? Math.Clamp(p, 1, 65535) : Port,
            Database = database ?? Database,
            User = string.IsNullOrWhiteSpace(user) ? User : user.Trim(),
            Password = password ?? Password,
            ConnectTimeoutMs = connectTimeoutMs is int ct ? Math.Max(100, ct) : ConnectTimeoutMs,
            CommandTimeoutMs = commandTimeoutMs is int cmdt ? Math.Max(100, cmdt) : CommandTimeoutMs,
            Charset = string.IsNullOrWhiteSpace(charset) ? Charset : charset.Trim(),
            Pooling = pooling ?? Pooling,
            SslMode = string.IsNullOrWhiteSpace(sslMode) ? SslMode : ParseSslMode(sslMode),
            AutoConnect = autoConnect ?? AutoConnect,
        };
    }

    private static uint ToSeconds(int milliseconds) =>
        (uint)Math.Max(1, (milliseconds + 999) / 1000);

    private static MySqlSslMode ParseSslMode(string raw)
    {
        return raw.Trim().ToLowerInvariant() switch
        {
            "none" or "disabled" => MySqlSslMode.None,
            "preferred" or "prefer" => MySqlSslMode.Preferred,
            "required" or "require" => MySqlSslMode.Required,
            "verifyca" or "verify_ca" => MySqlSslMode.VerifyCA,
            "verifyfull" or "verify_full" => MySqlSslMode.VerifyFull,
            _ => MySqlSslMode.None,
        };
    }

    private static string ReadString(IReadOnlyDictionary<string, string> parameters, string key, string fallback)
    {
        return parameters.TryGetValue(key, out var raw) && !string.IsNullOrWhiteSpace(raw)
            ? raw.Trim()
            : fallback;
    }

    private static int ReadInt(IReadOnlyDictionary<string, string> parameters, string key, int fallback)
    {
        if (!parameters.TryGetValue(key, out var raw) || string.IsNullOrWhiteSpace(raw))
        {
            return fallback;
        }

        return int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : fallback;
    }

    private static bool ReadBool(IReadOnlyDictionary<string, string> parameters, string key, bool fallback)
    {
        if (!parameters.TryGetValue(key, out var raw) || string.IsNullOrWhiteSpace(raw))
        {
            return fallback;
        }

        return bool.TryParse(raw, out var value) ? value : fallback;
    }
}
