using MDKOSS.Core.Flow;

namespace MDKOSS.Tests.Core.Flow;

public sealed class FlowCompositeTests
{
    [Fact]
    public void BuildEdges_if_then_else_join()
    {
        var nodes = new List<FlowNode>
        {
            new() { Id = "s", Kind = FlowNodeKinds.Start, Order = 0 },
            new() { Id = "i", Kind = FlowNodeKinds.If, Order = 1, Props = { ["condition"] = "true" } },
            new() { Id = "t", Kind = FlowNodeKinds.SetVar, ParentId = "i", Slot = FlowSlots.Then, Order = 0, Props = { ["name"] = "x", ["expr"] = "1" } },
            new() { Id = "f", Kind = FlowNodeKinds.SetVar, ParentId = "i", Slot = FlowSlots.Else, Order = 0, Props = { ["name"] = "x", ["expr"] = "2" } },
            new() { Id = "e", Kind = FlowNodeKinds.End, Order = 2 },
        };

        var edges = FlowComposite.BuildEdges(nodes);
        Assert.Contains(edges, x => x is { From: "i", Port: "true", To: "t" });
        Assert.Contains(edges, x => x is { From: "i", Port: "false", To: "f" });
        Assert.Contains(edges, x => x is { From: "t", Port: "next", To: "e" });
        Assert.Contains(edges, x => x is { From: "f", Port: "next", To: "e" });

        var doc = new FlowDocument
        {
            Functions = [new FlowFunction { Name = "main", EntryNodeId = "s" }],
            Nodes = nodes,
            Edges = edges,
        };
        Assert.Empty(doc.Validate());
    }

    [Fact]
    public void BuildEdges_while_body_loops_back()
    {
        var nodes = new List<FlowNode>
        {
            new() { Id = "s", Kind = FlowNodeKinds.Start, Order = 0 },
            new() { Id = "w", Kind = FlowNodeKinds.While, Order = 1, Props = { ["condition"] = "x < 3" } },
            new()
            {
                Id = "inc",
                Kind = FlowNodeKinds.SetVar,
                ParentId = "w",
                Slot = FlowSlots.Body,
                Order = 0,
                Props = { ["name"] = "x", ["expr"] = "x + 1" },
            },
            new() { Id = "e", Kind = FlowNodeKinds.End, Order = 2 },
        };

        var edges = FlowComposite.BuildEdges(nodes);
        Assert.Contains(edges, x => x is { From: "w", Port: "body", To: "inc" });
        Assert.Contains(edges, x => x is { From: "inc", Port: "next", To: "w" });
        Assert.Contains(edges, x => x is { From: "w", Port: "exit", To: "e" });
    }

    [Fact]
    public void Nested_if_inside_then()
    {
        var nodes = new List<FlowNode>
        {
            new() { Id = "s", Kind = FlowNodeKinds.Start, Order = 0 },
            new() { Id = "outer", Kind = FlowNodeKinds.If, Order = 1, Props = { ["condition"] = "true" } },
            new() { Id = "inner", Kind = FlowNodeKinds.If, ParentId = "outer", Slot = FlowSlots.Then, Order = 0, Props = { ["condition"] = "false" } },
            new() { Id = "a", Kind = FlowNodeKinds.OpLog, ParentId = "inner", Slot = FlowSlots.Then, Order = 0, Props = { ["message"] = "\"a\"" } },
            new() { Id = "b", Kind = FlowNodeKinds.OpLog, ParentId = "inner", Slot = FlowSlots.Else, Order = 0, Props = { ["message"] = "\"b\"" } },
            new() { Id = "e", Kind = FlowNodeKinds.End, Order = 2 },
        };

        var edges = FlowComposite.BuildEdges(nodes);
        Assert.Contains(edges, x => x is { From: "outer", Port: "true", To: "inner" });
        Assert.Contains(edges, x => x is { From: "outer", Port: "false", To: "e" });
        Assert.Contains(edges, x => x is { From: "a", Port: "next", To: "e" });
        Assert.Contains(edges, x => x is { From: "b", Port: "next", To: "e" });
        Assert.Empty(FlowComposite.ValidateTree(nodes));
    }

    [Fact]
    public void Interpreter_runs_composite_if()
    {
        var nodes = new List<FlowNode>
        {
            new() { Id = "s", Kind = FlowNodeKinds.Start, Order = 0 },
            new() { Id = "i", Kind = FlowNodeKinds.If, Order = 1, Props = { ["condition"] = "true" } },
            new()
            {
                Id = "t",
                Kind = FlowNodeKinds.SetVar,
                ParentId = "i",
                Slot = FlowSlots.Then,
                Order = 0,
                Props = { ["name"] = "x", ["expr"] = "7" },
            },
            new()
            {
                Id = "f",
                Kind = FlowNodeKinds.SetVar,
                ParentId = "i",
                Slot = FlowSlots.Else,
                Order = 0,
                Props = { ["name"] = "x", ["expr"] = "0" },
            },
            new() { Id = "e", Kind = FlowNodeKinds.End, Order = 2 },
        };
        var doc = new FlowDocument
        {
            Variables = [new FlowVariable { Name = "x", Type = "number", Init = "0" }],
            Functions = [new FlowFunction { Name = "main", EntryNodeId = "s" }],
            Nodes = nodes,
            Edges = FlowComposite.BuildEdges(nodes),
        };

        var vars = new MDKOSS.Core.MVarStore();
        var interp = new FlowInterpreter(doc, "c", vars);
        interp.Reset();
        interp.Pump(64);
        Assert.Equal(FlowRunState.Completed, interp.State);
        Assert.Equal(7.0, vars.Get<double>("task.c.flow.var.x"));
    }
}
