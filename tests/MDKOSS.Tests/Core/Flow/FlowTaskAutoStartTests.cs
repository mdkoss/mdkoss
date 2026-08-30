using MDKOSS.Core;
using MDKOSS.Core.Flow;
using MDKOSS.Tasks;

namespace MDKOSS.Tests.Core.Flow;

public sealed class FlowTaskAutoStartTests
{
    [Fact]
    public async Task AutoStart_false_stays_idle_until_Reset()
    {
        var vars = new MVarStore();
        var task = new FlowTask("calib-flow", 20, FlowDocument.CreateEmpty(), vars, loop: false, autoStart: false);

        Assert.Equal(FlowRunState.Idle, task.FlowState);
        await task.ExecuteOnceAsync(CancellationToken.None);
        Assert.Equal(FlowRunState.Idle, task.FlowState);

        task.Reset();
        Assert.Equal(FlowRunState.Running, task.FlowState);
        await task.ExecuteOnceAsync(CancellationToken.None);
        Assert.Equal(FlowRunState.Completed, task.FlowState);
    }

    [Fact]
    public async Task Halt_returns_to_idle()
    {
        var vars = new MVarStore();
        var task = FlowTask.Create(
            new MdkSetting.TaskConfig
            {
                Name = "gated",
                Type = "flow",
                IntervalMs = 20,
                Parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["loop"] = "false",
                    ["autoStart"] = "false",
                },
            },
            vars);

        Assert.Equal(FlowRunState.Idle, task.FlowState);
        task.Reset();
        task.Halt();
        Assert.Equal(FlowRunState.Idle, task.FlowState);
        await task.ExecuteOnceAsync(CancellationToken.None);
        Assert.Equal(FlowRunState.Idle, task.FlowState);
    }

    [Fact]
    public void TryReadFlowFile_missing_returns_false()
    {
        Assert.False(FlowTask.TryReadFlowFile("configs/does-not-exist.flow.json", out _));
    }
}
