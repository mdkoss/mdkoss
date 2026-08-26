using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json;
using MDKOSS.Core;

namespace MDKOSS.Tests.Core;

/// <summary>
/// HTTP coverage for /api/config modules used by man_* pages
/// (machine / drivers / devices / axes / platforms / gpio / tasks / vars / recipes / visions / alarms).
/// </summary>
public sealed class ConfigApiModuleTests
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

    private static async Task WithServerAsync(Func<HttpClient, MdkRuntime, Task> body, Action<MdkSetting>? configure = null)
    {
        var port = GetFreeLoopbackPort();
        var dir = Path.Combine(Path.GetTempPath(), $"mdk-cfg-api-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        var settingPath = Path.Combine(dir, "setting.json");
        var setting = new MdkSetting
        {
            ProjectName = "cfg-modules",
            CycleMs = 20,
            DatabasePath = Path.Combine(dir, "mdk.db"),
            MonitoringPrefix = $"http://127.0.0.1:{port}/",
            Drivers =
            [
                new MdkSetting.DriverConfig
                {
                    Id = "drv-sim",
                    Name = "sim",
                    Type = "sim",
                    Enabled = true,
                    Parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["keep"] = "1",
                        ["drop"] = "x",
                    },
                },
            ],
        };
        configure?.Invoke(setting);
        setting.Save(settingPath);

        var rt = new MdkRuntime(setting, settingPath);
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
            await body(client, rt).ConfigureAwait(false);
        }
        finally
        {
            await rt.StopAsync().ConfigureAwait(false);
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

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
    {
        var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        Assert.True(response.IsSuccessStatusCode, $"{response.StatusCode}: {json}");
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }

    private static string ReqId(JsonElement created, string wrap)
    {
        var node = created.GetProperty(wrap);
        if (node.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String)
        {
            var s = id.GetString();
            if (!string.IsNullOrWhiteSpace(s))
            {
                return s;
            }
        }

        if (node.TryGetProperty("name", out var name) && name.ValueKind == JsonValueKind.String)
        {
            return name.GetString() ?? "";
        }

        if (node.TryGetProperty("key", out var key) && key.ValueKind == JsonValueKind.String)
        {
            return key.GetString() ?? "";
        }

        return "";
    }

    [Fact]
    public async Task Config_summary_and_catalog()
    {
        await WithServerAsync(async (client, _) =>
        {
            var summary = await ReadJsonAsync(await client.GetAsync("/api/config"));
            Assert.True(summary.GetProperty("success").GetBoolean());
            Assert.Equal("cfg-modules", summary.GetProperty("projectName").GetString());
            Assert.Equal(1, summary.GetProperty("counts").GetProperty("drivers").GetInt32());

            var catalog = await ReadJsonAsync(await client.GetAsync("/api/config/catalog"));
            Assert.Contains("drv-sim", catalog.GetProperty("driverIds").EnumerateArray().Select(x => x.GetString()));
            Assert.True(catalog.GetProperty("types").GetProperty("devices").GetArrayLength() > 0);
            Assert.True(catalog.GetProperty("axisIds").GetArrayLength() >= 0);

            var tpl = await ReadJsonAsync(await client.GetAsync("/api/config/catalog?module=devices&type=gpio&driverId=drv-sim"));
            Assert.Contains("drv-sim", tpl.GetProperty("parameters").GetProperty("in.startButton").GetString());
        });
    }

    [Fact]
    public async Task Config_machine_get_and_patch()
    {
        await WithServerAsync(async (client, rt) =>
        {
            var get = await ReadJsonAsync(await client.GetAsync("/api/config/machine"));
            Assert.Equal("cfg-modules", get.GetProperty("projectName").GetString());
            Assert.Equal(20, get.GetProperty("cycleMs").GetInt32());
            Assert.Equal("cfg-modules", get.GetProperty("parameters").GetProperty("projectName").GetString());

            var patch = await client.PatchAsJsonAsync("/api/config/machine", new
            {
                parameters = new Dictionary<string, string>
                {
                    ["projectName"] = "patched-machine",
                    ["machineId"] = "mid-1",
                    ["machineType"] = "Pnp",
                    ["cycleMs"] = "50",
                    ["startPage"] = "index.html",
                    ["recipeVarKeys"] = "a,b",
                },
            });
            var after = await ReadJsonAsync(patch);
            Assert.Equal("patched-machine", after.GetProperty("projectName").GetString());
            Assert.Equal("mid-1", after.GetProperty("machineId").GetString());
            Assert.Equal("Pnp", after.GetProperty("machineType").GetString());
            Assert.Equal(50, after.GetProperty("cycleMs").GetInt32());
            Assert.Equal("index.html", after.GetProperty("startPage").GetString());
            Assert.Equal("patched-machine", rt.Setting.ProjectName);
            Assert.Equal("mid-1", rt.Setting.MachineId);
            Assert.Equal("Pnp", rt.Setting.MachineType);
            Assert.Equal(["a", "b"], rt.Setting.RecipeVarKeys);
        });
    }

    [Fact]
    public async Task Config_drivers_crud_replaces_parameters()
    {
        await WithServerAsync(async (client, rt) =>
        {
            var created = await ReadJsonAsync(await client.PostAsJsonAsync("/api/config/drivers", new { type = "sim", name = "copy" }));
            var id = ReqId(created, "driver");
            Assert.False(string.IsNullOrWhiteSpace(id));

            var list = await ReadJsonAsync(await client.GetAsync("/api/config/drivers"));
            Assert.Equal(2, list.GetProperty("drivers").GetArrayLength());

            var patched = await ReadJsonAsync(await client.PatchAsJsonAsync($"/api/config/drivers/drv-sim", new
            {
                name = "sim-renamed",
                enabled = false,
                type = "sim",
                parameters = new Dictionary<string, string> { ["keep"] = "2" },
            }));
            Assert.Equal("sim-renamed", patched.GetProperty("driver").GetProperty("name").GetString());
            var ps = patched.GetProperty("driver").GetProperty("parameters");
            Assert.Equal("2", ps.GetProperty("keep").GetString());
            Assert.False(ps.TryGetProperty("drop", out _));
            Assert.False(rt.Setting.Drivers.First(d => d.Id == "drv-sim").Parameters.ContainsKey("drop"));

            var del = await client.DeleteAsync($"/api/config/drivers/{id}");
            (await ReadJsonAsync(del)).GetProperty("success").GetBoolean();
            Assert.DoesNotContain(rt.Setting.Drivers, d => d.Id == id);

            var missing = await client.DeleteAsync("/api/config/drivers/no-such");
            Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
        });
    }

    [Fact]
    public async Task Config_devices_crud()
    {
        await WithServerAsync(async (client, rt) =>
        {
            var created = await ReadJsonAsync(await client.PostAsJsonAsync("/api/config/devices", new
            {
                type = "serialdev",
                name = "串口",
            }));
            var id = ReqId(created, "device");
            Assert.StartsWith("dev-new", id, StringComparison.OrdinalIgnoreCase);
            Assert.Equal("drv-sim", created.GetProperty("device").GetProperty("driverId").GetString());

            await ReadJsonAsync(await client.PatchAsJsonAsync($"/api/config/devices/{id}", new
            {
                name = "串口-1",
                driverId = "drv-sim",
                enabled = true,
                type = "serialdev",
                parameters = new Dictionary<string, string> { ["portName"] = "COM3" },
            }));
            Assert.Equal("串口-1", rt.Setting.Devices.Single(d => d.Id == id).Name);
            Assert.Equal("COM3", rt.Setting.Devices.Single(d => d.Id == id).Parameters["portName"]);

            var list = await ReadJsonAsync(await client.GetAsync("/api/config/devices"));
            Assert.Equal(1, list.GetProperty("devices").GetArrayLength());

            await ReadJsonAsync(await client.DeleteAsync($"/api/config/devices/{id}"));
            Assert.Empty(rt.Setting.Devices);
        });
    }

    [Fact]
    public async Task Config_axes_crud()
    {
        await WithServerAsync(async (client, rt) =>
        {
            var created = await ReadJsonAsync(await client.PostAsJsonAsync("/api/config/axes", new { type = "linear" }));
            var id = ReqId(created, "device");
            Assert.Equal("linear", created.GetProperty("device").GetProperty("type").GetString());
            Assert.NotEmpty(created.GetProperty("device").GetProperty("parameters").EnumerateObject());

            await ReadJsonAsync(await client.PatchAsJsonAsync($"/api/config/axes/{id}", new
            {
                name = "X",
                type = "linear",
                driverId = "drv-sim",
                enabled = true,
                parameters = new Dictionary<string, string> { ["lead"] = "10" },
            }));
            Assert.Equal("X", rt.Setting.Axes.Single().Name);

            var list = await ReadJsonAsync(await client.GetAsync("/api/config/axes"));
            Assert.Equal(1, list.GetProperty("axes").GetArrayLength());

            var catalog = await ReadJsonAsync(await client.GetAsync("/api/config/catalog"));
            Assert.Contains(id, catalog.GetProperty("axisIds").EnumerateArray().Select(x => x.GetString()));

            await ReadJsonAsync(await client.DeleteAsync($"/api/config/axes/{id}"));
            Assert.Empty(rt.Setting.Axes);
        });
    }

    [Fact]
    public async Task Config_platforms_crud()
    {
        await WithServerAsync(async (client, rt) =>
        {
            rt.Setting.Axes.Add(new MdkSetting.DeviceConfig { Id = "ax-x", Type = "linear", Name = "X" });

            var created = await ReadJsonAsync(await client.PostAsJsonAsync("/api/config/platforms", new { type = "xy" }));
            var id = ReqId(created, "device");
            Assert.Equal("xy", created.GetProperty("device").GetProperty("type").GetString());

            await ReadJsonAsync(await client.PatchAsJsonAsync($"/api/config/platforms/{id}", new
            {
                name = "XY",
                type = "xy",
                enabled = true,
                parameters = new Dictionary<string, string>
                {
                    ["axis.X"] = "ax-x",
                    ["axis.Y"] = "",
                },
            }));
            Assert.Equal("ax-x", rt.Setting.Platforms.Single().Parameters["axis.X"]);

            var list = await ReadJsonAsync(await client.GetAsync("/api/config/platforms"));
            Assert.Equal(1, list.GetProperty("platforms").GetArrayLength());

            await ReadJsonAsync(await client.DeleteAsync($"/api/config/platforms/{id}"));
            Assert.Empty(rt.Setting.Platforms);
        });
    }

    [Fact]
    public async Task Config_gpio_and_vio_devices()
    {
        await WithServerAsync(async (client, rt) =>
        {
            var gpio = await ReadJsonAsync(await client.PostAsJsonAsync("/api/config/devices", new { type = "gpio" }));
            var gpioId = ReqId(gpio, "device");
            Assert.Contains("in.startButton", gpio.GetProperty("device").GetProperty("parameters").EnumerateObject().Select(p => p.Name));

            await ReadJsonAsync(await client.PatchAsJsonAsync($"/api/config/devices/{gpioId}", new
            {
                name = "IO",
                type = "gpio",
                driverId = "drv-sim",
                enabled = true,
                parameters = new Dictionary<string, string>
                {
                    ["in.start"] = "drv-sim|di.gpi.bit.1|启动",
                    ["out.lamp"] = "drv-sim|do.gpo.bit.1|灯",
                },
            }));
            Assert.Equal("drv-sim|di.gpi.bit.1|启动", rt.Setting.Devices.First(d => d.Id == gpioId).Parameters["in.start"]);
            Assert.False(rt.Setting.Devices.First(d => d.Id == gpioId).Parameters.ContainsKey("in.startButton"));

            var vio = await ReadJsonAsync(await client.PostAsJsonAsync("/api/config/devices", new { type = "vio" }));
            var vioId = ReqId(vio, "device");
            Assert.Contains(
                vio.GetProperty("device").GetProperty("parameters").EnumerateObject(),
                p => p.Name.StartsWith("vio.b", StringComparison.OrdinalIgnoreCase));

            var tpl = await ReadJsonAsync(await client.GetAsync("/api/config/catalog?module=devices&type=vio"));
            Assert.True(tpl.GetProperty("parameters").EnumerateObject().Any());

            var list = await ReadJsonAsync(await client.GetAsync("/api/config/devices"));
            Assert.Equal(2, list.GetProperty("devices").GetArrayLength());

            await ReadJsonAsync(await client.DeleteAsync($"/api/config/devices/{gpioId}"));
            await ReadJsonAsync(await client.DeleteAsync($"/api/config/devices/{vioId}"));
            Assert.Empty(rt.Setting.Devices);
        });
    }

    [Fact]
    public async Task Config_tasks_crud()
    {
        await WithServerAsync(async (client, rt) =>
        {
            var created = await ReadJsonAsync(await client.PostAsJsonAsync("/api/config/tasks", new { type = "pollDriver" }));
            var name = ReqId(created, "task");
            Assert.Equal("pollDriver", created.GetProperty("task").GetProperty("type").GetString());

            await ReadJsonAsync(await client.PatchAsJsonAsync($"/api/config/tasks/{name}", new
            {
                name,
                type = "pollDriver",
                driverId = "drv-sim",
                intervalMs = 200,
                parameters = new Dictionary<string, string> { ["varPrefix"] = "drv" },
            }));
            var task = rt.Setting.Tasks.Single(t => t.Name == name);
            Assert.Equal(200, task.IntervalMs);
            Assert.Equal("drv", task.Parameters["varPrefix"]);

            var list = await ReadJsonAsync(await client.GetAsync("/api/config/tasks"));
            Assert.Equal(1, list.GetProperty("tasks").GetArrayLength());

            await ReadJsonAsync(await client.DeleteAsync($"/api/config/tasks/{name}"));
            Assert.Empty(rt.Setting.Tasks);
        });
    }

    [Fact]
    public async Task Config_vars_crud_and_rename()
    {
        await WithServerAsync(async (client, rt) =>
        {
            rt.Setting.Vars["seed.a"] = 1;

            var list0 = await ReadJsonAsync(await client.GetAsync("/api/config/vars"));
            Assert.Equal(1, list0.GetProperty("vars").GetArrayLength());

            var created = await ReadJsonAsync(await client.PostAsJsonAsync("/api/config/vars", new { key = "machine.mode", value = "AUTO" }));
            Assert.Equal("machine.mode", created.GetProperty("varItem").GetProperty("key").GetString());
            Assert.Equal("AUTO", created.GetProperty("varItem").GetProperty("value").GetString());

            var renamed = await ReadJsonAsync(await client.PatchAsJsonAsync("/api/config/vars/machine.mode", new
            {
                key = "machine.mode2",
                value = "MANUAL",
            }));
            Assert.Equal("machine.mode2", renamed.GetProperty("varItem").GetProperty("key").GetString());
            Assert.False(rt.Setting.Vars.ContainsKey("machine.mode"));
            Assert.Equal("MANUAL", rt.Setting.Vars["machine.mode2"]?.ToString());

            var dup = await client.PostAsJsonAsync("/api/config/vars", new { key = "machine.mode2", value = "x" });
            Assert.Equal(HttpStatusCode.BadRequest, dup.StatusCode);

            await ReadJsonAsync(await client.DeleteAsync("/api/config/vars/machine.mode2"));
            Assert.False(rt.Setting.Vars.ContainsKey("machine.mode2"));
        });
    }

    [Fact]
    public async Task Config_recipes_list_includes_vars_and_upsert()
    {
        await WithServerAsync(async (client, rt) =>
        {
            rt.Setting.RecipeVarKeys = ["speed"];
            rt.Setting.Recipes.Add(new MdkSetting.RecipeConfig
            {
                Id = "r1",
                Name = "配方1",
                Description = "d",
                Vars = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase) { ["speed"] = 12 },
            });

            var list = await ReadJsonAsync(await client.GetAsync("/api/recipe"));
            Assert.Equal(1, list.GetProperty("recipes").GetArrayLength());
            var first = list.GetProperty("recipes")[0];
            Assert.Equal("r1", first.GetProperty("id").GetString());
            Assert.Equal(12, first.GetProperty("vars").GetProperty("speed").GetInt32());

            var upsert = await ReadJsonAsync(await client.PostAsJsonAsync("/api/recipe", new
            {
                id = "r2",
                name = "配方2",
                description = "",
                vars = new Dictionary<string, object?> { ["speed"] = 9 },
            }));
            Assert.True(upsert.GetProperty("success").GetBoolean());
            Assert.Equal(2, rt.Setting.Recipes.Count);

            var update = await ReadJsonAsync(await client.PostAsJsonAsync("/api/recipe", new
            {
                id = "r1",
                name = "配方1b",
                description = "x",
                vars = new Dictionary<string, object?> { ["speed"] = 3 },
            }));
            Assert.True(update.GetProperty("success").GetBoolean());
            Assert.Equal("配方1b", rt.Setting.Recipes.First(r => r.Id == "r1").Name);

            await ReadJsonAsync(await client.DeleteAsync("/api/recipe/r2"));
            Assert.DoesNotContain(rt.Setting.Recipes, r => r.Id == "r2");
        });
    }

    [Fact]
    public async Task Config_visions_crud()
    {
        await WithServerAsync(async (client, rt) =>
        {
            rt.Setting.Devices.Add(new MdkSetting.DeviceConfig { Id = "cam-1", Type = "extcamera", Name = "cam" });

            var created = await ReadJsonAsync(await client.PostAsJsonAsync("/api/config/visions", new { name = "检测" }));
            var id = ReqId(created, "vision");
            Assert.Equal(id, rt.Setting.ActiveVisionId);

            await ReadJsonAsync(await client.PatchAsJsonAsync($"/api/config/visions/{id}", new
            {
                name = "检测A",
                description = "desc",
                cameraDeviceId = "cam-1",
            }));
            var v = rt.Setting.Visions.Single();
            Assert.Equal("检测A", v.Name);
            Assert.Equal("cam-1", v.CameraDeviceId);
            Assert.NotNull(v.Pipeline);

            var list = await ReadJsonAsync(await client.GetAsync("/api/config/visions"));
            Assert.Equal(1, list.GetProperty("visions").GetArrayLength());
            Assert.Equal(id, list.GetProperty("activeVisionId").GetString());

            var catalog = await ReadJsonAsync(await client.GetAsync("/api/config/catalog"));
            Assert.Contains("cam-1", catalog.GetProperty("cameraDeviceIds").EnumerateArray().Select(x => x.GetString()));

            await ReadJsonAsync(await client.DeleteAsync($"/api/config/visions/{id}"));
            Assert.Empty(rt.Setting.Visions);
        });
    }

    [Fact]
    public async Task Config_alarms_crud()
    {
        await WithServerAsync(async (client, rt) =>
        {
            var created = await ReadJsonAsync(await client.PostAsJsonAsync("/api/config/alarms", new
            {
                name = "过流",
                code = "E100",
                level = "error",
                op = "gt",
                varKey = "axis.i",
                value = "2",
                message = "电流过大",
                latch = true,
                enabled = true,
            }));
            var id = ReqId(created, "alarm");
            Assert.False(string.IsNullOrWhiteSpace(id));

            await ReadJsonAsync(await client.PatchAsJsonAsync($"/api/config/alarms/{id}", new
            {
                name = "过流A",
                code = "E101",
                level = "warn",
                op = "ge",
                varKey = "axis.i",
                value = "3",
                message = "电流告警",
                solution = "减速",
                module = "axis",
                latch = false,
                enabled = true,
            }));
            var a = rt.Setting.Alarms.Single();
            Assert.Equal("过流A", a.Name);
            Assert.Equal("E101", a.Code);
            Assert.Equal("warn", a.Level);
            Assert.Equal("ge", a.Op);
            Assert.Equal("3", a.Value);
            Assert.Equal("电流告警", a.Message);
            Assert.Equal("减速", a.Solution);
            Assert.Equal("axis", a.Module);
            Assert.False(a.Latch);

            var list = await ReadJsonAsync(await client.GetAsync("/api/config/alarms"));
            Assert.Equal(1, list.GetProperty("alarms").GetArrayLength());

            await ReadJsonAsync(await client.DeleteAsync($"/api/config/alarms/{id}"));
            Assert.Empty(rt.Setting.Alarms);
        });
    }

    [Fact]
    public async Task Config_save_writes_setting_and_rejects_unset_path()
    {
        await WithServerAsync(async (client, rt) =>
        {
            rt.Setting.ProjectName = "saved-name";
            var save = await ReadJsonAsync(await client.PostAsync("/api/config/save", null));
            Assert.True(save.GetProperty("success").GetBoolean());
            Assert.False(string.IsNullOrWhiteSpace(save.GetProperty("settingPath").GetString()));
            Assert.True(File.Exists(rt.SettingPath));
            var reloaded = MdkSetting.Load(rt.SettingPath!);
            Assert.Equal("saved-name", reloaded.ProjectName);

            rt.SettingPath = null;
            var unset = await client.PostAsync("/api/config/save", null);
            Assert.Equal(HttpStatusCode.BadRequest, unset.StatusCode);
            using var doc = JsonDocument.Parse(await unset.Content.ReadAsStringAsync());
            Assert.Equal("setting_path_unset", doc.RootElement.GetProperty("error").GetString());
        });
    }
}
