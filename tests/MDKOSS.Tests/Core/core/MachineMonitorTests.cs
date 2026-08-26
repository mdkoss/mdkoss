using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using MDKOSS.Core;
using MDKOSS.Core.Data;

namespace MDKOSS.Tests.Core;

public sealed class MachineMonitorTests
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
    public void ResolveId_prefers_machineId_then_host_project()
    {
        Assert.Equal("fixed-id", MachineMonitor.ResolveId(new MdkSetting { MachineId = "fixed-id", ProjectName = "P" }, "HOST"));
        Assert.Equal("HOST:Demo", MachineMonitor.ResolveId(new MdkSetting { ProjectName = "Demo" }, "HOST"));
        Assert.Equal("DieBonder", MachineMonitor.ResolveType(new MdkSetting { MachineType = "DieBonder" }));
        Assert.Equal("", MachineMonitor.ResolveType(new MdkSetting { ProjectName = "P" }));
    }

    [Fact]
    public void RedactParameters_masks_secrets()
    {
        var redacted = MachineMonitor.RedactParameters(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["host"] = "db.local",
            ["password"] = "s3cret",
            ["apiToken"] = "tok",
        });
        Assert.Equal("db.local", redacted["host"]);
        Assert.Equal("***", redacted["password"]);
        Assert.Equal("***", redacted["apiToken"]);
    }

    [Fact]
    public void GetMachineMonitor_includes_name_version_type_vars_recipe_orders()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"mdk-machine-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        var setting = new MdkSetting
        {
            ProjectName = "unit-monitor",
            MachineId = "test-machine-1",
            MachineType = "DieBonder",
            CycleMs = 20,
            DatabasePath = Path.Combine(dir, "mdk.db"),
            MonitoringPrefix = $"http://127.0.0.1:{GetFreeLoopbackPort()}/",
            RecipeVarKeys = ["speed"],
            Vars = new Dictionary<string, object?> { ["speed"] = 12 },
            Recipes =
            [
                new MdkSetting.RecipeConfig
                {
                    Id = "r1",
                    Name = "R1",
                    Vars = new Dictionary<string, object?> { ["speed"] = 12 },
                },
            ],
            ActiveRecipeId = "r1",
            Devices =
            [
                new MdkSetting.DeviceConfig
                {
                    Id = "mysql-cloud",
                    Name = "Cloud",
                    Type = "mysqldev",
                    Enabled = true,
                    Parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["host"] = "127.0.0.1",
                        ["password"] = "s3cret",
                    },
                },
            ],
        };

        using var rt = new MdkRuntime(setting, Path.Combine(dir, "setting.json"));
        rt.Initialize();
        Assert.True(rt.TryUpsertOrder(new ProductionOrderRecord
        {
            Id = "ord-1",
            Product = "P",
            Qty = 2,
            Status = "pending",
        }, out _));

        var rec = rt.GetMachineMonitor();
        Assert.Equal("test-machine-1", rec.Id);
        Assert.Equal("unit-monitor", rec.Name);
        Assert.Equal("DieBonder", rec.MachineType);
        Assert.Equal(MdkProduct.Version, rec.Version);
        Assert.False(rec.IsRunning);
        Assert.False(string.IsNullOrWhiteSpace(rec.MachineState));
        Assert.NotNull(rec.Setting);
        Assert.True(rec.Vars.ContainsKey("machine.state"));
        Assert.NotNull(rec.Recipe);
        Assert.NotNull(rec.Orders);
        Assert.NotNull(rec.Drivers);
        Assert.NotNull(rec.Devices);
        Assert.NotNull(rec.Tasks);
        Assert.NotNull(rec.Alarms);

        var json = JsonSerializer.Serialize(rec.Setting, MachineMonitor.JsonOptions);
        Assert.Contains("***", json, StringComparison.Ordinal);
        Assert.DoesNotContain("s3cret", json, StringComparison.Ordinal);

        var parameters = rec.ToUpsertParameters();
        Assert.Equal("test-machine-1", parameters["id"]);
        Assert.Equal("DieBonder", parameters["machine_type"]);
        Assert.Contains("CAST(@setting_json AS JSON)", MachineMonitorRecord.UpsertSql, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Api_machine_returns_monitor_payload()
    {
        var port = GetFreeLoopbackPort();
        var dir = Path.Combine(Path.GetTempPath(), $"mdk-machine-api-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        var setting = new MdkSetting
        {
            ProjectName = "api-monitor",
            MachineId = "api-machine-1",
            MachineType = "Dispenser",
            DatabasePath = Path.Combine(dir, "mdk.db"),
            MonitoringPrefix = $"http://127.0.0.1:{port}/",
        };
        using var rt = new MdkRuntime(setting, Path.Combine(dir, "setting.json"));
        rt.Initialize();
        rt.Start();
        try
        {
            using var handler = new HttpClientHandler { UseProxy = false };
            using var client = new HttpClient(handler)
            {
                BaseAddress = new Uri($"http://127.0.0.1:{port}"),
                Timeout = TimeSpan.FromSeconds(10),
            };
            using var response = await client.GetAsync("/api/machine");
            response.EnsureSuccessStatusCode();
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.Equal("api-machine-1", doc.RootElement.GetProperty("id").GetString());
            Assert.Equal("api-monitor", doc.RootElement.GetProperty("name").GetString());
            Assert.Equal("Dispenser", doc.RootElement.GetProperty("machineType").GetString());
            Assert.Equal(MdkProduct.Version, doc.RootElement.GetProperty("version").GetString());
            Assert.True(doc.RootElement.GetProperty("vars").ValueKind == JsonValueKind.Object);
            Assert.True(doc.RootElement.GetProperty("recipe").ValueKind == JsonValueKind.Object);
            Assert.True(doc.RootElement.GetProperty("orders").ValueKind == JsonValueKind.Array);
        }
        finally
        {
            await rt.StopAsync();
            rt.Dispose();
            try
            {
                Directory.Delete(dir, recursive: true);
            }
            catch
            {
                // temp cleanup is best-effort
            }
        }
    }
}
