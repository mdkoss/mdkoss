using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json;
using MDKOSS.Core;

namespace MDKOSS.Tests.Core;

public sealed class AlarmsVisionsApiTests
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
    public void AlarmHub_match_supports_numeric_and_truthy()
    {
        Assert.True(MdkAlarmHub.Match(1, "gt", "0"));
        Assert.True(MdkAlarmHub.Match("fault", "eq", "FAULT"));
        Assert.True(MdkAlarmHub.Match(true, "truthy", ""));
        Assert.False(MdkAlarmHub.Match(false, "truthy", ""));
        Assert.True(MdkAlarmHub.Match("", "empty", ""));
    }

    [Fact]
    public async Task Alarms_api_evaluates_ack_and_test()
    {
        var port = GetFreeLoopbackPort();
        var setting = new MdkSetting
        {
            ProjectName = "alarm-api",
            DatabasePath = Path.Combine(Path.GetTempPath(), $"mdk-alarm-api-{Guid.NewGuid():N}.db"),
            MonitoringPrefix = $"http://127.0.0.1:{port}/",
            Alarms =
            [
                new MdkSetting.AlarmConfig
                {
                    Id = "alm-demo",
                    Code = "DEMO-1",
                    Name = "演示",
                    Level = "error",
                    Enabled = true,
                    VarKey = "alarm.test",
                    Op = "truthy",
                    Message = "test raised",
                    Latch = true,
                },
            ],
            Vars = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["alarm.test"] = false,
            },
        };

        using var rt = new MdkRuntime(setting);
        rt.Initialize();
        rt.Start();

        try
        {
            using var handler = new HttpClientHandler { UseProxy = false };
            using var client = new HttpClient(handler) { BaseAddress = new Uri($"http://127.0.0.1:{port}") };

            var idle = await client.GetFromJsonAsync<JsonElement>("/api/alarms");
            Assert.True(idle.GetProperty("success").GetBoolean());
            Assert.Equal(0, idle.GetProperty("activeCount").GetInt32());

            var testRes = await client.PostAsync("/api/alarms/test", null);
            testRes.EnsureSuccessStatusCode();
            using var testDoc = JsonDocument.Parse(await testRes.Content.ReadAsStringAsync());
            Assert.Equal(1, testDoc.RootElement.GetProperty("activeCount").GetInt32());
            Assert.Equal(1, testDoc.RootElement.GetProperty("unackedCount").GetInt32());

            var ackRes = await client.PostAsJsonAsync("/api/alarms/ack", new { id = "alm-demo" });
            ackRes.EnsureSuccessStatusCode();

            var resetRes = await client.PostAsync("/api/alarms/reset", null);
            resetRes.EnsureSuccessStatusCode();
            using var resetDoc = JsonDocument.Parse(await resetRes.Content.ReadAsStringAsync());
            Assert.Equal(0, resetDoc.RootElement.GetProperty("activeCount").GetInt32());
        }
        finally
        {
            await rt.StopAsync();
        }
    }

    [Fact]
    public async Task Config_alarms_and_visions_crud()
    {
        var port = GetFreeLoopbackPort();
        var setting = new MdkSetting
        {
            ProjectName = "cfg-api",
            DatabasePath = Path.Combine(Path.GetTempPath(), $"mdk-cfg-api-{Guid.NewGuid():N}.db"),
            MonitoringPrefix = $"http://127.0.0.1:{port}/",
        };

        using var rt = new MdkRuntime(setting);
        rt.Initialize();
        rt.Start();

        try
        {
            using var handler = new HttpClientHandler { UseProxy = false };
            using var client = new HttpClient(handler) { BaseAddress = new Uri($"http://127.0.0.1:{port}") };

            var createAlarm = await client.PostAsJsonAsync("/api/config/alarms", new
            {
                name = "新建",
                code = "E100",
                varKey = "alarm.test",
                op = "truthy",
            });
            createAlarm.EnsureSuccessStatusCode();
            using var alarmDoc = JsonDocument.Parse(await createAlarm.Content.ReadAsStringAsync());
            var alarmId = alarmDoc.RootElement.GetProperty("alarm").GetProperty("id").GetString();
            Assert.False(string.IsNullOrWhiteSpace(alarmId));

            var listAlarms = await client.GetFromJsonAsync<JsonElement>("/api/config/alarms");
            Assert.Equal(1, listAlarms.GetProperty("alarms").GetArrayLength());

            var createVision = await client.PostAsJsonAsync("/api/config/visions", new { name = "检测" });
            createVision.EnsureSuccessStatusCode();
            using var visDoc = JsonDocument.Parse(await createVision.Content.ReadAsStringAsync());
            var visionId = visDoc.RootElement.GetProperty("vision").GetProperty("id").GetString();
            Assert.False(string.IsNullOrWhiteSpace(visionId));

            var listVis = await client.GetFromJsonAsync<JsonElement>("/api/visions");
            Assert.True(listVis.GetProperty("success").GetBoolean());
            Assert.Equal(1, listVis.GetProperty("visions").GetArrayLength());
            Assert.Equal(visionId, listVis.GetProperty("activeVisionId").GetString());
            Assert.Equal("opencv", listVis.GetProperty("defaultAlgorithm").GetString());
            Assert.True(listVis.GetProperty("backends").GetArrayLength() >= 2);
            var first = listVis.GetProperty("visions")[0];
            Assert.False(string.IsNullOrWhiteSpace(first.GetProperty("algorithm").GetString()));
            Assert.True(first.GetProperty("algorithmAvailable").GetBoolean());

            var backends = await client.GetFromJsonAsync<JsonElement>("/api/visions/backends");
            Assert.True(backends.GetProperty("success").GetBoolean());
            Assert.Contains(
                backends.GetProperty("backends").EnumerateArray(),
                b => string.Equals(b.GetProperty("id").GetString(), "opencv", StringComparison.OrdinalIgnoreCase)
                     && b.GetProperty("available").GetBoolean());
        }
        finally
        {
            await rt.StopAsync();
        }
    }
}
