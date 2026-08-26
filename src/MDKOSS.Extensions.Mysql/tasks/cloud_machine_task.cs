using MDKOSS.Core;

namespace MDKOSS.Extensions.Mysql;

/// <summary>
/// Heartbeat task: upserts <see cref="MachineMonitorRecord"/> into public table <c>machine</c>
/// via a <see cref="MysqlDevice"/>. Each tick connects, writes, then disconnects.
/// Failures are warning-logged only (the task does not go <see cref="MTaskState.Fault"/>).
/// </summary>
public sealed class CloudMachineTask : MTaskBase
{
    public const string TaskName = MdkCloudMonitor.TaskName;
    public const int DefaultIntervalMs = MdkCloudMonitor.DefaultIntervalMs;

    private readonly MysqlDevice _mysql;
    private readonly Func<MachineMonitorRecord> _getMonitor;
    private readonly MVarStore _vars;
    private int _busy;

    public CloudMachineTask(
        MysqlDevice mysql,
        Func<MachineMonitorRecord> getMonitor,
        MVarStore vars,
        int intervalMs = DefaultIntervalMs)
        : base(TaskName, intervalMs > 0 ? Math.Max(3_000, intervalMs) : DefaultIntervalMs)
    {
        _mysql = mysql;
        _getMonitor = getMonitor;
        _vars = vars;
    }

    public static MTaskBase? Create(TaskBootstrapContext ctx, MdkSetting.TaskConfig config)
    {
        if (ctx.GetMachineMonitor is null)
        {
            return null;
        }

        var mysql = ResolveMysql(ctx, config.Parameters);
        if (mysql is null)
        {
            return null;
        }

        var interval = config.IntervalMs > 0 ? config.IntervalMs : DefaultIntervalMs;
        return new CloudMachineTask(mysql, ctx.GetMachineMonitor, ctx.Vars, interval);
    }

    protected override Task TickAsync(CancellationToken cancellationToken)
    {
        if (Interlocked.Exchange(ref _busy, 1) == 1)
        {
            return Task.CompletedTask;
        }

        try
        {
            if (!_mysql.IsConnected)
            {
                var connect = _mysql.Connect(markFaultOnError: false);
                if (connect != MysqlErrorCode.Ok)
                {
                    Warn($"connect:{connect}:{_mysql.LastError}");
                    return Task.CompletedTask;
                }
            }

            var record = _getMonitor();
            var (error, _, _) = _mysql.Execute(MachineMonitorRecord.UpsertSql, record.ToUpsertParameters());
            if (error != MysqlErrorCode.Ok)
            {
                Warn($"upsert:{error}:{_mysql.LastError}");
                return Task.CompletedTask;
            }

            _vars.Set("cloud.machine.id", record.Id);
            _vars.Set("cloud.machine.lastOkUtc", DateTime.UtcNow);
            _vars.Set("cloud.machine.lastError", string.Empty);
            AppLog.Info($"Cloud machine heartbeat ok id={record.Id}");
        }
        catch (Exception ex)
        {
            Warn(MysqlDevice.FlattenException(ex));
        }
        finally
        {
            DisconnectQuiet();
            Interlocked.Exchange(ref _busy, 0);
        }

        return Task.CompletedTask;
    }

    private void Warn(string message)
    {
        AppLog.Warn($"Cloud machine heartbeat failed: {message}");
        _vars.Set("cloud.machine.lastError", message ?? string.Empty);
    }

    private void DisconnectQuiet()
    {
        try
        {
            _mysql.Disconnect();
        }
        catch (Exception ex)
        {
            AppLog.Warn($"Cloud machine disconnect failed: {ex.Message}");
        }
    }

    private static MysqlDevice? ResolveMysql(
        TaskBootstrapContext ctx,
        IReadOnlyDictionary<string, string>? parameters)
    {
        if (parameters is not null
            && parameters.TryGetValue("mysqlDeviceId", out var deviceId)
            && !string.IsNullOrWhiteSpace(deviceId)
            && ctx.Devices.TryGetValue(deviceId.Trim(), out var mapped)
            && mapped is MysqlDevice named)
        {
            return named;
        }

        return ctx.Devices.TryGetValue(MdkCloudMonitor.MysqlDeviceId, out var cloud)
               && cloud is MysqlDevice cloudMysql
            ? cloudMysql
            : ctx.Devices.Values.OfType<MysqlDevice>().FirstOrDefault();
    }
}
