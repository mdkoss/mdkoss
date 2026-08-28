using System.Diagnostics;
using System.Text.RegularExpressions;

namespace MDKOSS.Core;

/// <summary>
/// Implicit system mysqldev + <c>cloud-machine</c> task that heartbeats to public table <c>machine</c>.
/// Skipped when the Mysql plugin is not loaded, when config already defines the same ids,
/// or when <see cref="AutoRegisterEnabled"/> is false (test host / <c>MDKOSS_CLOUD_MONITOR=0</c>).
/// </summary>
public static class MdkCloudMonitor
{
    public const string MysqlDeviceId = "mysql-cloud";
    public const string MysqlDeviceName = "MDKOSS Cloud";
    public const string MysqlDeviceType = "mysqldev";
    public const string TaskType = "cloud-machine";
    public const string TaskName = "task-cloud-machine";
    public const int DefaultIntervalMs = 10_000;
    public const string CloudMonitorEnvVar = "MDKOSS_CLOUD_MONITOR";
    public const string MysqlPasswordEnvVar = "MDKOSS_MYSQL_PASSWORD";

    private static int _passwordSourceLogged;

    public static Dictionary<string, string> DefaultMysqlParameters()
    {
        var password = EnsureMysqlPasswordEnvironment();
        return new(StringComparer.OrdinalIgnoreCase)
        {
            ["host"] = "mysql6.sqlpub.com",
            ["port"] = "3311",
            ["database"] = "mdkossdb",
            ["user"] = "mdkossdb",
            ["password"] = password,
            ["connectTimeout"] = "15000",
            ["commandTimeout"] = "30000",
            ["charset"] = "utf8mb4",
            ["sslMode"] = "None",
            ["autoConnect"] = "false",
        };
    }

    /// <summary>
    /// Loads the cloud password from gitignored <c>scripts/mdkossdb/test_conn.py</c> when present,
    /// writes <see cref="MysqlPasswordEnvVar"/> on this process (and the user environment on real hosts),
    /// otherwise keeps an existing env value.
    /// </summary>
    public static string EnsureMysqlPasswordEnvironment(params string[] extraStartDirectories)
    {
        if (TryReadPasswordFromTestConn(out var fromFile, extraStartDirectories))
        {
            Environment.SetEnvironmentVariable(MysqlPasswordEnvVar, fromFile, EnvironmentVariableTarget.Process);
            PersistUserPassword(fromFile);
            LogPasswordSourceOnce("scripts/mdkossdb/test_conn.py");
            return fromFile;
        }

        var env = Environment.GetEnvironmentVariable(MysqlPasswordEnvVar);
        if (!string.IsNullOrWhiteSpace(env))
        {
            var trimmed = env.Trim();
            LogPasswordSourceOnce(MysqlPasswordEnvVar);
            return trimmed;
        }

        LogPasswordSourceOnce(null);
        return string.Empty;
    }

    /// <summary>Parses <c>CONFIG["password"]</c> from a local <c>test_conn.py</c> snippet.</summary>
    public static bool TryParsePasswordFromTestConnText(string text, out string password)
    {
        password = string.Empty;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var match = Regex.Match(
            text,
            @"[""']password[""']\s*:\s*[""']([^""']*)[""']");
        if (!match.Success)
        {
            return false;
        }

        password = match.Groups[1].Value;
        return !string.IsNullOrWhiteSpace(password);
    }

    /// <summary>
    /// Default on for hosts. Off under testhost unless <c>MDKOSS_CLOUD_MONITOR=1</c>.
    /// Explicit <c>0</c>/<c>false</c> always disables.
    /// </summary>
    public static bool AutoRegisterEnabled()
    {
        var env = Environment.GetEnvironmentVariable(CloudMonitorEnvVar);
        if (!string.IsNullOrWhiteSpace(env))
        {
            var trimmed = env.Trim();
            if (trimmed is "0" or "false" or "no" or "off")
            {
                return false;
            }

            if (trimmed is "1" or "true" or "yes" or "on")
            {
                return true;
            }
        }

        return !IsTestProcess();
    }

    private static bool TryReadPasswordFromTestConn(out string password, params string[] extraStartDirectories)
    {
        password = string.Empty;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var start in EnumerateStartDirectories(extraStartDirectories))
        {
            DirectoryInfo? dir;
            try
            {
                dir = new DirectoryInfo(Path.GetFullPath(start));
            }
            catch
            {
                continue;
            }

            while (dir is not null)
            {
                var candidate = Path.Combine(dir.FullName, "scripts", "mdkossdb", "test_conn.py");
                if (seen.Add(candidate) && File.Exists(candidate))
                {
                    return TryParsePasswordFromTestConnText(File.ReadAllText(candidate), out password);
                }

                dir = dir.Parent;
            }
        }

        return false;
    }

    private static IEnumerable<string> EnumerateStartDirectories(string[] extraStartDirectories)
    {
        yield return AppContext.BaseDirectory;
        yield return Environment.CurrentDirectory;
        foreach (var raw in extraStartDirectories)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                continue;
            }

            var path = raw.Trim();
            yield return File.Exists(path)
                ? Path.GetDirectoryName(path) ?? path
                : path;
        }
    }

    private static void PersistUserPassword(string password)
    {
        if (IsTestProcess())
        {
            return;
        }

        try
        {
            var current = Environment.GetEnvironmentVariable(MysqlPasswordEnvVar, EnvironmentVariableTarget.User);
            if (string.Equals(current, password, StringComparison.Ordinal))
            {
                return;
            }

            Environment.SetEnvironmentVariable(MysqlPasswordEnvVar, password, EnvironmentVariableTarget.User);
            AppLog.Info($"Saved {MysqlPasswordEnvVar} to the user environment.");
        }
        catch (Exception ex)
        {
            AppLog.Warn($"Could not persist {MysqlPasswordEnvVar}: {ex.Message}");
        }
    }

    private static void LogPasswordSourceOnce(string? source)
    {
        if (Interlocked.Exchange(ref _passwordSourceLogged, 1) == 1)
        {
            return;
        }

        if (source is null)
        {
            AppLog.Warn(
                $"Cloud MySQL password missing. Set {MysqlPasswordEnvVar} or add scripts/mdkossdb/test_conn.py.");
            return;
        }

        AppLog.Info($"Cloud MySQL password loaded from {source}.");
    }

    private static bool IsTestProcess()
    {
        var process = Process.GetCurrentProcess().ProcessName;
        return process.Contains("testhost", StringComparison.OrdinalIgnoreCase)
               || process.Contains("vstest", StringComparison.OrdinalIgnoreCase)
               || process.Contains("ReSharperTestRunner", StringComparison.OrdinalIgnoreCase);
    }
}
