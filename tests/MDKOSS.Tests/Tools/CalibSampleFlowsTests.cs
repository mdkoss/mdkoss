using MDKOSS.Core;
using MDKOSS.Core.Flow;
using MDKOSS.Tasks;
using MDKOSS.Tools.Calib.Calib;

namespace MDKOSS.Tests.Tools;

public sealed class CalibSampleFlowsTests
{
    public static TheoryData<string, int> SampleSettings => new()
    {
        { Path.Combine("src", "MDKOSS.Sample.DieBonder", "configs", "sample.setting.json"), 3 },
        { Path.Combine("src", "MDKOSS.Sample.Dispenser", "configs", "sample.setting.json"), 2 },
        { Path.Combine("src", "MDKOSS.Sample.Pnp", "configs", "sample.setting.json"), 2 },
        { Path.Combine("src", "MDKOSS.Sample", "configs", "sample.setting.json"), 1 },
        { Path.Combine("src", "MDKOSS.Sample.Tools", "configs", "sample.setting.json"), 2 },
        { Path.Combine("src", "MDKOSS.Tools.Calib", "configs", "sample.setting.json"), 4 },
    };

    [Theory]
    [MemberData(nameof(SampleSettings))]
    public void Sample_setting_lists_calib_flows(string relativeSetting, int expectedCount)
    {
        var settingPath = CalibTestFiles.FindRepoFile(relativeSetting);
        Assert.True(File.Exists(settingPath), settingPath);
        var setting = MdkSetting.Load(settingPath);
        var items = CalibCatalog.List(setting);
        Assert.Equal(expectedCount, items.Count);

        foreach (var task in items)
        {
            Assert.True(CalibCatalog.IsCalibTask(task), task.Name);
            if (!CalibCatalog.IsFlowKind(task.Type))
            {
                Assert.StartsWith("calib", task.Type, StringComparison.OrdinalIgnoreCase);
                continue;
            }

            Assert.True(task.Parameters.TryGetValue("flowFile", out var flowFile), task.Name);
            Assert.True(FlowTask.TryReadFlowFile(flowFile, out var json, settingPath), $"{task.Name}: {flowFile}");
            Assert.True(FlowDocument.TryParse(json, out var doc, out var error), error);
            Assert.Empty(doc.Validate());
            Assert.Contains(doc.Nodes, n =>
                string.Equals(n.Kind, FlowNodeKinds.MotionSetTaskVar, StringComparison.OrdinalIgnoreCase)
                && n.Props.TryGetValue("key", out var key)
                && key.StartsWith("calib.", StringComparison.OrdinalIgnoreCase));
        }
    }

    [Fact]
    public void Factory_platform_axis_flow_validates()
    {
        var doc = CalibFlowFactory.CreatePlatformAxisMove(
            "head-bond",
            "X",
            "10",
            "offsetX",
            "10",
            "开始贴装头 XY 标定",
            "贴装头 X 到达 10");
        Assert.Empty(doc.Validate());
        Assert.Contains("head-bond", CalibFlowFactory.ToJson(doc));
    }

    [Fact]
    public void FlowTask_reads_flowFile_next_to_setting()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"mdk-flow-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(dir, "configs", "flows"));
        var settingPath = Path.Combine(dir, "sample.setting.json");
        var flowPath = Path.Combine(dir, "configs", "flows", "demo.flow.json");
        var doc = CalibFlowFactory.CreatePlatformAxisMove("head-demo", "X", "5", "offsetX", "5", "start", "done");
        File.WriteAllText(flowPath, doc.ToJson());
        File.WriteAllText(settingPath, "{}");

        var task = FlowTask.Create(
            new MdkSetting.TaskConfig
            {
                Name = "calib-demo",
                Type = "flow",
                IntervalMs = 20,
                Parameters =
                {
                    ["calib"] = "true",
                    ["loop"] = "false",
                    ["autoStart"] = "false",
                    ["flowFile"] = "configs/flows/demo.flow.json",
                },
            },
            new MVarStore(),
            settingPath: settingPath);

        Assert.Equal(FlowRunState.Idle, task.FlowState);
        task.Reset();
        Assert.Equal(FlowRunState.Running, task.FlowState);
    }
}
