using System.Diagnostics;

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

    public static Dictionary<string, string> DefaultMysqlParameters() =>
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["host"] = "mysql6.sqlpub.com",
            ["port"] = "3311",
            ["database"] = "mdkossdb",
            ["user"] = "mdkossdb",
            ["password"] = "",
            ["connectTimeout"] = "15000",
            ["commandTimeout"] = "30000",
            ["charset"] = "utf8mb4",
            ["sslMode"] = "None",
            ["autoConnect"] = "false",
        };

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

        var process = Process.GetCurrentProcess().ProcessName;
        return !process.Contains("testhost", StringComparison.OrdinalIgnoreCase)
               && !process.Contains("vstest", StringComparison.OrdinalIgnoreCase)
               && !process.Contains("ReSharperTestRunner", StringComparison.OrdinalIgnoreCase);
    }
}
