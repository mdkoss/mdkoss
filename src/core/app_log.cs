using NLog;
using NLog.Config;
using NLog.Targets;

namespace MDKOSS.Core;

/// <summary>
/// Application-wide logging facade. Configure once via <see cref="Configure"/> after process start.
/// </summary>
public static class AppLog
{
    private static readonly object Gate = new();
    private static bool _configured;
    private static readonly Logger Logger = LogManager.GetLogger("MDKOSS");

    /// <summary>
    /// Ensures NLog is configured with a daily rolling file: <c>{logDirectory}/yyyyMMdd.log</c>.
    /// </summary>
    /// <param name="logDirectory">Directory for log files; defaults to <c>logs</c> under <see cref="AppContext.BaseDirectory"/>.</param>
    public static void Configure(string? logDirectory = null)
    {
        lock (Gate)
        {
            if (_configured)
            {
                return;
            }

            var dir = string.IsNullOrWhiteSpace(logDirectory)
                ? Path.Combine(AppContext.BaseDirectory, "logs")
                : logDirectory.Trim();
            Directory.CreateDirectory(dir);

            var config = new LoggingConfiguration();
            var fileTarget = new FileTarget("dailyFile")
            {
                FileName = Path.Combine(dir, "${date:format=yyyyMMdd}.log"),
                Encoding = System.Text.Encoding.UTF8,
                Layout = "${longdate}|${level:uppercase=true}|${logger}|${message}${onexception:inner=${newline}${exception:format=tostring}}"
            };

            config.AddRule(LogLevel.Trace, LogLevel.Fatal, fileTarget);
            LogManager.Configuration = config;
            _configured = true;
        }
    }

    public static void Shutdown()
    {
        lock (Gate)
        {
            if (!_configured)
            {
                return;
            }

            LogManager.Shutdown();
            _configured = false;
        }
    }

    public static void Trace(string message) => Logger.Trace(message);

    public static void Debug(string message) => Logger.Debug(message);

    public static void Info(string message) => Logger.Info(message);

    public static void Warn(string message) => Logger.Warn(message);

    public static void Error(string message) => Logger.Error(message);

    public static void Error(Exception exception, string message) => Logger.Error(exception, message);

    public static void Fatal(string message) => Logger.Fatal(message);

    public static void Fatal(Exception exception, string message) => Logger.Fatal(exception, message);
}
