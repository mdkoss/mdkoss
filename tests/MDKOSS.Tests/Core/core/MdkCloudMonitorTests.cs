using System.Net;
using System.Net.Sockets;
using MDKOSS.Core;

namespace MDKOSS.Tests.Core;

[Collection("CloudMonitorEnv")]
public sealed class MdkCloudMonitorTests
{
    private static readonly object EnvLock = new();

    private static int GetFreeLoopbackPort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            return ((IPEndPoint)listener.LocalEndpoint).Port;
        }
        finally
        {
            listener.Stop();
        }
    }

    private static MdkRuntime CreateRuntime()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"mdk-cloud-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        var setting = new MdkSetting
        {
            ProjectName = "cloud-auto",
            DatabasePath = Path.Combine(dir, "mdk.db"),
            MonitoringPrefix = $"http://127.0.0.1:{GetFreeLoopbackPort()}/",
        };
        return new MdkRuntime(setting, Path.Combine(dir, "setting.json"));
    }

    [Fact]
    public void AutoRegisterEnabled_respects_env()
    {
        lock (EnvLock)
        {
            var previous = Environment.GetEnvironmentVariable(MdkCloudMonitor.CloudMonitorEnvVar);
            try
            {
                Environment.SetEnvironmentVariable(MdkCloudMonitor.CloudMonitorEnvVar, "0");
                Assert.False(MdkCloudMonitor.AutoRegisterEnabled());
                Environment.SetEnvironmentVariable(MdkCloudMonitor.CloudMonitorEnvVar, "1");
                Assert.True(MdkCloudMonitor.AutoRegisterEnabled());
            }
            finally
            {
                Environment.SetEnvironmentVariable(MdkCloudMonitor.CloudMonitorEnvVar, previous);
            }
        }
    }

    [Fact]
    public void Testhost_does_not_auto_register()
    {
        lock (EnvLock)
        {
            var previous = Environment.GetEnvironmentVariable(MdkCloudMonitor.CloudMonitorEnvVar);
            try
            {
                Environment.SetEnvironmentVariable(MdkCloudMonitor.CloudMonitorEnvVar, null);
                using var rt = CreateRuntime();
                rt.Initialize();
                Assert.False(rt.TryGetDevice(MdkCloudMonitor.MysqlDeviceId, out _));
                Assert.DoesNotContain(rt.GetTaskSnapshots(), t => t.Name == MdkCloudMonitor.TaskName);
            }
            finally
            {
                Environment.SetEnvironmentVariable(MdkCloudMonitor.CloudMonitorEnvVar, previous);
            }
        }
    }

    [Fact]
    public void Env_one_registers_mysql_cloud_and_upload_task()
    {
        lock (EnvLock)
        {
            var previous = Environment.GetEnvironmentVariable(MdkCloudMonitor.CloudMonitorEnvVar);
            try
            {
                Environment.SetEnvironmentVariable(MdkCloudMonitor.CloudMonitorEnvVar, "1");
                using var rt = CreateRuntime();
                rt.Initialize();
                Assert.True(rt.TryGetDevice(MdkCloudMonitor.MysqlDeviceId, out var device));
                Assert.Equal("mysqldev", device.GetSnapshot().Type);
                Assert.Contains(rt.GetTaskSnapshots(), t => t.Name == MdkCloudMonitor.TaskName);
            }
            finally
            {
                Environment.SetEnvironmentVariable(MdkCloudMonitor.CloudMonitorEnvVar, previous);
            }
        }
    }

    [Fact]
    public void Existing_config_ids_are_not_duplicated()
    {
        lock (EnvLock)
        {
            var previous = Environment.GetEnvironmentVariable(MdkCloudMonitor.CloudMonitorEnvVar);
            try
            {
                Environment.SetEnvironmentVariable(MdkCloudMonitor.CloudMonitorEnvVar, "1");
                var dir = Path.Combine(Path.GetTempPath(), $"mdk-cloud-dup-{Guid.NewGuid():N}");
                Directory.CreateDirectory(dir);
                var setting = new MdkSetting
                {
                    ProjectName = "cloud-dup",
                    DatabasePath = Path.Combine(dir, "mdk.db"),
                    MonitoringPrefix = $"http://127.0.0.1:{GetFreeLoopbackPort()}/",
                    Devices =
                    [
                        new MdkSetting.DeviceConfig
                        {
                            Id = MdkCloudMonitor.MysqlDeviceId,
                            Name = "Existing Cloud",
                            Type = "mysqldev",
                            Enabled = true,
                            Parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                            {
                                ["host"] = "127.0.0.1",
                                ["port"] = "3306",
                            },
                        },
                    ],
                    Tasks =
                    [
                        new MdkSetting.TaskConfig
                        {
                            Name = MdkCloudMonitor.TaskName,
                            Type = MdkCloudMonitor.TaskType,
                            IntervalMs = 12_000,
                            Parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                            {
                                ["mysqlDeviceId"] = MdkCloudMonitor.MysqlDeviceId,
                            },
                        },
                    ],
                };
                using var rt = new MdkRuntime(setting, Path.Combine(dir, "setting.json"));
                rt.Initialize();
                Assert.True(rt.TryGetDevice(MdkCloudMonitor.MysqlDeviceId, out var device));
                Assert.Equal("Existing Cloud", device.Name);
                var snap = Assert.Single(rt.GetTaskSnapshots(), t => t.Name == MdkCloudMonitor.TaskName);
                Assert.Equal(12_000, snap.IntervalMs);
            }
            finally
            {
                Environment.SetEnvironmentVariable(MdkCloudMonitor.CloudMonitorEnvVar, previous);
            }
        }
    }

    [Fact]
    public async Task Start_kicks_cloud_upload_immediately()
    {
        string? previous;
        lock (EnvLock)
        {
            previous = Environment.GetEnvironmentVariable(MdkCloudMonitor.CloudMonitorEnvVar);
            Environment.SetEnvironmentVariable(MdkCloudMonitor.CloudMonitorEnvVar, "1");
        }

        try
        {
            var dir = Path.Combine(Path.GetTempPath(), $"mdk-cloud-kick-{Guid.NewGuid():N}");
            Directory.CreateDirectory(dir);
            var setting = new MdkSetting
            {
                ProjectName = "cloud-kick",
                DatabasePath = Path.Combine(dir, "mdk.db"),
                MonitoringPrefix = $"http://127.0.0.1:{GetFreeLoopbackPort()}/",
                Devices =
                [
                    new MdkSetting.DeviceConfig
                    {
                        Id = MdkCloudMonitor.MysqlDeviceId,
                        Name = MdkCloudMonitor.MysqlDeviceName,
                        Type = MdkCloudMonitor.MysqlDeviceType,
                        Enabled = true,
                        Parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                        {
                            ["host"] = "127.0.0.1",
                            ["port"] = "1",
                            ["connectTimeoutMs"] = "200",
                            ["commandTimeoutMs"] = "200",
                            ["autoConnect"] = "false",
                        },
                    },
                ],
            };
            using var rt = new MdkRuntime(setting, Path.Combine(dir, "setting.json"));
            rt.Initialize();
            rt.Start();
            try
            {
                var deadline = DateTime.UtcNow.AddSeconds(3);
                string? err = null;
                while (DateTime.UtcNow < deadline)
                {
                    err = rt.Vars.Get<string>("cloud.machine.lastError");
                    if (!string.IsNullOrWhiteSpace(err))
                    {
                        break;
                    }

                    await Task.Delay(50);
                }

                Assert.False(string.IsNullOrWhiteSpace(err));
                Assert.True(rt.TryGetDevice(MdkCloudMonitor.MysqlDeviceId, out var device));
                Assert.NotEqual(MDeviceState.Fault, device.State);
            }
            finally
            {
                await rt.StopAsync();
            }
        }
        finally
        {
            lock (EnvLock)
            {
                Environment.SetEnvironmentVariable(MdkCloudMonitor.CloudMonitorEnvVar, previous);
            }
        }
    }
}

[CollectionDefinition("CloudMonitorEnv", DisableParallelization = true)]
public sealed class CloudMonitorEnvCollection;
