using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text.Json;
using MDKOSS.Core;
using MDKOSS.Sample.Dispenser.Machine;
using MDKOSS.Extensions;

namespace MDKOSS.Tests.Dispenser;

/// <summary>
/// Verifies the 3-axis dispenser sample registers its task / API / page and can finish a grid.
/// </summary>
public sealed class DispenserExtensionTests
{
    public DispenserExtensionTests()
    {
        TestPluginBootstrap.EnsureRegistered();
        MdkExtensionHost.Register(new DispenserExtension());
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
    public void Registers_dispenser_extension()
    {
        Assert.Contains("sample-dispenser", MdkExtensionHost.RegisteredIds);
    }

    [Fact]
    public async Task Runtime_serves_dispense_api_and_completes_grid()
    {
        var db = Path.Combine(Path.GetTempPath(), $"mdk-dispenser-{Guid.NewGuid():N}.db");
        var port = FreePort();
        var setting = new MdkSetting
        {
            ProjectName = "dispenser-test",
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
                        ["out.valve"] = "d1|do.gpo.bit.10|点胶阀",
                    },
                },
            ],
            Platforms =
            [
                new MdkSetting.DeviceConfig
                {
                    Id = "head-dispense",
                    Name = "XYZ",
                    Type = "xyz",
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
                    Name = "dispense-cycle",
                    Type = "dispense",
                    DriverId = "d1",
                    IntervalMs = 40,
                    Parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["platformDeviceId"] = "head-dispense",
                        ["gpioDeviceId"] = "gpio-machine",
                        ["valveAlias"] = "valve",
                        ["rows"] = "1",
                        ["cols"] = "2",
                        ["dwellTicks"] = "1",
                    },
                },
            ],
            Vars = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["task.dispense.workpiecePresent"] = true,
                ["dispense.rows"] = 1,
                ["dispense.cols"] = 2,
                ["dispense.dwellTicks"] = 1,
            },
        };

        try
        {
            using var rt = new MdkRuntime(setting);
            rt.Initialize();
            rt.Start();

            using var handler = new HttpClientHandler { UseProxy = false };
            using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(5) };

            using (var start = await http.PostAsync($"http://127.0.0.1:{port}/api/dispense/start", null))
            {
                start.EnsureSuccessStatusCode();
            }

            var deadline = DateTime.UtcNow.AddSeconds(8);
            string? phase = null;
            while (DateTime.UtcNow < deadline)
            {
                phase = rt.Vars.Get<string>("task.dispense.phase");
                if (string.Equals(phase, "Done", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(phase, "Fault", StringComparison.OrdinalIgnoreCase))
                {
                    break;
                }

                await Task.Delay(40);
            }

            Assert.Equal("Done", phase, StringComparer.OrdinalIgnoreCase);
            Assert.Equal(2, Convert.ToInt32(rt.Vars.Get<object>("task.dispense.okCount")));

            using (var status = await http.GetAsync($"http://127.0.0.1:{port}/api/dispense/dashboard"))
            {
                status.EnsureSuccessStatusCode();
                var json = await status.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(json);
                Assert.True(doc.RootElement.GetProperty("success").GetBoolean());
                Assert.Equal("Done", doc.RootElement.GetProperty("phase").GetString(), StringComparer.OrdinalIgnoreCase);
            }

            using var page = await http.GetAsync($"http://127.0.0.1:{port}/indexDispenser.html");
            page.EnsureSuccessStatusCode();
            var html = await page.Content.ReadAsStringAsync();
            Assert.Contains("点胶", html, StringComparison.Ordinal);

            await rt.StopAsync();
        }
        finally
        {
            try { File.Delete(db); } catch { /* ignore */ }
        }
    }
}
