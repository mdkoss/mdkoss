using System.Text.Json;
using MDKOSS.Core;

namespace MDKOSS.Tests;

public sealed class MdkRuntimeIntegrationTests
{
    [Fact]
    public void Initialize_start_snapshot_stop_lifecycle()
    {
        var setting = new MdkSetting
        {
            ProjectName = "unit-test",
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
        Assert.False(snap.IsRunning);
        Assert.True(snap.Drivers.ContainsKey("d1"));
        Assert.True(snap.Devices.ContainsKey("g1"));
        Assert.True(snap.Drivers["d1"].IsConnected);

        var json = JsonSerializer.Serialize(snap, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        });
        using var doc = JsonDocument.Parse(json);
        Assert.True(doc.RootElement.TryGetProperty("devices", out var devEl));
        Assert.Equal(JsonValueKind.Object, devEl.ValueKind);
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
}
