using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text.Json;
using MDKOSS.Core;
using MDKOSS.Extensions;
using MDKOSS.Pnp;

namespace MDKOSS.Tests.Pnp;

/// <summary>
/// Verifies the PNP sample registers tray / cycle / API and can start a pick-place cycle.
/// </summary>
public sealed class PnpExtensionTests
{
    public PnpExtensionTests()
    {
        _ = typeof(TrayDevice);
        TestPluginBootstrap.EnsureRegistered();
        MdkExtensionHost.Register(new PnpExtension());
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
    public void Registers_pnp_extension()
    {
        Assert.Contains("pnp", MdkExtensionHost.RegisteredIds);
    }

    [Fact]
    public async Task Runtime_serves_pnp_api_and_starts_cycle()
    {
        var db = Path.Combine(Path.GetTempPath(), $"mdk-pnp-{Guid.NewGuid():N}.db");
        var port = FreePort();
        var setting = new MdkSetting
        {
            ProjectName = "pnp-test",
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
                    Id = "gpio-pnp",
                    Name = "IO",
                    Type = "gpio",
                    Enabled = true,
                    Parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["out.vacuum"] = "d1|do.gpo.bit.10|真空",
                    },
                },
                new MdkSetting.DeviceConfig
                {
                    Id = "tray-src",
                    Name = "源盘",
                    Type = "tray",
                    Enabled = true,
                    Parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["rows"] = "1",
                        ["cols"] = "2",
                        ["originX"] = "0",
                        ["originY"] = "0",
                        ["pitchX"] = "1",
                        ["pitchY"] = "1",
                        ["pickZ"] = "-1",
                        ["safeZ"] = "0",
                        ["startIndex"] = "0",
                    },
                },
                new MdkSetting.DeviceConfig
                {
                    Id = "tray-tgt",
                    Name = "目标盘",
                    Type = "tray",
                    Enabled = true,
                    Parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["rows"] = "1",
                        ["cols"] = "2",
                        ["originX"] = "10",
                        ["originY"] = "0",
                        ["pitchX"] = "1",
                        ["pitchY"] = "1",
                        ["pickZ"] = "-1",
                        ["safeZ"] = "0",
                        ["startIndex"] = "0",
                    },
                },
            ],
            Platforms =
            [
                new MdkSetting.DeviceConfig
                {
                    Id = "robot",
                    Name = "XYZU",
                    Type = "xyzu",
                    DriverId = "d1",
                    Enabled = true,
                },
            ],
            Tasks =
            [
                new MdkSetting.TaskConfig
                {
                    Name = "task-operation",
                    Type = "operation",
                    IntervalMs = 50,
                    Parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["gpioDeviceId"] = "gpio-pnp",
                    },
                },
                new MdkSetting.TaskConfig
                {
                    Name = "pnp-cycle",
                    Type = "pnp",
                    DriverId = "d1",
                    IntervalMs = 40,
                    Parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["platformDeviceId"] = "robot",
                        ["gpioDeviceId"] = "gpio-pnp",
                        ["sourceTrayDeviceId"] = "tray-src",
                        ["targetTrayDeviceId"] = "tray-tgt",
                        ["dwellTicks"] = "1",
                    },
                },
            ],
            Vars = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["task.pnp.srcTrayPresent"] = true,
                ["task.pnp.tgtTrayPresent"] = true,
            },
        };

        try
        {
            using var rt = new MdkRuntime(setting);
            rt.Initialize();
            rt.Start();

            using var handler = new HttpClientHandler { UseProxy = false };
            using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(5) };

            using (var start = await http.PostAsync($"http://127.0.0.1:{port}/api/pnp/start", null))
            {
                start.EnsureSuccessStatusCode();
            }

            var deadline = DateTime.UtcNow.AddSeconds(8);
            string? phase = null;
            while (DateTime.UtcNow < deadline)
            {
                phase = rt.Vars.Get<string>("task.pnp.phase");
                if (!string.IsNullOrWhiteSpace(phase)
                    && !string.Equals(phase, "Idle", StringComparison.OrdinalIgnoreCase))
                {
                    break;
                }

                await Task.Delay(40);
            }

            Assert.False(string.IsNullOrWhiteSpace(phase));
            Assert.NotEqual("Idle", phase, StringComparer.OrdinalIgnoreCase);
            Assert.NotEqual("Fault", phase, StringComparer.OrdinalIgnoreCase);

            using (var status = await http.GetAsync($"http://127.0.0.1:{port}/api/pnp/dashboard"))
            {
                status.EnsureSuccessStatusCode();
                var json = await status.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(json);
                Assert.True(doc.RootElement.GetProperty("success").GetBoolean());
            }

            using var page = await http.GetAsync($"http://127.0.0.1:{port}/indexPnp.html");
            page.EnsureSuccessStatusCode();
            var html = await page.Content.ReadAsStringAsync();
            Assert.Contains("PNP", html, StringComparison.OrdinalIgnoreCase);

            await rt.StopAsync();
        }
        finally
        {
            try { File.Delete(db); } catch { /* ignore */ }
        }
    }
}
