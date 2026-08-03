using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using MDKOSS.Core;

namespace MDKOSS.Tests.Core;

public sealed class MdkRuntimeIntegrationTests
{
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

    [Fact]
    public void Initialize_start_snapshot_stop_lifecycle()
    {
        var setting = new MdkSetting
        {
            ProjectName = "unit-test",
            MonitoringPrefix = $"http://127.0.0.1:{GetFreeLoopbackPort()}/",
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
            MonitoringPrefix = $"http://127.0.0.1:{GetFreeLoopbackPort()}/",
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

    [Fact]
    public void Platform_xy_with_shared_driver_exposes_axis_rows_in_snapshot()
    {
        var setting = new MdkSetting
        {
            Drivers = [new MdkSetting.DriverConfig { Id = "d1", Type = "sim", Enabled = true }],
            Devices =
            [
                new MdkSetting.DeviceConfig
                {
                    Id = "p1",
                    Name = "Table",
                    Type = "xy",
                    DriverId = "d1",
                    Enabled = true,
                },
            ],
        };

        using var rt = new MdkRuntime(setting);
        rt.Initialize();
        var snap = rt.GetSnapshot();
        Assert.True(snap.Devices.TryGetValue("p1", out var p1));
        Assert.Equal("platform-xy", p1.DriverType);
        Assert.NotNull(p1.PlatformAxes);
        Assert.Equal(2, p1.PlatformAxes!.Count);
        Assert.Equal("X", p1.PlatformAxes[0].AxisLetter);
        Assert.Equal("Y", p1.PlatformAxes[1].AxisLetter);
        Assert.All(p1.PlatformAxes, row => Assert.Equal("d1", row.DriverId));
    }

    [Fact]
    public void Vio_device_binds_virtual_addresses_on_sim_driver()
    {
        var setting = new MdkSetting
        {
            Drivers = [new MdkSetting.DriverConfig { Id = "d1", Type = "sim", Enabled = true }],
            Devices =
            [
                new MdkSetting.DeviceConfig
                {
                    Id = "vio1",
                    Name = "Virtual IO",
                    Type = "vio",
                    DriverId = "d1",
                    Enabled = true,
                    Parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["in.a"] = "virtual",
                        ["out.b"] = "",
                    },
                },
            ],
        };

        using var rt = new MdkRuntime(setting);
        rt.Initialize();
        var snap = rt.GetSnapshot();
        Assert.True(snap.Devices.TryGetValue("vio1", out var vio));
        Assert.Equal("Vio", vio.Type);
        Assert.Equal("vio", vio.DriverType);
        Assert.NotNull(vio.GpioIoPoints);
        Assert.Equal(2, vio.GpioIoPoints!.Count);
        Assert.Contains(vio.GpioIoPoints, p => p.Alias == "a" && p.Direction == "in");
        Assert.Contains(vio.GpioIoPoints, p => p.Alias == "b" && p.Direction == "out");
    }

    [Fact]
    public void Vio_rejects_physical_gpio_route_in_parameters()
    {
        var setting = new MdkSetting
        {
            Drivers = [new MdkSetting.DriverConfig { Id = "d1", Type = "sim", Enabled = true }],
            Devices =
            [
                new MdkSetting.DeviceConfig
                {
                    Id = "vio1",
                    Type = "vio",
                    DriverId = "d1",
                    Enabled = true,
                    Parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["in.a"] = "d1:X0",
                    },
                },
            ],
        };

        using var rt = new MdkRuntime(setting);
        var ex = Assert.Throws<MdkException>(() => rt.Initialize());
        Assert.Equal(MdkErrorCode.VioBindingInvalid, ex.Code);
    }

    [Fact]
    public void Platform_invalid_kind_parameter_throws_mdk_exception()
    {
        var setting = new MdkSetting
        {
            Drivers = [new MdkSetting.DriverConfig { Id = "d1", Type = "sim", Enabled = true }],
            Devices =
            [
                new MdkSetting.DeviceConfig
                {
                    Id = "p1",
                    Type = "platform",
                    DriverId = "d1",
                    Enabled = true,
                    Parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["kind"] = "not-a-kind",
                    },
                },
            ],
        };

        using var rt = new MdkRuntime(setting);
        var ex = Assert.Throws<MdkException>(() => rt.Initialize());
        Assert.Equal(MdkErrorCode.PlatformConfigurationInvalid, ex.Code);
    }
}
