using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text.Json;
using MDKOSS.Core;
using MDKOSS.Sample.DieBonder.Machine;
using MDKOSS.Extensions;
using MDKOSS.Pnp;

namespace MDKOSS.Tests.DieBonder;

/// <summary>
/// Verifies the die bonder sample registers its task / API / page and can start a bond cycle.
/// </summary>
public sealed class DieBonderExtensionTests
{
    public DieBonderExtensionTests()
    {
        _ = typeof(TrayDevice);
        TestPluginBootstrap.EnsureRegistered();
        MdkExtensionHost.Register(new DieBonderExtension());
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
    public void Registers_diebonder_extension()
    {
        Assert.Contains("sample-diebonder", MdkExtensionHost.RegisteredIds);
    }

    [Fact]
    public async Task Runtime_serves_bond_api_and_starts_cycle()
    {
        var db = Path.Combine(Path.GetTempPath(), $"mdk-diebonder-{Guid.NewGuid():N}.db");
        var port = FreePort();
        var setting = new MdkSetting
        {
            ProjectName = "diebonder-test",
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
                    Id = "gpio-machine",
                    Name = "IO",
                    Type = "gpio",
                    Enabled = true,
                    Parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["out.vacuum"] = "d1|do.gpo.bit.10|真空",
                        ["out.ejector"] = "d1|do.gpo.bit.11|顶针",
                    },
                },
                new MdkSetting.DeviceConfig
                {
                    Id = "tray-wafer",
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
                    Id = "tray-substrate",
                    Name = "基板",
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
                    Id = "head-bond",
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
                        ["gpioDeviceId"] = "gpio-machine",
                    },
                },
                new MdkSetting.TaskConfig
                {
                    Name = "bond-cycle",
                    Type = "bond",
                    DriverId = "d1",
                    IntervalMs = 40,
                    Parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["platformDeviceId"] = "head-bond",
                        ["gpioDeviceId"] = "gpio-machine",
                        ["sourceTrayDeviceId"] = "tray-wafer",
                        ["targetTrayDeviceId"] = "tray-substrate",
                        ["useEjector"] = "false",
                        ["useDispenser"] = "false",
                        ["checkSafetyDoor"] = "false",
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

            using (var start = await http.PostAsync($"http://127.0.0.1:{port}/api/bond/start", null))
            {
                start.EnsureSuccessStatusCode();
            }

            var deadline = DateTime.UtcNow.AddSeconds(8);
            string? phase = null;
            while (DateTime.UtcNow < deadline)
            {
                phase = rt.Vars.Get<string>("task.bond.phase");
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

            using (var status = await http.GetAsync($"http://127.0.0.1:{port}/api/bond/dashboard"))
            {
                status.EnsureSuccessStatusCode();
                var json = await status.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(json);
                Assert.True(doc.RootElement.GetProperty("success").GetBoolean());
            }

            using var page = await http.GetAsync($"http://127.0.0.1:{port}/indexDieBonder.html");
            page.EnsureSuccessStatusCode();
            var html = await page.Content.ReadAsStringAsync();
            Assert.Contains("贴片", html, StringComparison.Ordinal);

            await rt.StopAsync();
        }
        finally
        {
            try { File.Delete(db); } catch { /* ignore */ }
        }
    }
}
