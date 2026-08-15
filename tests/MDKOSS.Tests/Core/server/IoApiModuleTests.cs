using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json;
using MDKOSS.Core;

namespace MDKOSS.Tests.Core;

public sealed class IoApiModuleTests
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private static int GetFreeLoopbackPort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    private static async Task WithServerAsync(Func<HttpClient, MdkRuntime, Task> body, Action<MdkSetting>? configure = null)
    {
        var port = GetFreeLoopbackPort();
        var dir = Path.Combine(Path.GetTempPath(), $"mdk-io-api-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        var settingPath = Path.Combine(dir, "setting.json");
        var setting = new MdkSetting
        {
            ProjectName = "io-driver",
            CycleMs = 20,
            DatabasePath = Path.Combine(dir, "mdk.db"),
            MonitoringPrefix = $"http://127.0.0.1:{port}/",
            Drivers =
            [
                new MdkSetting.DriverConfig
                {
                    Id = "sim1",
                    Type = "sim",
                    Enabled = true,
                    Parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["ioBitBase"] = "0",
                        ["inBits"] = "32",
                        ["outBits"] = "32",
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
                // temp dir
            }
        }
    }

    [Fact]
    public async Task Driver_port_read_write_roundtrip_on_sim()
    {
        await WithServerAsync(async (client, _) =>
        {
            var empty = await client.GetFromJsonAsync<JsonElement>("/api/io/driver?driverId=sim1&dir=do&type=gpo", Json);
            Assert.True(empty.GetProperty("success").GetBoolean());
            Assert.Equal(0, empty.GetProperty("word").GetInt32());
            Assert.Equal(0, empty.GetProperty("ioBitBase").GetInt32());

            var write = await client.PostAsJsonAsync("/api/io/driver", new
            {
                driverId = "sim1",
                dir = "do",
                type = "gpo",
                bit = 0,
                value = true,
            });
            write.EnsureSuccessStatusCode();
            var written = await write.Content.ReadFromJsonAsync<JsonElement>(Json);
            Assert.True(written.GetProperty("success").GetBoolean());
            Assert.Equal("do.gpo.bit.0", written.GetProperty("address").GetString());

            var after = await client.GetFromJsonAsync<JsonElement>("/api/io/driver?driverId=sim1&dir=do&type=gpo", Json);
            Assert.Equal(1, after.GetProperty("word").GetInt32());
            var bit0 = after.GetProperty("bits")[0];
            Assert.True(bit0.GetProperty("value").GetBoolean());
            Assert.Equal(0, bit0.GetProperty("addressBit").GetInt32());
        });
    }

    [Fact]
    public async Task Driver_port_ioBitBase_1_uses_bit1_as_first()
    {
        await WithServerAsync(
            async (client, _) =>
            {
                var write0 = await client.PostAsJsonAsync("/api/io/driver", new
                {
                    driverId = "sim1",
                    dir = "do",
                    type = "gpo",
                    bit = 0,
                    value = true,
                });
                Assert.Equal(HttpStatusCode.BadRequest, write0.StatusCode);

                var write1 = await client.PostAsJsonAsync("/api/io/driver", new
                {
                    driverId = "sim1",
                    dir = "do",
                    type = "gpo",
                    bit = 1,
                    value = true,
                });
                write1.EnsureSuccessStatusCode();

                var after = await client.GetFromJsonAsync<JsonElement>("/api/io/driver?driverId=sim1&dir=do&type=gpo", Json);
                Assert.Equal(1, after.GetProperty("word").GetInt32());
                Assert.Equal(1, after.GetProperty("ioBitBase").GetInt32());
                Assert.Equal(1, after.GetProperty("bits")[0].GetProperty("addressBit").GetInt32());
                Assert.True(after.GetProperty("bits")[0].GetProperty("value").GetBoolean());
            },
            setting =>
            {
                setting.Drivers[0].Parameters["ioBitBase"] = "1";
            });
    }
}
