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
        Assert.False(string.IsNullOrWhiteSpace(snap.Version));
        Assert.Equal(MdkProduct.Version, snap.Version);
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
    public void Gpio_attaches_all_non_vio_drivers_by_default_even_with_device_driverId()
    {
        var setting = new MdkSetting
        {
            Drivers =
            [
                new MdkSetting.DriverConfig { Id = "d1", Type = "sim", Enabled = true },
                new MdkSetting.DriverConfig { Id = "d2", Type = "sim", Enabled = true },
                new MdkSetting.DriverConfig { Id = "dv", Type = "vio", Enabled = true },
            ],
            Devices =
            [
                new MdkSetting.DeviceConfig
                {
                    Id = "g1",
                    Type = "gpio",
                    DriverId = "d1",
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
        Assert.True(rt.TryGetDevice("g1", out var raw));
        var gpio = Assert.IsType<GpioDevice>(raw);
        Assert.True(gpio.Drivers.ContainsKey("d1"));
        Assert.True(gpio.Drivers.ContainsKey("d2"));
        Assert.False(gpio.Drivers.ContainsKey("dv"));
    }

    [Fact]
    public void TryWriteDigitalOutput_blank_deviceId_uses_shared_gpio()
    {
        var setting = new MdkSetting
        {
            Drivers = [new MdkSetting.DriverConfig { Id = "d1", Type = "sim", Enabled = true }],
            Devices =
            [
                new MdkSetting.DeviceConfig
                {
                    Id = "gpio-main",
                    Type = "gpio",
                    Enabled = true,
                    Parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["out.lamp"] = "d1|do.gpo.bit.0|灯",
                    },
                },
            ],
        };

        using var rt = new MdkRuntime(setting);
        rt.Initialize();
        Assert.True(rt.TryWriteDigitalOutput("", "lamp", true, out var err), err);
        var on = rt.GetSnapshot().Devices["gpio-main"].GpioIoPoints!.Single(p => p.Alias == "lamp");
        Assert.Equal("true", on.Value, StringComparer.OrdinalIgnoreCase);

        Assert.True(rt.TryWriteDigitalOutput("  ", "lamp", false, out err), err);
        var off = rt.GetSnapshot().Devices["gpio-main"].GpioIoPoints!.Single(p => p.Alias == "lamp");
        Assert.Equal("false", off.Value, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void Gpio_bit_write_does_not_clobber_other_outputs()
    {
        var setting = new MdkSetting
        {
            Drivers = [new MdkSetting.DriverConfig { Id = "d1", Type = "sim", Enabled = true }],
            Devices =
            [
                new MdkSetting.DeviceConfig
                {
                    Id = "gpio-main",
                    Type = "gpio",
                    Enabled = true,
                    Parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["out.red"] = "d1|do.gpo.bit.0|红",
                        ["out.green"] = "d1|do.gpo.bit.1|绿",
                    },
                },
            ],
        };

        using var rt = new MdkRuntime(setting);
        rt.Initialize();
        Assert.True(rt.TryWriteDigitalOutput("gpio-main", "red", true, out var err), err);
        Assert.True(rt.TryWriteDigitalOutput("gpio-main", "green", true, out err), err);
        Assert.True(rt.TryWriteDigitalOutput("gpio-main", "red", false, out err), err);

        var points = rt.GetSnapshot().Devices["gpio-main"].GpioIoPoints!;
        Assert.Equal("false", points.Single(p => p.Alias == "red").Value, StringComparer.OrdinalIgnoreCase);
        Assert.Equal("true", points.Single(p => p.Alias == "green").Value, StringComparer.OrdinalIgnoreCase);
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
        Assert.Equal("p1.X", p1.PlatformAxes[0].AxisDeviceId);
        Assert.Equal("p1.Y", p1.PlatformAxes[1].AxisDeviceId);
        Assert.Equal("xy", p1.Type);
        Assert.NotNull(p1.PlatformAxes[0].AxisStatus);
        Assert.True(p1.PlatformAxes[0].AxisStatus!.Value.InPosition);
    }

    [Fact]
    public void Axis_snapshot_exposes_full_axis_status()
    {
        var setting = new MdkSetting
        {
            Drivers = [new MdkSetting.DriverConfig { Id = "d1", Type = "sim", Enabled = true }],
            Devices =
            [
                new MdkSetting.DeviceConfig
                {
                    Id = "ax1",
                    Name = "X",
                    Type = "axis",
                    DriverId = "d1",
                    Enabled = true,
                    Parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["axis"] = "0",
                    },
                },
            ],
        };

        using var rt = new MdkRuntime(setting);
        rt.Initialize();
        var snap = rt.GetSnapshot();
        Assert.True(snap.Devices.TryGetValue("ax1", out var ax));
        Assert.NotNull(ax.AxisStatus);
        Assert.True(ax.AxisStatus!.Value.InPosition);
        Assert.False(ax.AxisStatus.Value.Moving);
        Assert.False(ax.AxisStatus.Value.Alarm);
    }

    [Fact]
    public void Vio_device_binds_undirected_bit_keys()
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
                        ["vio.b1"] = "virtual|vio.b1",
                        ["vio.b2"] = "",
                    },
                },
            ],
        };

        using var rt = new MdkRuntime(setting);
        rt.Initialize();
        var snap = rt.GetSnapshot();
        Assert.True(snap.Devices.TryGetValue("vio1", out var vio));
        Assert.NotNull(vio.GpioIoPoints);
        Assert.Equal(2, vio.GpioIoPoints!.Count);
        Assert.Contains(vio.GpioIoPoints, p => p.Alias == "vio.b1" && p.Direction == "vio");
        Assert.Contains(vio.GpioIoPoints, p => p.Alias == "vio.b2" && p.Direction == "vio");
        Assert.Contains(vio.GpioIoPoints, p => p.Address == "vio.vio1.vio.b1");
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
        Assert.Equal("vio", vio.Type);
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
