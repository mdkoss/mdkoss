using System.Net;
using System.Net.Sockets;
using MDKOSS.Core;
using MDKOSS.Core.Drivers;
using MDKOSS.Tasks;

namespace MDKOSS.Tests.Core.Tasks;

public sealed class TaskMachineTaskTests
{
    [Fact]
    public async Task Commands_follow_idle_running_stopped_idle()
    {
        var vars = new MVarStore();
        var task = new TaskMachineTask(vars, gpioDevice: null, intervalMs: 10);

        Assert.Equal(TaskMachineTask.States.Idle, vars.Get<string>("machine.state"));
        Assert.False(vars.Get<bool>("machine.running"));

        vars.Set("machine.command", "start");
        await task.ExecuteOnceAsync(CancellationToken.None);
        Assert.Equal(TaskMachineTask.States.Running, vars.Get<string>("machine.state"));
        Assert.True(vars.Get<bool>("machine.running"));
        Assert.Equal("running", vars.Get<string>("task.operation.state"));

        vars.Set("machine.command", "stop");
        await task.ExecuteOnceAsync(CancellationToken.None);
        Assert.Equal(TaskMachineTask.States.Stopped, vars.Get<string>("machine.state"));
        Assert.False(vars.Get<bool>("machine.running"));

        vars.Set("machine.command", "start");
        await task.ExecuteOnceAsync(CancellationToken.None);
        Assert.Equal(TaskMachineTask.States.Stopped, vars.Get<string>("machine.state"));
        Assert.Contains("reset required", vars.Get<string>("machine.message"), StringComparison.OrdinalIgnoreCase);

        vars.Set("machine.command", "reset");
        await task.ExecuteOnceAsync(CancellationToken.None);
        Assert.Equal(TaskMachineTask.States.Idle, vars.Get<string>("machine.state"));
    }

