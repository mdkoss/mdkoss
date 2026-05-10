using MDKOSS.Core;
using MDKOSS.Core.Drivers;

namespace MDKOSS.Tests;

public sealed class MTaskSchedulerTests
{
    [Fact]
    public async Task Start_stop_completes_without_hanging()
    {
        var vars = new MVarStore();
        var driver = new DrvSim();
        driver.Initialize(new MdkSetting.DriverConfig { Id = "d1", Type = "sim" });
        var task = new PollDriverTask("tick", 20, driver, vars);

        var scheduler = new MTaskScheduler();
        scheduler.Register(task);
        scheduler.Start();
        await Task.Delay(80);
        await scheduler.StopAsync();
        scheduler.Dispose();
        driver.Dispose();
    }
}
