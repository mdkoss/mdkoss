using MDKOSS.Core;
using MDKOSS.Core.Flow;
using MDKOSS.Tasks;

namespace MDKOSS.Tests.Core.Flow;

/// <summary>
/// Mirrors Flow editor UI ops on the document model: add nodes, connect ports, delete edge, apply → run.
/// (WPF UI itself is not automated; VM logic maps 1:1 onto these mutations.)
/// </summary>
public sealed class FlowEditorGraphOpsTests
{
    [Fact]
    public void Ui_ops_add_connect_replace_delete_validate()
    {
        // Toolbox: start / setVar / op.log / end
        var doc = new FlowDocument
        {
            Version = 1,
            Variables = [new FlowVariable { Name = "x", Type = "number", Init = "0" }],
            Functions = [new FlowFunction { Name = "main", EntryNodeId = "n-start" }],
            Nodes =
            [
                new FlowNode { Id = "n-start", Kind = FlowNodeKinds.Start, X = 80, Y = 120 },
                new FlowNode
                {
                    Id = "n-set",
                    Kind = FlowNodeKinds.SetVar,
                    X = 260,
                    Y = 120,
                    Props = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["name"] = "x",
                        ["expr"] = "x + 1",
                    },
                },
                new FlowNode
                {
                    Id = "n-log",
                    Kind = FlowNodeKinds.OpLog,
                    X = 440,
                    Y = 120,
                    Props = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["message"] = "\"tick \" + x",
                    },
                },
                new FlowNode { Id = "n-end", Kind = FlowNodeKinds.End, X = 620, Y = 120 },
            ],
            Edges = [],
        };

        // Port next → click target (Connect replaces same from+port)
        Connect(doc, "n-start", FlowPorts.Next, "n-set");
        Connect(doc, "n-set", FlowPorts.Next, "n-log");
        Connect(doc, "n-log", FlowPorts.Next, "n-end");
        Assert.Equal(3, doc.Edges.Count);

        Connect(doc, "n-start", FlowPorts.Next, "n-set"); // replace
        Assert.Equal(3, doc.Edges.Count);

        // Double-click edge delete
        var mid = doc.Edges.First(e => e.From == "n-set");
        doc.Edges.Remove(mid);
        Assert.Equal(2, doc.Edges.Count);
        Connect(doc, "n-set", FlowPorts.Next, "n-log");

        // if node ports (toolbox)
        doc.Nodes.Add(new FlowNode
        {
            Id = "n-if",
            Kind = FlowNodeKinds.If,
            X = 260,
            Y = 280,
            Props = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["condition"] = "x < 10",
            },
        });
        Connect(doc, "n-if", FlowPorts.True, "n-end");
        Connect(doc, "n-if", FlowPorts.False, "n-log");
        Assert.Contains(doc.Edges, e => e.Port == FlowPorts.True);
        Assert.Contains(doc.Edges, e => e.Port == FlowPorts.False);

        // Delete node + touching edges (UI RemoveSelected)
        RemoveNode(doc, "n-if");
        Assert.DoesNotContain(doc.Nodes, n => n.Id == "n-if");
        Assert.DoesNotContain(doc.Edges, e => e.From == "n-if" || e.To == "n-if");

        Assert.Empty(doc.Validate());

        var json = doc.ToJson();
        Assert.True(FlowDocument.TryParse(json, out var again, out var err), err);
        Assert.Equal(4, again.Nodes.Count);
        Assert.Equal(3, again.Edges.Count);
        Assert.Empty(again.Validate());
    }

    [Fact]
    public async Task Ui_apply_save_load_run()
    {
        var doc = new FlowDocument
        {
            Version = 1,
            Variables = [new FlowVariable { Name = "n", Type = "number", Init = "1" }],
            Functions = [new FlowFunction { Name = "main", EntryNodeId = "s" }],
            Nodes =
            [
                new FlowNode { Id = "s", Kind = FlowNodeKinds.Start, X = 40, Y = 40 },
                new FlowNode
                {
                    Id = "a",
                    Kind = FlowNodeKinds.SetVar,
                    X = 200,
                    Y = 40,
                    Props = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["name"] = "n",
                        ["expr"] = "n + 3",
                    },
                },
                new FlowNode { Id = "e", Kind = FlowNodeKinds.End, X = 360, Y = 40 },
            ],
            Edges =
            [
                new FlowEdge { From = "s", To = "a", Port = FlowPorts.Next },
                new FlowEdge { From = "a", To = "e", Port = FlowPorts.Next },
            ],
        };
        Assert.Empty(doc.Validate());

        // Apply → TaskConfig.parameters.flowJson → Save
        var setting = new MdkSetting
        {
            ProjectName = "flow-ui-graph",
            Tasks =
            [
                new MdkSetting.TaskConfig
                {
                    Name = "task-flow-ui",
                    Type = "flow",
                    IntervalMs = 50,
                    Parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["flowJson"] = doc.ToJson(),
                        ["loop"] = "false",
                    },
                },
            ],
        };

        var path = Path.Combine(Path.GetTempPath(), $"mdkoss-flow-ui-{Guid.NewGuid():N}.json");
        try
        {
            setting.Save(path);
            var loaded = MdkSetting.Load(path);
            var cfg = Assert.Single(loaded.Tasks);

            var vars = new MVarStore();
            var task = FlowTask.Create(cfg, vars);
            await task.ExecuteOnceAsync(CancellationToken.None);

            Assert.Equal(FlowRunState.Completed, task.FlowState);
            Assert.Equal(4.0, vars.Get<double>("task.task-flow-ui.flow.var.n"));
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    private static void Connect(FlowDocument doc, string from, string port, string to)
    {
        doc.Edges.RemoveAll(e =>
            string.Equals(e.From, from, StringComparison.OrdinalIgnoreCase)
            && string.Equals(e.Port, port, StringComparison.OrdinalIgnoreCase));
        doc.Edges.Add(new FlowEdge { From = from, To = to, Port = port });
    }

    private static void RemoveNode(FlowDocument doc, string id)
    {
        doc.Edges.RemoveAll(e =>
            string.Equals(e.From, id, StringComparison.OrdinalIgnoreCase)
            || string.Equals(e.To, id, StringComparison.OrdinalIgnoreCase));
        doc.Nodes.RemoveAll(n => string.Equals(n.Id, id, StringComparison.OrdinalIgnoreCase));
    }
}