    [Fact]
    public async Task Pause_holds_and_start_resumes()
    {
        var vars = new MVarStore();
        var task = new TaskMachineTask(vars, gpioDevice: null, intervalMs: 10);
        vars.Set("machine.command", "start");
        await task.ExecuteOnceAsync(CancellationToken.None);

        vars.Set("machine.command", "pause");
        await task.ExecuteOnceAsync(CancellationToken.None);
        Assert.Equal(TaskMachineTask.States.Paused, vars.Get<string>("machine.state"));
        Assert.True(vars.Get<bool>("machine.paused"));
        Assert.False(vars.Get<bool>("machine.running"));

        vars.Set("machine.command", "start");
        await task.ExecuteOnceAsync(CancellationToken.None);
        Assert.Equal(TaskMachineTask.States.Running, vars.Get<string>("machine.state"));
        Assert.Contains("resumed", vars.Get<string>("machine.message"), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Reset_while_running_is_rejected()
    {
        var vars = new MVarStore();
        var task = new TaskMachineTask(vars, gpioDevice: null, intervalMs: 10);
        vars.Set("machine.command", "start");
        await task.ExecuteOnceAsync(CancellationToken.None);

        vars.Set("machine.command", "reset");
        await task.ExecuteOnceAsync(CancellationToken.None);
        Assert.Equal(TaskMachineTask.States.Running, vars.Get<string>("machine.state"));
        Assert.Contains("stop first", vars.Get<string>("machine.message"), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Gpio_rising_edges_change_machine_state()
    {
        var vars = new MVarStore();
        var driver = DriverFactory.Create("sim");
        driver.Initialize(new MdkSetting.DriverConfig { Id = "d1", Type = "sim" });
        var gpio = new GpioDevice("g1", "gpio", new Dictionary<string, IDriver>(StringComparer.OrdinalIgnoreCase)
        {
            ["d1"] = driver,
        }, vars);
        gpio.RegisterInput("startButton", "d1", "di.gpi.bit.0");
        gpio.RegisterInput("stopButton", "d1", "di.gpi.bit.1");
        gpio.RegisterInput("resetButton", "d1", "di.gpi.bit.2");
        gpio.Start();

        var task = new TaskMachineTask(vars, gpio, intervalMs: 10);
        await task.ExecuteOnceAsync(CancellationToken.None);
        Assert.Equal(TaskMachineTask.States.Idle, vars.Get<string>("machine.state"));

        Assert.True(driver.Write("di.gpi.bit.0", true));
        await task.ExecuteOnceAsync(CancellationToken.None);
        Assert.Equal(TaskMachineTask.States.Running, vars.Get<string>("machine.state"));
        Assert.Equal("start", vars.Get<string>("task.operation.command"));
        Assert.True(vars.Get<bool>("machine.button.start"));

        Assert.True(driver.Write("di.gpi.bit.1", true));
        await task.ExecuteOnceAsync(CancellationToken.None);
        Assert.Equal(TaskMachineTask.States.Stopped, vars.Get<string>("machine.state"));

        Assert.True(driver.Write("di.gpi.bit.2", true));
        await task.ExecuteOnceAsync(CancellationToken.None);
        Assert.Equal(TaskMachineTask.States.Idle, vars.Get<string>("machine.state"));

        driver.Dispose();
    }

    [Fact]
    public void Runtime_auto_registers_system_machine_task()
    {
        var db = Path.Combine(Path.GetTempPath(), $"mdk-machine-task-{Guid.NewGuid():N}.db");
        var setting = new MdkSetting
        {
            ProjectName = "machine-task",
            MonitoringPrefix = $"http://127.0.0.1:{FreePort()}/",
            DatabasePath = db,
            Drivers =
            [
                new MdkSetting.DriverConfig { Id = "d1", Type = "sim", Enabled = true },
            ],
            Devices =
            [
                new MdkSetting.DeviceConfig
                {
                    Id = "gpio-machine",
                    Name = "GPIO",
                    Type = "gpio",
                    Enabled = true,
                    Parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["in.startButton"] = "d1|di.gpi.bit.0|启动",
                        ["in.stopButton"] = "d1|di.gpi.bit.1|停止",
                        ["in.resetButton"] = "d1|di.gpi.bit.2|复位",
                    },
                },
            ],
        };

        using var rt = new MdkRuntime(setting);
        rt.Initialize();
        Assert.Contains(rt.GetTaskSnapshots(), t => t.Name == TaskMachineTask.TaskName);
        Assert.Equal(TaskMachineTask.States.Idle, rt.Vars.Get<string>("machine.state"));
    }

    [Fact]
    public void Config_machine_task_does_not_duplicate()
    {
        var db = Path.Combine(Path.GetTempPath(), $"mdk-machine-dup-{Guid.NewGuid():N}.db");
        var setting = new MdkSetting
        {
            MonitoringPrefix = $"http://127.0.0.1:{FreePort()}/",
            DatabasePath = db,
            Drivers = [new MdkSetting.DriverConfig { Id = "d1", Type = "sim", Enabled = true }],
            Tasks =
            [
                new MdkSetting.TaskConfig
                {
                    Name = TaskMachineTask.TaskName,
                    Type = "machine",
                    IntervalMs = 40,
                    Parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["gpioDeviceId"] = "g1",
                    },
                },
            ],
            Devices =
            [
                new MdkSetting.DeviceConfig
                {
                    Id = "g1",
                    Type = "gpio",
                    Enabled = true,
                    Parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["in.startButton"] = "d1|di.gpi.bit.0|启动",
                    },
                },
            ],
        };

        using var rt = new MdkRuntime(setting);
        rt.Initialize();
        Assert.Single(rt.GetTaskSnapshots(), t => t.Name == TaskMachineTask.TaskName);
        Assert.Equal(40, rt.GetTaskSnapshots().First(t => t.Name == TaskMachineTask.TaskName).IntervalMs);
    }

    private static int FreePort()
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
}
