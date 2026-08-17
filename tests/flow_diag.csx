using MDKOSS.Core;
using MDKOSS.Core.Flow;
var doc = new FlowDocument {
  Version = 1,
  Variables = [new FlowVariable { Name = "x", Type = "number", Init = "0" }],
  Functions = [new FlowFunction { Name = "main", EntryNodeId = "s" }],
  Nodes = [
    new FlowNode { Id = "s", Kind = FlowNodeKinds.Start },
    new FlowNode { Id = "a", Kind = FlowNodeKinds.SetVar, Props = new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase){["name"]="x",["expr"]="x + 2"} },
    new FlowNode { Id = "e", Kind = FlowNodeKinds.End },
  ],
  Edges = [
    new FlowEdge { From = "s", To = "a", Port = FlowPorts.Next },
    new FlowEdge { From = "a", To = "e", Port = FlowPorts.Next },
  ],
};
Console.WriteLine("validate: " + string.Join("; ", doc.Validate()));
var vars = new MVarStore();
var interp = new FlowInterpreter(doc, "demo", vars);
interp.Reset();
interp.Pump(64);
Console.WriteLine("state=" + interp.State + " err=" + interp.LastError + " pc=" + interp.ProgramCounter);
Console.WriteLine("x=" + vars.Get<double>("task.demo.flow.var.x"));
