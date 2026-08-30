using MDKOSS.Core;
using MDKOSS.Core.Data;
using MDKOSS.Tools.Calib.Calib;

namespace MDKOSS.Tests.Tools;

public sealed class CalibStoreTests
{
    [Fact]
    public void CollectVisibleParams_skips_hidden_keys()
    {
        var config = new MdkSetting.TaskConfig
        {
            Name = "calib-head-xy",
            Type = "flow",
            Parameters =
            {
                ["calib"] = "true",
                ["flowFile"] = "configs/flows/a.flow.json",
                ["loop"] = "false",
                ["autoStart"] = "false",
                ["displayName"] = "贴装头",
                ["group"] = "标定",
                ["expectedX"] = "10",
            },
        };

        var visible = CalibStore.CollectVisibleParams(config);
        Assert.False(visible.ContainsKey("calib"));
        Assert.False(visible.ContainsKey("flowFile"));
        Assert.Equal("10", visible["expectedX"]);
        Assert.Single(visible);
    }

    [Fact]
    public void CollectResults_reads_calib_prefix()
    {
        var snap = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["task.foo.calib.ok"] = true,
            ["task.foo.calib.offsetX"] = 1.5,
            ["task.foo.phase"] = "Done",
            ["task.bar.calib.ok"] = false,
        };

        var results = CalibStore.CollectResults(snap, "foo");
        Assert.Equal(2, results.Count);
        Assert.Equal("True", results["ok"]);
        Assert.Equal("1.5", results["offsetX"]);
        Assert.True(CalibStore.IsTruthyResult(results));
        Assert.False(CalibStore.IsTruthyResult(new Dictionary<string, string> { ["ok"] = "false" }));
    }

    [Fact]
    public void Save_and_load_params_and_results()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"mdk-calib-store-{Guid.NewGuid():N}.db");
        using var store = new MdkDataStore(dbPath);

        var parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["expectedX"] = "10",
        };
        Assert.True(CalibStore.TrySaveParams(store, "半导体贴片机-DieBonder", "calib-head-xy", parameters, out var error), error);
        Assert.True(CalibStore.TryLoadParams(store, "半导体贴片机-DieBonder", "calib-head-xy", out var loaded));
        Assert.Equal("10", loaded["expectedX"]);
        Assert.False(CalibStore.TryLoadParams(store, "半导体贴片机-DieBonder", "missing", out _));

        Assert.True(CalibStore.TrySaveResult(
            store,
            "半导体贴片机-DieBonder",
            "calib-head-xy",
            parameters,
            new Dictionary<string, string> { ["ok"] = "true", ["offsetX"] = "10" },
            ok: true,
            "流程完成",
            out error), error);

        Assert.True(CalibStore.TryLoadLatestResult(store, "半导体贴片机-DieBonder", "calib-head-xy", out var result));
        Assert.True(result!.Ok);
        Assert.Equal("流程完成", result.Message);
        Assert.Equal("10", result.Params["expectedX"]);
        Assert.Equal("10", result.Results["offsetX"]);
    }
}
