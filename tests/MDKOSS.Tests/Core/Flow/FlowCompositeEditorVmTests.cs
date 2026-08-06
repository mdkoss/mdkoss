using MDKOSS.Config.Wpf.Debug.Flow;
using MDKOSS.Core.Flow;

namespace MDKOSS.Tests.Core.Flow;

/// <summary>Composite-block editor VM: insert into then/else/body, collapse, edges from tree.</summary>
public sealed class FlowCompositeEditorVmTests
{
    [Fact]
    public void Insert_if_focuses_then_and_builds_branch_edges()
    {
        var vm = new FlowEditorVm();
        vm.Load(FlowDocument.CreateEmpty());
        var set = vm.InsertNode(FlowNodeKinds.SetVar);
        Assert.Equal("setVar", set.Kind);

        var iff = vm.InsertNode(FlowNodeKinds.If);
        Assert.Equal(FlowSlots.Then, vm.FocusSlot);
        Assert.Equal(iff.Id, vm.FocusParentId);

        var thenLog = vm.InsertNode(FlowNodeKinds.OpLog);
        Assert.Equal(iff.Id, thenLog.ParentId);
        Assert.Equal(FlowSlots.Then, thenLog.Slot);

        vm.FocusSlotOfSelected(FlowSlots.Else); // need Selected=if
        vm.Selected = iff;
        vm.FocusSlotOfSelected(FlowSlots.Else);
        var elseLog = vm.InsertNode(FlowNodeKinds.OpLog);
        Assert.Equal(FlowSlots.Else, elseLog.Slot);

        var doc = vm.ToDocument();
        Assert.Empty(doc.Validate());
        Assert.Contains(doc.Edges, e => e.Port == FlowPorts.True && e.To == thenLog.Id);
        Assert.Contains(doc.Edges, e => e.Port == FlowPorts.False && e.To == elseLog.Id);
    }

    [Fact]
    public void Insert_while_creates_body_placeholder()
    {
        var vm = new FlowEditorVm();
        vm.Load(FlowDocument.CreateEmpty());
        var w = vm.InsertNode(FlowNodeKinds.While);
        Assert.Equal(FlowSlots.Body, vm.FocusSlot);
        var body = vm.Nodes.Where(n =>
            string.Equals(n.ParentId, w.Id, StringComparison.OrdinalIgnoreCase)).ToList();
        Assert.Single(body);
        Assert.Equal(FlowNodeKinds.Delay, body[0].Kind);

        var doc = vm.ToDocument();
        Assert.Empty(doc.Validate());
        Assert.Contains(doc.Edges, e => e is { From: var f, Port: "body" } && f == w.Id);
        Assert.Contains(doc.Edges, e => e.Port == FlowPorts.Exit);
    }

    [Fact]
    public void Remove_composite_removes_subtree()
    {
        var vm = new FlowEditorVm();
        vm.Load(FlowDocument.CreateEmpty());
        var iff = vm.InsertNode(FlowNodeKinds.If);
        vm.InsertNode(FlowNodeKinds.OpLog);
        var before = vm.Nodes.Count;
        vm.Selected = iff;
        vm.RemoveSelected();
        Assert.True(vm.Nodes.Count < before);
        Assert.DoesNotContain(vm.Nodes, n => string.Equals(n.Id, iff.Id, StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(vm.Nodes, n =>
            string.Equals(n.ParentId, iff.Id, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Collapse_hides_children_in_regions_only()
    {
        var vm = new FlowEditorVm();
        vm.Load(FlowDocument.CreateEmpty());
        var iff = vm.InsertNode(FlowNodeKinds.If);
        vm.InsertNode(FlowNodeKinds.SetVar);
        Assert.NotEmpty(vm.Regions);
        vm.Selected = iff;
        vm.ToggleCollapseSelected();
        Assert.True(iff.IsCollapsed);
        Assert.Empty(vm.Regions);
    }
}
