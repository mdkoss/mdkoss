using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using MDKOSS.Core.Flow;

namespace MDKOSS.Config.Wpf.Debug.Flow;

/// <summary>
/// Workflow-style editor model: top-to-bottom centered sequence with auto-wired edges
/// (similar to a C# Workflow Foundation Sequence).
/// </summary>
public sealed class FlowEditorVm : INotifyPropertyChanged
{
    public const double LayoutNodeWidth = 200;
    public const double LayoutNodeHeight = 64;
    public const double LayoutGapY = 48;
    public const double LayoutTop = 40;
    public const double LayoutCenterX = 400; // canvas center line; node left = center - width/2
    public const double LayoutBranchOffsetX = 220;

    private FlowNodeVm? _selected;
    private string _jsonPreview = string.Empty;
    private string _validationText = string.Empty;

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<FlowNodeVm> Nodes { get; } = [];
    public ObservableCollection<FlowEdgeVm> Edges { get; } = [];
    public ObservableCollection<FlowVarVm> Variables { get; } = [];
    public ObservableCollection<FlowFuncVm> Functions { get; } = [];

    public FlowNodeVm? Selected
    {
        get => _selected;
        set
        {
            _selected = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasSelection));
        }
    }

    public bool HasSelection => Selected is not null;

    public string JsonPreview
    {
        get => _jsonPreview;
        private set { _jsonPreview = value; OnPropertyChanged(); }
    }

    public string ValidationText
    {
        get => _validationText;
        private set { _validationText = value; OnPropertyChanged(); }
    }

    public void Load(FlowDocument doc)
    {
        Nodes.Clear();
        Edges.Clear();
        Variables.Clear();
        Functions.Clear();
        Selected = null;

        foreach (var v in doc.Variables)
        {
            Variables.Add(new FlowVarVm { Name = v.Name, Type = v.Type, Init = v.Init ?? "" });
        }

        foreach (var f in doc.Functions)
        {
            Functions.Add(new FlowFuncVm { Name = f.Name, EntryNodeId = f.EntryNodeId });
        }

        foreach (var n in doc.Nodes)
        {
            Nodes.Add(FlowNodeVm.FromModel(n));
        }

        foreach (var e in doc.Edges)
        {
            Edges.Add(new FlowEdgeVm { From = e.From, To = e.To, Port = e.Port });
        }

        if (Functions.Count == 0)
        {
            var start = Nodes.FirstOrDefault(n =>
                string.Equals(n.Kind, FlowNodeKinds.Start, StringComparison.OrdinalIgnoreCase));
            Functions.Add(new FlowFuncVm { Name = "main", EntryNodeId = start?.Id ?? "" });
        }

        EnsureStartEnd();
        RebuildSequenceFromEdges();
        RelayoutAndAutoWire();
        RefreshPreview();
    }

    public FlowDocument ToDocument()
    {
        return new FlowDocument
        {
            Version = 1,
            Variables = Variables.Select(v => new FlowVariable
            {
                Name = v.Name.Trim(),
                Type = string.IsNullOrWhiteSpace(v.Type) ? "number" : v.Type.Trim(),
                Init = v.Init,
            }).Where(v => !string.IsNullOrWhiteSpace(v.Name)).ToList(),
            Functions = Functions.Select(f => new FlowFunction
            {
                Name = string.IsNullOrWhiteSpace(f.Name) ? "main" : f.Name.Trim(),
                EntryNodeId = f.EntryNodeId.Trim(),
            }).ToList(),
            Nodes = Nodes.Select(n => n.ToModel()).ToList(),
            Edges = Edges.Select(e => new FlowEdge
            {
                From = e.From,
                To = e.To,
                Port = string.IsNullOrWhiteSpace(e.Port) ? FlowPorts.Next : e.Port,
            }).ToList(),
        };
    }

    public void RefreshPreview()
    {
        var doc = ToDocument();
        JsonPreview = doc.ToJson();
        var errors = doc.Validate();
        ValidationText = errors.Count == 0
            ? "校验通过"
            : string.Join(Environment.NewLine, errors.Select(e => "• " + e));
    }

    /// <summary>Insert activity after selected (or before end). Auto-wires and relayouts.</summary>
    public FlowNodeVm InsertNode(string kind)
    {
        EnsureStartEnd();
        var id = "n-" + Guid.NewGuid().ToString("N")[..8];
        var vm = new FlowNodeVm
        {
            Id = id,
            Kind = kind,
            Title = kind,
        };
        ApplyDefaultProps(vm);

        if (string.Equals(kind, FlowNodeKinds.End, StringComparison.OrdinalIgnoreCase))
        {
            var existingEnd = Nodes.FirstOrDefault(n =>
                string.Equals(n.Kind, FlowNodeKinds.End, StringComparison.OrdinalIgnoreCase));
            if (existingEnd is not null)
            {
                Selected = existingEnd;
                RelayoutAndAutoWire();
                RefreshPreview();
                return existingEnd;
            }
        }

        if (string.Equals(kind, FlowNodeKinds.Start, StringComparison.OrdinalIgnoreCase))
        {
            var existingStart = Nodes.FirstOrDefault(n =>
                string.Equals(n.Kind, FlowNodeKinds.Start, StringComparison.OrdinalIgnoreCase));
            if (existingStart is not null)
            {
                Selected = existingStart;
                RelayoutAndAutoWire();
                RefreshPreview();
                return existingStart;
            }
        }

        // Nodes collection order is the workflow sequence (not edges).
        var seq = Nodes.ToList();
        var endIdx = seq.FindIndex(n =>
            string.Equals(n.Kind, FlowNodeKinds.End, StringComparison.OrdinalIgnoreCase));
        var startIdx = seq.FindIndex(n =>
            string.Equals(n.Kind, FlowNodeKinds.Start, StringComparison.OrdinalIgnoreCase));

        var insertAt = endIdx >= 0 ? endIdx : seq.Count;
        if (Selected is not null)
        {
            var idx = seq.FindIndex(n => string.Equals(n.Id, Selected.Id, StringComparison.OrdinalIgnoreCase));
            if (idx >= 0)
            {
                insertAt = idx + 1;
            }
        }

        if (startIdx >= 0 && insertAt <= startIdx)
        {
            insertAt = startIdx + 1;
        }

        // Keep a single end as last
        if (endIdx >= 0)
        {
            // endIdx may shift if we haven't removed end; clamp so we never insert after end
            if (insertAt > endIdx)
            {
                insertAt = endIdx;
            }
        }

        if (insertAt < 0)
        {
            insertAt = 0;
        }

        if (insertAt > seq.Count)
        {
            insertAt = seq.Count;
        }

        seq.Insert(insertAt, vm);
        ApplySequenceOrder(seq);
        Selected = vm;
        RelayoutAndAutoWire();
        RefreshPreview();
        return vm;
    }

    public void RemoveSelected()
    {
        if (Selected is null)
        {
            return;
        }

        if (string.Equals(Selected.Kind, FlowNodeKinds.Start, StringComparison.OrdinalIgnoreCase)
            || string.Equals(Selected.Kind, FlowNodeKinds.End, StringComparison.OrdinalIgnoreCase))
        {
            return; // keep spine terminals
        }

        var id = Selected.Id;
        foreach (var e in Edges.Where(x =>
                     string.Equals(x.From, id, StringComparison.OrdinalIgnoreCase)
                     || string.Equals(x.To, id, StringComparison.OrdinalIgnoreCase)).ToList())
        {
            Edges.Remove(e);
        }

        Nodes.Remove(Selected);
        Selected = null;
        RelayoutAndAutoWire();
        RefreshPreview();
    }

    public bool MoveSelected(int delta)
    {
        if (Selected is null || delta == 0)
        {
            return false;
        }

        if (string.Equals(Selected.Kind, FlowNodeKinds.Start, StringComparison.OrdinalIgnoreCase)
            || string.Equals(Selected.Kind, FlowNodeKinds.End, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var seq = Nodes.ToList();
        var idx = seq.FindIndex(n => string.Equals(n.Id, Selected.Id, StringComparison.OrdinalIgnoreCase));
        if (idx < 0)
        {
            return false;
        }

        var target = idx + delta;
        var startIdx = seq.FindIndex(n =>
            string.Equals(n.Kind, FlowNodeKinds.Start, StringComparison.OrdinalIgnoreCase));
        var endIdx = seq.FindIndex(n =>
            string.Equals(n.Kind, FlowNodeKinds.End, StringComparison.OrdinalIgnoreCase));
        var min = startIdx >= 0 ? startIdx + 1 : 0;
        var max = endIdx >= 0 ? endIdx - 1 : seq.Count - 1;
        if (target < min || target > max)
        {
            return false;
        }

        (seq[idx], seq[target]) = (seq[target], seq[idx]);
        ApplySequenceOrder(seq);
        RelayoutAndAutoWire();
        RefreshPreview();
        return true;
    }

    public void RelayoutAndAutoWire()
    {
        EnsureStartEnd();
        // Editor sequence is Nodes order (Workflow Sequence), edges are derived.
        var seq = NormalizeSequence(Nodes.ToList());
        ApplySequenceOrder(seq);
        LayoutVerticalCentered(seq);
        AutoWireSequence(seq);
        SyncMainEntry();
    }

    /// <summary>
    /// On load: derive spine from edges once, then keep Nodes order authoritative.
    /// </summary>
    private void RebuildSequenceFromEdges()
    {
        var seq = WalkSequenceFromEdges();
        ApplySequenceOrder(NormalizeSequence(seq));
    }

    /// <summary>Main spine for UI: current Nodes order with start first / end last.</summary>
    public List<FlowNodeVm> GetMainSequence() => NormalizeSequence(Nodes.ToList());

    private List<FlowNodeVm> WalkSequenceFromEdges()
    {
        var byId = Nodes.ToDictionary(n => n.Id, StringComparer.OrdinalIgnoreCase);
        var start = Nodes.FirstOrDefault(n =>
            string.Equals(n.Kind, FlowNodeKinds.Start, StringComparison.OrdinalIgnoreCase));
        var ordered = new List<FlowNodeVm>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (start is not null)
        {
            var cur = start;
            while (cur is not null && seen.Add(cur.Id))
            {
                ordered.Add(cur);
                var nextId = Edges.FirstOrDefault(e =>
                    string.Equals(e.From, cur.Id, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(e.Port, FlowPorts.Next, StringComparison.OrdinalIgnoreCase))?.To;
                if (nextId is null)
                {
                    var kind = cur.Kind.Trim().ToLowerInvariant();
                    if (kind == "if")
                    {
                        nextId = Edges.FirstOrDefault(e =>
                            string.Equals(e.From, cur.Id, StringComparison.OrdinalIgnoreCase)
                            && string.Equals(e.Port, FlowPorts.False, StringComparison.OrdinalIgnoreCase))?.To;
                    }
                    else if (kind == "while")
                    {
                        nextId = Edges.FirstOrDefault(e =>
                            string.Equals(e.From, cur.Id, StringComparison.OrdinalIgnoreCase)
                            && string.Equals(e.Port, FlowPorts.Exit, StringComparison.OrdinalIgnoreCase))?.To;
                    }
                }

                cur = nextId is not null && byId.TryGetValue(nextId, out var n) ? n : null;
            }
        }

        foreach (var n in Nodes.OrderBy(x => x.Y).ThenBy(x => x.X))
        {
            if (seen.Add(n.Id))
            {
                ordered.Add(n);
            }
        }

        return ordered;
    }

    private static List<FlowNodeVm> NormalizeSequence(List<FlowNodeVm> seq)
    {
        var start = seq.FirstOrDefault(n =>
            string.Equals(n.Kind, FlowNodeKinds.Start, StringComparison.OrdinalIgnoreCase));
        var end = seq.LastOrDefault(n =>
            string.Equals(n.Kind, FlowNodeKinds.End, StringComparison.OrdinalIgnoreCase));
        var mid = seq
            .Where(n =>
                !string.Equals(n.Kind, FlowNodeKinds.Start, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(n.Kind, FlowNodeKinds.End, StringComparison.OrdinalIgnoreCase))
            .ToList();

        var result = new List<FlowNodeVm>();
        if (start is not null)
        {
            result.Add(start);
        }

        result.AddRange(mid);
        if (end is not null)
        {
            result.Add(end);
        }

        // any duplicates / extras
        foreach (var n in seq)
        {
            if (!result.Contains(n))
            {
                // keep before end if possible
                if (end is not null && result.Count > 0 && ReferenceEquals(result[^1], end))
                {
                    result.Insert(result.Count - 1, n);
                }
                else
                {
                    result.Add(n);
                }
            }
        }

        return result;
    }

    private void ApplySequenceOrder(List<FlowNodeVm> seq)
    {
        var seqIds = new HashSet<string>(seq.Select(n => n.Id), StringComparer.OrdinalIgnoreCase);
        var extras = Nodes.Where(n => !seqIds.Contains(n.Id)).ToList();
        Nodes.Clear();
        foreach (var n in seq)
        {
            Nodes.Add(n);
        }

        foreach (var n in extras)
        {
            Nodes.Add(n);
        }
    }

    private void LayoutVerticalCentered(List<FlowNodeVm> seq)
    {
        var left = LayoutCenterX - LayoutNodeWidth / 2;
        var y = LayoutTop;
        for (var i = 0; i < seq.Count; i++)
        {
            var node = seq[i];
            var kind = node.Kind.Trim().ToLowerInvariant();
            node.X = left;
            node.Y = y;

            // if/while: place branch stubs offset (visual only for targets that are not spine next)
            if (kind is "if" or "while")
            {
                y += LayoutNodeHeight + LayoutGapY;
                continue;
            }

            y += LayoutNodeHeight + LayoutGapY;
        }

        // Offset true/body targets slightly left, false/exit slightly right when they are not the spine successor
        foreach (var node in seq)
        {
            var kind = node.Kind.Trim().ToLowerInvariant();
            if (kind is not ("if" or "while"))
            {
                continue;
            }

            var leftPort = kind == "if" ? FlowPorts.True : FlowPorts.Body;
            var rightPort = kind == "if" ? FlowPorts.False : FlowPorts.Exit;
            OffsetBranchTarget(node.Id, leftPort, -LayoutBranchOffsetX);
            OffsetBranchTarget(node.Id, rightPort, LayoutBranchOffsetX);
        }
    }

    private void OffsetBranchTarget(string fromId, string port, double dx)
    {
        var edge = Edges.FirstOrDefault(e =>
            string.Equals(e.From, fromId, StringComparison.OrdinalIgnoreCase)
            && string.Equals(e.Port, port, StringComparison.OrdinalIgnoreCase));
        if (edge is null)
        {
            return;
        }

        var target = Nodes.FirstOrDefault(n =>
            string.Equals(n.Id, edge.To, StringComparison.OrdinalIgnoreCase));
        if (target is null)
        {
            return;
        }

        // Only offset if target is not also the linear next of a prior node in a simple way —
        // if true/false both point to same spine node, skip offset to keep centered.
        var siblingPort = string.Equals(port, FlowPorts.True, StringComparison.OrdinalIgnoreCase)
                          || string.Equals(port, FlowPorts.Body, StringComparison.OrdinalIgnoreCase)
            ? (string.Equals(port, FlowPorts.True, StringComparison.OrdinalIgnoreCase) ? FlowPorts.False : FlowPorts.Exit)
            : (string.Equals(port, FlowPorts.False, StringComparison.OrdinalIgnoreCase) ? FlowPorts.True : FlowPorts.Body);
        var other = Edges.FirstOrDefault(e =>
            string.Equals(e.From, fromId, StringComparison.OrdinalIgnoreCase)
            && string.Equals(e.Port, siblingPort, StringComparison.OrdinalIgnoreCase));
        if (other is not null && string.Equals(other.To, edge.To, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        target.X = LayoutCenterX - LayoutNodeWidth / 2 + dx;
    }

    private void AutoWireSequence(List<FlowNodeVm> seq)
    {
        // Rebuild spine next links; branch ports for if/while default to following spine node.
        var keepBranches = Edges
            .Where(e =>
            {
                var port = (e.Port ?? "").Trim().ToLowerInvariant();
                return port is "true" or "false" or "body" or "exit";
            })
            .Select(e => new FlowEdgeVm { From = e.From, To = e.To, Port = e.Port })
            .ToList();

        Edges.Clear();

        for (var i = 0; i < seq.Count - 1; i++)
        {
            var from = seq[i];
            var to = seq[i + 1];
            var kind = from.Kind.Trim().ToLowerInvariant();
            if (kind == "if")
            {
                Wire(from.Id, FlowPorts.True, ResolveBranchTarget(keepBranches, from.Id, FlowPorts.True, to.Id));
                Wire(from.Id, FlowPorts.False, ResolveBranchTarget(keepBranches, from.Id, FlowPorts.False, to.Id));
            }
            else if (kind == "while")
            {
                Wire(from.Id, FlowPorts.Body, ResolveBranchTarget(keepBranches, from.Id, FlowPorts.Body, to.Id));
                Wire(from.Id, FlowPorts.Exit, ResolveBranchTarget(keepBranches, from.Id, FlowPorts.Exit, to.Id));
            }
            else if (kind != "end")
            {
                Wire(from.Id, FlowPorts.Next, to.Id);
            }
        }
    }

    private static string ResolveBranchTarget(
        List<FlowEdgeVm> previous,
        string fromId,
        string port,
        string defaultTo)
    {
        var old = previous.FirstOrDefault(e =>
            string.Equals(e.From, fromId, StringComparison.OrdinalIgnoreCase)
            && string.Equals(e.Port, port, StringComparison.OrdinalIgnoreCase));
        return old?.To ?? defaultTo;
    }

    private void Wire(string from, string port, string to)
    {
        Edges.Add(new FlowEdgeVm { From = from, To = to, Port = port });
    }

    private void EnsureStartEnd()
    {
        if (!Nodes.Any(n => string.Equals(n.Kind, FlowNodeKinds.Start, StringComparison.OrdinalIgnoreCase)))
        {
            Nodes.Insert(0, new FlowNodeVm
            {
                Id = "n-start",
                Kind = FlowNodeKinds.Start,
                Title = FlowNodeKinds.Start,
            });
        }

        if (!Nodes.Any(n => string.Equals(n.Kind, FlowNodeKinds.End, StringComparison.OrdinalIgnoreCase)))
        {
            Nodes.Add(new FlowNodeVm
            {
                Id = "n-end",
                Kind = FlowNodeKinds.End,
                Title = FlowNodeKinds.End,
            });
        }

        SyncMainEntry();
    }

    private void SyncMainEntry()
    {
        var start = Nodes.FirstOrDefault(n =>
            string.Equals(n.Kind, FlowNodeKinds.Start, StringComparison.OrdinalIgnoreCase));
        var main = Functions.FirstOrDefault(f =>
            string.Equals(f.Name, "main", StringComparison.OrdinalIgnoreCase));
        if (main is null)
        {
            Functions.Add(new FlowFuncVm { Name = "main", EntryNodeId = start?.Id ?? "" });
        }
        else if (start is not null)
        {
            main.EntryNodeId = start.Id;
        }
    }

    private static void ApplyDefaultProps(FlowNodeVm vm)
    {
        switch (vm.Kind.Trim().ToLowerInvariant())
        {
            case "declarevar":
                vm.SetProp("name", "x");
                vm.SetProp("type", "number");
                vm.SetProp("init", "0");
                break;
            case "setvar":
                vm.SetProp("name", "x");
                vm.SetProp("expr", "x + 1");
                break;
            case "if":
                vm.SetProp("condition", "x < 10");
                break;
            case "while":
                vm.SetProp("condition", "x < 10");
                break;
            case "delay":
                vm.SetProp("ms", "100");
                break;
            case "call":
                vm.SetProp("function", "main");
                break;
            case "op.writeio":
                vm.SetProp("deviceId", "gpio-main");
                vm.SetProp("alias", "out.tower.green");
                vm.SetProp("value", "true");
                break;
            case "op.deviceaction":
                vm.SetProp("deviceId", "dev-axis");
                vm.SetProp("action", "enable");
                vm.SetProp("parametersJson", "{}");
                break;
            case "op.log":
                vm.SetProp("message", "\"hello\"");
                break;
            case "motion.axismoveto":
                vm.SetProp("deviceId", "axis-x");
                vm.SetProp("position", "0");
                break;
            case "motion.axisenable":
                vm.SetProp("deviceId", "axis-x");
                vm.SetProp("enabled", "true");
                break;
            case "motion.platformsetmotion":
                vm.SetProp("deviceId", "platform-main");
                vm.SetProp("enabled", "true");
                break;
            case "motion.platformstart":
            case "motion.platformstop":
            case "motion.ensuredriver":
                vm.SetProp("deviceId", "platform-main");
                break;
            case "motion.platformaxismoveto":
                vm.SetProp("deviceId", "platform-main");
                vm.SetProp("axis", "X");
                vm.SetProp("position", "0");
                break;
            case "motion.gpiowrite":
                vm.SetProp("deviceId", "gpio-main");
                vm.SetProp("alias", "out.tower.green");
                vm.SetProp("value", "true");
                break;
            case "motion.gpioread":
                vm.SetProp("deviceId", "gpio-main");
                vm.SetProp("alias", "in.start");
                vm.SetProp("name", "io");
                break;
            case "motion.devicesnapshot":
                vm.SetProp("deviceId", "axis-x");
                vm.SetProp("prefix", "snap");
                break;
            case "motion.setparam":
                vm.SetProp("key", "target");
                vm.SetProp("expr", "0");
                break;
            case "motion.getparam":
                vm.SetProp("key", "target");
                vm.SetProp("name", "x");
                break;
            case "motion.settaskvar":
                vm.SetProp("key", "alive");
                vm.SetProp("expr", "true");
                break;
            case "motion.setglobalvar":
                vm.SetProp("key", "machine.mode");
                vm.SetProp("expr", "\"AUTO\"");
                break;
        }
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed class FlowNodeVm : INotifyPropertyChanged
{
    private string _id = "";
    private string _kind = FlowNodeKinds.Start;
    private string _title = "";
    private double _x;
    private double _y;
    private bool _isSelected;

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Id { get => _id; set { _id = value; OnPropertyChanged(); } }
    public string Kind { get => _kind; set { _kind = value; Title = value; OnPropertyChanged(); } }
    public string Title { get => _title; set { _title = value; OnPropertyChanged(); } }
    public double X { get => _x; set { _x = value; OnPropertyChanged(); } }
    public double Y { get => _y; set { _y = value; OnPropertyChanged(); } }
    public bool IsSelected
    {
        get => _isSelected;
        set { _isSelected = value; OnPropertyChanged(); OnPropertyChanged(nameof(BorderBrush)); }
    }

    public Brush BorderBrush => IsSelected
        ? new SolidColorBrush(Color.FromRgb(0x0B, 0x6E, 0x4F))
        : new SolidColorBrush(Color.FromRgb(0xD0, 0xD7, 0xDE));

    public ObservableCollection<KvPairRow> Props { get; } = [];

    public IReadOnlyList<string> OutputPorts => Kind.Trim().ToLowerInvariant() switch
    {
        "if" => [FlowPorts.True, FlowPorts.False],
        "while" => [FlowPorts.Body, FlowPorts.Exit],
        "end" => [],
        _ => [FlowPorts.Next],
    };

    public static FlowNodeVm FromModel(FlowNode n)
    {
        var vm = new FlowNodeVm
        {
            Id = n.Id,
            Kind = n.Kind,
            Title = n.Kind,
            X = n.X,
            Y = n.Y,
        };
        foreach (var kv in n.Props.OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase))
        {
            vm.Props.Add(new KvPairRow { Key = kv.Key, Value = kv.Value });
        }

        return vm;
    }

    public FlowNode ToModel() => new()
    {
        Id = Id,
        Kind = Kind,
        X = X,
        Y = Y,
        Props = Props
            .Where(p => !string.IsNullOrWhiteSpace(p.Key))
            .ToDictionary(p => p.Key.Trim(), p => p.Value ?? "", StringComparer.OrdinalIgnoreCase),
    };

    public void SetProp(string key, string value)
    {
        var row = Props.FirstOrDefault(p => string.Equals(p.Key, key, StringComparison.OrdinalIgnoreCase));
        if (row is null)
        {
            Props.Add(new KvPairRow { Key = key, Value = value });
        }
        else
        {
            row.Value = value;
        }
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed class FlowEdgeVm : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    public string From { get; set; } = "";
    public string To { get; set; } = "";
    public string Port { get; set; } = FlowPorts.Next;

    private PathGeometry? _geometry;
    public PathGeometry? Geometry
    {
        get => _geometry;
        set { _geometry = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Geometry))); }
    }

    public string Label => Port;
}

public sealed class FlowVarVm : INotifyPropertyChanged
{
    private string _name = "";
    private string _type = "number";
    private string _init = "0";
    public event PropertyChangedEventHandler? PropertyChanged;
    public string Name { get => _name; set { _name = value; PropertyChanged?.Invoke(this, new(nameof(Name))); } }
    public string Type { get => _type; set { _type = value; PropertyChanged?.Invoke(this, new(nameof(Type))); } }
    public string Init { get => _init; set { _init = value; PropertyChanged?.Invoke(this, new(nameof(Init))); } }
}

public sealed class FlowFuncVm : INotifyPropertyChanged
{
    private string _name = "main";
    private string _entry = "";
    public event PropertyChangedEventHandler? PropertyChanged;
    public string Name { get => _name; set { _name = value; PropertyChanged?.Invoke(this, new(nameof(Name))); } }
    public string EntryNodeId { get => _entry; set { _entry = value; PropertyChanged?.Invoke(this, new(nameof(EntryNodeId))); } }
}
