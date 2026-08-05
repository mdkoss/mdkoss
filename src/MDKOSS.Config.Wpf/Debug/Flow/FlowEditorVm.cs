using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;
using MDKOSS.Core.Flow;

namespace MDKOSS.Config.Wpf.Debug.Flow;

public sealed class FlowEditorVm : INotifyPropertyChanged
{
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
            var start = Nodes.FirstOrDefault(n => n.Kind == FlowNodeKinds.Start);
            Functions.Add(new FlowFuncVm { Name = "main", EntryNodeId = start?.Id ?? "" });
        }

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

    public FlowNodeVm AddNode(string kind, Point at)
    {
        var id = "n-" + Guid.NewGuid().ToString("N")[..8];
        var vm = new FlowNodeVm
        {
            Id = id,
            Kind = kind,
            X = at.X,
            Y = at.Y,
            Title = kind,
        };
        ApplyDefaultProps(vm);
        Nodes.Add(vm);
        Selected = vm;
        RefreshPreview();
        return vm;
    }

    public void RemoveSelected()
    {
        if (Selected is null)
        {
            return;
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
        RefreshPreview();
    }

    public void Connect(string fromId, string port, string toId)
    {
        // replace existing edge on same from+port
        foreach (var e in Edges.Where(x =>
                     string.Equals(x.From, fromId, StringComparison.OrdinalIgnoreCase)
                     && string.Equals(x.Port, port, StringComparison.OrdinalIgnoreCase)).ToList())
        {
            Edges.Remove(e);
        }

        Edges.Add(new FlowEdgeVm { From = fromId, To = toId, Port = port });
        RefreshPreview();
    }

    private static void ApplyDefaultProps(FlowNodeVm vm)
    {
        switch (vm.Kind.ToLowerInvariant())
        {
            case FlowNodeKinds.DeclareVar:
                vm.SetProp("name", "x");
                vm.SetProp("type", "number");
                vm.SetProp("init", "0");
                break;
            case FlowNodeKinds.SetVar:
                vm.SetProp("name", "x");
                vm.SetProp("expr", "x + 1");
                break;
            case FlowNodeKinds.If:
                vm.SetProp("condition", "x < 10");
                break;
            case FlowNodeKinds.While:
                vm.SetProp("condition", "x < 10");
                break;
            case FlowNodeKinds.Delay:
                vm.SetProp("ms", "100");
                break;
            case FlowNodeKinds.Call:
                vm.SetProp("function", "main");
                break;
            case FlowNodeKinds.OpWriteIo:
                vm.SetProp("deviceId", "gpio-main");
                vm.SetProp("alias", "out.tower.green");
                vm.SetProp("value", "true");
                break;
            case FlowNodeKinds.OpDeviceAction:
                vm.SetProp("deviceId", "dev-axis");
                vm.SetProp("action", "enable");
                vm.SetProp("parametersJson", "{}");
                break;
            case FlowNodeKinds.OpLog:
                vm.SetProp("message", "\"hello\"");
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

    public IReadOnlyList<string> OutputPorts => Kind.ToLowerInvariant() switch
    {
        FlowNodeKinds.If => [FlowPorts.True, FlowPorts.False],
        FlowNodeKinds.While => [FlowPorts.Body, FlowPorts.Exit],
        FlowNodeKinds.End => [],
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

    // geometry updated by window
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
