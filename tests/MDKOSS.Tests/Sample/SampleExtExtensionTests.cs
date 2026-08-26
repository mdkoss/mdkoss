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
    public async Task Custom_startPage_replaces_root_and_index_html()
    {
        var port = FreePort();
        var setting = new MdkSetting
        {
            ProjectName = "start-page-test",
            StartPage = "demo_sample_ext.html",
            MonitoringPrefix = $"http://127.0.0.1:{port}/",
            Drivers =
            [
                new MdkSetting.DriverConfig { Id = "d1", Type = "sim", Enabled = true },
            ],
        };

        using var rt = new MdkRuntime(setting);
        rt.Initialize();
        rt.Start();

        try
        {
            using var handler = new HttpClientHandler { UseProxy = false };
            using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(5) };

            foreach (var path in new[] { "/", "/index.html" })
            {
                using var page = await http.GetAsync($"http://127.0.0.1:{port}{path}");
                page.EnsureSuccessStatusCode();
                var html = await page.Content.ReadAsStringAsync();
                Assert.Contains("Sample 扩展示例", html, StringComparison.Ordinal);
                Assert.DoesNotContain("MDKOSS 主界面", html, StringComparison.Ordinal);
            }
        }
        finally
        {
            await rt.StopAsync();
        }
    }

    [Fact]
    public async Task Runtime_serves_sampleext_api_and_motion_task()
    {
        var db = Path.Combine(Path.GetTempPath(), $"mdk-sampleext-{Guid.NewGuid():N}.db");
        var port = FreePort();
        var setting = new MdkSetting
        {
            ProjectName = "sample-ext-test",
            StartPage = "demo_sample_ext.html",
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
            Assert.Contains("publish-dingtalk", html, StringComparison.Ordinal);

            using (var shot = await http.GetAsync($"http://127.0.0.1:{port}/api/sampleext/run-screenshot.png"))
            {
                shot.EnsureSuccessStatusCode();
                Assert.Equal("image/png", shot.Content.Headers.ContentType?.MediaType);
                var png = await shot.Content.ReadAsByteArrayAsync();
                Assert.True(SampleRunScreenshot.LooksLikePng(png));
                Assert.True(png.Length > 500);
            }

            var fakePort = FreePort();
            using var fakeWebhook = new HttpListener();
            fakeWebhook.Prefixes.Add($"http://127.0.0.1:{fakePort}/hook/");
            fakeWebhook.Start();
            var webhookReceived = Task.Run(async () =>
            {
                var ctx = await fakeWebhook.GetContextAsync();
                using var reader = new StreamReader(ctx.Request.InputStream);
                var body = await reader.ReadToEndAsync();
                ctx.Response.ContentType = "application/json";
                var bytes = System.Text.Encoding.UTF8.GetBytes("""{"errcode":0,"errmsg":"ok"}""");
                ctx.Response.OutputStream.Write(bytes);
                ctx.Response.Close();
                return body;
            });

            var publishBody = JsonSerializer.Serialize(new
            {
                webhook = $"http://127.0.0.1:{fakePort}/hook/",
            });
            using (var publish = await http.PostAsync(
                       $"http://127.0.0.1:{port}/api/sampleext/publish-dingtalk",
                       new StringContent(publishBody, System.Text.Encoding.UTF8, "application/json")))
            {
                publish.EnsureSuccessStatusCode();
                var json = await publish.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(json);
                Assert.True(doc.RootElement.GetProperty("success").GetBoolean());
                Assert.True(doc.RootElement.GetProperty("pngBytes").GetInt32() > 500);
            }

            var webhookBody = await webhookReceived.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Contains("msgtype", webhookBody, StringComparison.Ordinal);
            Assert.Contains("MDKOSS Sample", webhookBody, StringComparison.Ordinal);
            fakeWebhook.Stop();

            await rt.StopAsync();
        }
        finally
        {
            try { File.Delete(db); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void Run_screenshot_renders_valid_png_from_snapshot()
    {
        var snap = new RuntimeSnapshot(
            ProjectName: "shot-test",
            Version: "1.1.0",
            IsRunning: true,
            Drivers: new Dictionary<string, DriverSnapshot>(StringComparer.OrdinalIgnoreCase)
            {
                ["d1"] = new DriverSnapshot("sim", true),
            },
            Devices: new Dictionary<string, DeviceSnapshot>(StringComparer.OrdinalIgnoreCase)
            {
                ["dev1"] = new DeviceSnapshot("dev1", "Dev", "gpio", "Ready", "sim", true),
            },
            Vars: new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase));

        var png = SampleRunScreenshot.RenderPng(snap);
        Assert.True(SampleRunScreenshot.LooksLikePng(png));
        var md = SampleRunScreenshot.BuildMarkdown(snap);
        Assert.Contains("shot-test", md, StringComparison.Ordinal);
        Assert.Contains("YES", md, StringComparison.Ordinal);
    }
}
