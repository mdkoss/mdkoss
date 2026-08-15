using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text.Json;
using MDKOSS.Core;
using MDKOSS.Core.Drivers;
using MDKOSS.Extensions;
using MDKOSS.Sample.SampleExt;

namespace MDKOSS.Tests.Sample;

/// <summary>
/// Verifies SampleExt registers device / MotionTask / API / static page together.
/// </summary>
public sealed class SampleExtExtensionTests
{
    public SampleExtExtensionTests()
    {
        TestPluginBootstrap.EnsureRegistered();
        MdkExtensionHost.Register(new SampleExtExtension());
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

    [Fact]
    public void Registers_beacon_device_and_actions()
    {
        Assert.Contains("sample-ext", MdkExtensionHost.RegisteredIds);

        var cfg = new MdkSetting.DeviceConfig
        {
            Id = "sample-beacon",
            Name = "Beacon",
            Type = "samplebeacon",
            Enabled = true,
            Parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["label"] = "unit-test",
            },
        };

        var vars = new MVarStore();
        var drivers = new Dictionary<string, IDriver>(StringComparer.OrdinalIgnoreCase);
        Assert.True(
            DeviceExtensionRegistry.TryCreate("samplebeacon", cfg, cfg.Name, vars, drivers, out var device));
        Assert.IsType<SampleBeaconDevice>(device);
        var beacon = (SampleBeaconDevice)device!;
        Assert.Equal(1, beacon.Pulse("t"));
        Assert.Equal(1, beacon.PulseCount);

        Assert.True(DeviceActionRegistry.TryExecute(beacon, "status", null, out var action));
        Assert.True(action.Success);
    }

    [Fact]
    public async Task Runtime_serves_sampleext_api_and_motion_task()
    {
        var db = Path.Combine(Path.GetTempPath(), $"mdk-sampleext-{Guid.NewGuid():N}.db");
        var port = FreePort();
        var setting = new MdkSetting
        {
            ProjectName = "sample-ext-test",
            MonitoringPrefix = $"http://127.0.0.1:{port}/",
            DatabasePath = db,
            Drivers =
            [
                new MdkSetting.DriverConfig { Id = "d1", Type = "sim", Enabled = true },
            ],
            Devices =
            [
                new MdkSetting.DeviceConfig
                {
                    Id = "sample-beacon",
                    Name = "Beacon",
                    Type = "samplebeacon",
                    Enabled = true,
                    Parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["label"] = "api-test",
                    },
                },
            ],
            Platforms =
            [
                new MdkSetting.DeviceConfig
                {
                    Id = "plat",
                    Name = "Plat",
                    Type = "xy",
                    DriverId = "d1",
                    Enabled = true,
                },
            ],
            Tasks =
            [
                new MdkSetting.TaskConfig
                {
                    Name = "sample-motion-demo",
                    Type = "samplemotion",
                    DriverId = "d1",
                    IntervalMs = 50,
                    Parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["platformDeviceId"] = "plat",
                        ["axisLetter"] = "X",
                        ["beaconDeviceId"] = "sample-beacon",
                        ["jogTicks"] = "1",
                    },
                },
            ],
        };

        try
        {
            using var rt = new MdkRuntime(setting);
            rt.Initialize();
            rt.Start();
            Assert.True(rt.TryGetDevice("sample-beacon", out var beaconDev));
            Assert.IsType<SampleBeaconDevice>(beaconDev);

            using var handler = new HttpClientHandler { UseProxy = false };
            using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(5) };

            using (var pulse = await http.PostAsync($"http://127.0.0.1:{port}/api/sampleext/pulse", null))
            {
                pulse.EnsureSuccessStatusCode();
            }

            using (var status = await http.GetAsync($"http://127.0.0.1:{port}/api/sampleext/status"))
            {
                status.EnsureSuccessStatusCode();
                var json = await status.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(json);
                Assert.True(doc.RootElement.GetProperty("success").GetBoolean());
                Assert.Equal(1, doc.RootElement.GetProperty("beacon").GetProperty("pulseCount").GetInt32());
            }

            using (var start = await http.PostAsync($"http://127.0.0.1:{port}/api/sampleext/motionstart", null))
            {
                start.EnsureSuccessStatusCode();
            }

            var deadline = DateTime.UtcNow.AddSeconds(5);
            string? phase = null;
            while (DateTime.UtcNow < deadline)
            {
                phase = rt.Vars.Get<string>("sample.motion.phase");
                if (string.Equals(phase, "Done", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(phase, "Fault", StringComparison.OrdinalIgnoreCase))
                {
                    break;
                }

                await Task.Delay(50);
            }

            Assert.False(string.IsNullOrWhiteSpace(phase));
            Assert.NotEqual("Fault", phase, StringComparer.OrdinalIgnoreCase);

            using var page = await http.GetAsync($"http://127.0.0.1:{port}/demo_sample_ext.html");
            page.EnsureSuccessStatusCode();
            var html = await page.Content.ReadAsStringAsync();
            Assert.Contains("Sample 扩展示例", html, StringComparison.Ordinal);

            await rt.StopAsync();
        }
        finally
        {
            try { File.Delete(db); } catch { /* ignore */ }
        }
    }
}
