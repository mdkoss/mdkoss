using System.Text.Json;
using MDKOSS.Core;

namespace MDKOSS.Tests;

public sealed class MdkRuntimeIntegrationTests
{
    [Fact]
    public void Initialize_start_snapshot_stop_lifecycle()
    {
        var setting = new MdkSetting
        {
            ProjectName = "unit-test",
            Drivers =
            [
                new MdkSetting.DriverConfig { Id = "d1", Type = "sim", Enabled = true },
            ],
            Devices =
            [
                new MdkSetting.DeviceConfig
                {
                    Id = "g1",
                    Name = "GPIO",
                    Type = "gpio",
                    Enabled = true,
                    Parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["in.a"] = "d1:X0",
                    },
                },
            ],
            Tasks =
            [
                new MdkSetting.TaskConfig
                {
                    Name = "poll-d1",
                    Type = "pollDriver",
                    DriverId = "d1",
                    IntervalMs = 50,
                },
            ],
        };

        using var rt = new MdkRuntime(setting);
        rt.Initialize();
        var snap = rt.GetSnapshot();
        Assert.Equal("unit-test", snap.ProjectName);
        Assert.False(snap.IsRunning);
        Assert.True(snap.Drivers.ContainsKey("d1"));
        Assert.True(snap.Devices.ContainsKey("g1"));
        Assert.True(snap.Drivers["d1"].IsConnected);
        var g1 = snap.Devices["g1"];
        Assert.NotNull(g1.GpioIoPoints);
        Assert.Single(g1.GpioIoPoints!);
        Assert.Equal("a", g1.GpioIoPoints![0].Alias);
        Assert.Equal("in", g1.GpioIoPoints[0].Direction);

        var json = JsonSerializer.Serialize(snap, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        });
        using var doc = JsonDocument.Parse(json);
        Assert.True(doc.RootElement.TryGetProperty("devices", out var devEl));
        Assert.Equal(JsonValueKind.Object, devEl.ValueKind);
        Assert.True(devEl.TryGetProperty("g1", out var g1El));
        Assert.True(g1El.TryGetProperty("gpioIoPoints", out var ioEl));
        Assert.Equal(JsonValueKind.Array, ioEl.ValueKind);
    }

    [Fact]
    public void Duplicate_task_name_throws_mdk_exception()
    {
        var setting = new MdkSetting
        {
            Drivers = [new MdkSetting.DriverConfig { Id = "d1", Type = "sim", Enabled = true }],
            Tasks =
            [
                new MdkSetting.TaskConfig { Name = "same", Type = "pollDriver", DriverId = "d1" },
                new MdkSetting.TaskConfig { Name = "same", Type = "pollDriver", DriverId = "d1" },
            ],
        };

        using var rt = new MdkRuntime(setting);
        var ex = Assert.Throws<MdkException>(() => rt.Initialize());
        Assert.Equal(MdkErrorCode.DuplicateTaskName, ex.Code);
    }

    [Fact]
    public void Gpio_driver_ids_scope_rejects_binding_outside_scope()
    {
        var setting = new MdkSetting
        {
            Drivers =
            [
                new MdkSetting.DriverConfig { Id = "d1", Type = "sim", Enabled = true },
                new MdkSetting.DriverConfig { Id = "d2", Type = "sim", Enabled = true },
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
                        ["driverIds"] = "d1",
                        ["in.a"] = "d2:X0",
                    },
                },
            ],
        };

        using var rt = new MdkRuntime(setting);
        var ex = Assert.Throws<MdkException>(() => rt.Initialize());
        Assert.Equal(MdkErrorCode.GpioDriverScopeInvalid, ex.Code);
    }

    [Fact]
    public async Task Gpio_multi_driver_start_succeeds_when_all_referenced_drivers_online()
    {
        var setting = new MdkSetting
        {
            Drivers =
            [
                new MdkSetting.DriverConfig { Id = "d1", Type = "sim", Enabled = true },
                new MdkSetting.DriverConfig { Id = "d2", Type = "sim", Enabled = true },
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
                        ["in.a"] = "d1:X0",
                        ["in.b"] = "d2:X1",
                    },
                },
            ],
        };

        using var rt = new MdkRuntime(setting);
        rt.Initialize();
        rt.Start();
        Assert.True(rt.GetSnapshot().Devices["g1"].DriverConnected);
        await rt.StopAsync();
    }
}
