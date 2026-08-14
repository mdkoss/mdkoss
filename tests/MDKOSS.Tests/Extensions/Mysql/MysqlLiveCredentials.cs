using System.Globalization;
using System.Text.RegularExpressions;
using MDKOSS.Extensions.Mysql;

namespace MDKOSS.Tests.Extensions.Mysql;

/// <summary>
/// Live SQL credentials: env <c>MDKOSS_MYSQL_*</c>, else local gitignored
/// <c>scripts/mdkossdb/test_conn.py</c> (same source as the Python smoke test).
/// </summary>
internal static class MysqlLiveCredentials
{
    public static bool TryLoad(out Dictionary<string, string> parameters)
    {
        if (TryFromEnv(out parameters))
        {
            return true;
        }

        var py = FindTestConnPy();
        return py is not null && TryFromPython(py, out parameters);
    }

    private static bool TryFromEnv(out Dictionary<string, string> parameters)
    {
        parameters = null!;
        var host = Environment.GetEnvironmentVariable("MDKOSS_MYSQL_HOST");
        if (string.IsNullOrWhiteSpace(host))
        {
            return false;
        }

        parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["host"] = host.Trim(),
            ["port"] = EnvOr("MDKOSS_MYSQL_PORT", "3306"),
            ["database"] = EnvOr("MDKOSS_MYSQL_DATABASE", ""),
            ["user"] = EnvOr("MDKOSS_MYSQL_USER", "root"),
            ["password"] = Environment.GetEnvironmentVariable("MDKOSS_MYSQL_PASSWORD") ?? "",
            ["charset"] = EnvOr("MDKOSS_MYSQL_CHARSET", "utf8mb4"),
            ["connectTimeout"] = "15000",
            ["commandTimeout"] = "30000",
            ["sslMode"] = "None",
            ["autoConnect"] = "false",
        };
        return true;
    }

    private static string EnvOr(string name, string fallback)
    {
        var v = Environment.GetEnvironmentVariable(name);
        return string.IsNullOrWhiteSpace(v) ? fallback : v.Trim();
    }

    private static string? FindTestConnPy()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "scripts", "mdkossdb", "test_conn.py");
            if (File.Exists(candidate))
            {
                return candidate;
            }

            dir = dir.Parent;
        }

        return null;
    }

    private static bool TryFromPython(string path, out Dictionary<string, string> parameters)
    {
        parameters = null!;
        var text = File.ReadAllText(path);
        if (!TryString(text, "host", out var host) || string.IsNullOrWhiteSpace(host))
        {
            return false;
        }

        TryString(text, "user", out var user);
        TryString(text, "password", out var password);
        TryString(text, "database", out var database);
        TryString(text, "charset", out var charset);
        var port = 3306;
        var portMatch = Regex.Match(text, @"[""']port[""']\s*:\s*(\d+)");
        if (portMatch.Success)
        {
            port = int.Parse(portMatch.Groups[1].Value, CultureInfo.InvariantCulture);
        }

        parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["host"] = host,
            ["port"] = port.ToString(CultureInfo.InvariantCulture),
            ["database"] = database ?? "",
            ["user"] = user ?? "root",
            ["password"] = password ?? "",
            ["charset"] = charset ?? "utf8mb4",
            ["connectTimeout"] = "15000",
            ["commandTimeout"] = "30000",
            ["sslMode"] = "None",
            ["autoConnect"] = "false",
        };
        return true;
    }

    private static bool TryString(string text, string key, out string? value)
    {
        var m = Regex.Match(text, $@"[""']{Regex.Escape(key)}[""']\s*:\s*[""']([^""']*)[""']");
        value = m.Success ? m.Groups[1].Value : null;
        return m.Success;
    }

    public static MysqlDeviceParameters ToDeviceParameters(Dictionary<string, string> raw) =>
        MysqlDeviceParameters.ParseConfig(raw);
}
