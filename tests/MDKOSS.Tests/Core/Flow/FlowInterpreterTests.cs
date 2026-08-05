using MDKOSS.Core;
using MDKOSS.Core.Flow;
using MDKOSS.Tasks;

namespace MDKOSS.Tests.Core.Flow;

public sealed class FlowInterpreterTests
{
    [Fact]
    public void Empty_document_validates()
    {
        var doc = FlowDocument.CreateEmpty();
        Assert.Empty(doc.Validate());
    }

    [Fact]
    public void Parse_roundtrip()
    {
        var doc = FlowDocument.CreateEmpty();
        var json = doc.ToJson();
        var again = FlowDocument.Parse(json);
        Assert.Equal(doc.Nodes.Count, again.Nodes.Count);
        Assert.Equal(doc.Edges.Count, again.Edges.Count);
    }

    [Fact]
    public void SetVar_and_log_completes()
    {
        var doc = new FlowDocument
        {
            Version = 1,
            Variables = [new FlowVariable { Name = "x", Type = "number", Init = "0" }],
            Functions = [new FlowFunction { Name = "main", EntryNodeId = "s" }],
            Nodes =
            [
                new FlowNode { Id = "s", Kind = FlowNodeKinds.Start },
                new FlowNode
                {
                    Id = "a",
                    Kind = FlowNodeKinds.SetVar,
                    Props = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["name"] = "x",
                        ["expr"] = "x + 2",
                    },
                },
                new FlowNode
                {
                    Id = "l",
                    Kind = FlowNodeKinds.OpLog,
                    Props = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["message"] = "\"v=\" + x",
                    },
                },
                new FlowNode { Id = "e", Kind = FlowNodeKinds.End },
            ],
            Edges =
            [
                new FlowEdge { From = "s", To = "a", Port = FlowPorts.Next },
                new FlowEdge { From = "a", To = "l", Port = FlowPorts.Next },
                new FlowEdge { From = "l", To = "e", Port = FlowPorts.Next },
            ],
        };

        Assert.Empty(doc.Validate());
        var vars = new MVarStore();
        var interp = new FlowInterpreter(doc, "demo", vars);
        interp.Reset();
        interp.Pump(64);
        Assert.Equal(FlowRunState.Completed, interp.State);
        Assert.Equal(2.0, vars.Get<double>("task.demo.flow.var.x"));
    }

    [Fact]
    public void If_branches_true()
    {
        var doc = new FlowDocument
        {
            Version = 1,
            Variables = [new FlowVariable { Name = "flag", Type = "bool", Init = "true" }],
            Functions = [new FlowFunction { Name = "main", EntryNodeId = "s" }],
            Nodes =
            [
                new FlowNode { Id = "s", Kind = FlowNodeKinds.Start },
                new FlowNode
                {
                    Id = "i",
                    Kind = FlowNodeKinds.If,
                    Props = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["condition"] = "flag",
                    },
                },
                new FlowNode
                {
                    Id = "t",
                    Kind = FlowNodeKinds.SetVar,
                    Props = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["name"] = "flag",
                        ["expr"] = "false",
                    },
                },
                new FlowNode
                {
                    Id = "f",
                    Kind = FlowNodeKinds.SetVar,
                    Props = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["name"] = "flag",
                        ["expr"] = "true",
                    },
                },
                new FlowNode { Id = "e", Kind = FlowNodeKinds.End },
            ],
            Edges =
            [
                new FlowEdge { From = "s", To = "i", Port = FlowPorts.Next },
                new FlowEdge { From = "i", To = "t", Port = FlowPorts.True },
                new FlowEdge { From = "i", To = "f", Port = FlowPorts.False },
                new FlowEdge { From = "t", To = "e", Port = FlowPorts.Next },
                new FlowEdge { From = "f", To = "e", Port = FlowPorts.Next },
            ],
        };

        Assert.Empty(doc.Validate());
        var vars = new MVarStore();
        var interp = new FlowInterpreter(doc, "demo", vars);
        interp.Reset();
        interp.Pump(64);
        Assert.Equal(FlowRunState.Completed, interp.State);
        Assert.False(vars.Get<bool>("task.demo.flow.var.flag"));
    }

    [Fact]
    public void While_runs_limited_times()
    {
        var doc = new FlowDocument
        {
            Version = 1,
            Variables = [new FlowVariable { Name = "x", Type = "number", Init = "0" }],
            Functions = [new FlowFunction { Name = "main", EntryNodeId = "s" }],
            Nodes =
            [
                new FlowNode { Id = "s", Kind = FlowNodeKinds.Start },
                new FlowNode
                {
                    Id = "w",
                    Kind = FlowNodeKinds.While,
                    Props = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["condition"] = "x < 3",
                    },
                },
                new FlowNode
                {
                    Id = "inc",
                    Kind = FlowNodeKinds.SetVar,
                    Props = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["name"] = "x",
                        ["expr"] = "x + 1",
                    },
                },
                new FlowNode { Id = "e", Kind = FlowNodeKinds.End },
            ],
            Edges =
            [
                new FlowEdge { From = "s", To = "w", Port = FlowPorts.Next },
                new FlowEdge { From = "w", To = "inc", Port = FlowPorts.Body },
                new FlowEdge { From = "inc", To = "w", Port = FlowPorts.Next }, // loop back
                new FlowEdge { From = "w", To = "e", Port = FlowPorts.Exit },
            ],
        };

        Assert.Empty(doc.Validate());
        var vars = new MVarStore();
        var interp = new FlowInterpreter(doc, "demo", vars);
        interp.Reset();
        interp.Pump(256);
        Assert.Equal(FlowRunState.Completed, interp.State);
        Assert.Equal(3.0, vars.Get<double>("task.demo.flow.var.x"));
    }

    [Fact]
    public void Delay_yields_across_pumps()
    {
        var doc = new FlowDocument
        {
            Version = 1,
            Functions = [new FlowFunction { Name = "main", EntryNodeId = "s" }],
            Nodes =
            [
                new FlowNode { Id = "s", Kind = FlowNodeKinds.Start },
                new FlowNode
                {
                    Id = "d",
                    Kind = FlowNodeKinds.Delay,
                    Props = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["ms"] = "50",
                    },
                },
                new FlowNode { Id = "e", Kind = FlowNodeKinds.End },
            ],
            Edges =
            [
                new FlowEdge { From = "s", To = "d", Port = FlowPorts.Next },
                new FlowEdge { From = "d", To = "e", Port = FlowPorts.Next },
            ],
        };

        var vars = new MVarStore();
        var interp = new FlowInterpreter(doc, "demo", vars);
        interp.Reset();
        interp.Pump(16);
        Assert.Equal(FlowRunState.Waiting, interp.State);
        Thread.Sleep(60);
        interp.Pump(16);
        Assert.Equal(FlowRunState.Completed, interp.State);
    }

    [Fact]
    public void FlowTask_Create_from_config()
    {
        var doc = FlowDocument.CreateEmpty();
        var config = new MdkSetting.TaskConfig
        {
            Name = "task-flow",
            Type = "flow",
            IntervalMs = 50,
            Parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["flowJson"] = doc.ToJson(),
                ["loop"] = "false",
            },
        };

        var vars = new MVarStore();
        var task = FlowTask.Create(config, vars);
        Assert.Equal("task-flow", task.Name);
        Assert.True(RuntimeTaskFactory.IsSupported("flow"));
    }

    [Fact]
    public void Invalid_json_fails_validate_path()
    {
        Assert.False(FlowDocument.TryParse("{not-json", out _, out var err));
        Assert.False(string.IsNullOrWhiteSpace(err));
    }

    [Fact]
    public void Expr_arith_and_compare()
    {
        object? Resolve(string n) => n == "a" ? 3.0 : 0.0;
        Assert.Equal(5.0, FlowExpr.ToNumber(FlowExpr.Eval("a + 2", Resolve)));
        Assert.True(FlowExpr.EvalBool("a > 1", Resolve));
        Assert.False(FlowExpr.EvalBool("a > 10", Resolve));
    }
}
