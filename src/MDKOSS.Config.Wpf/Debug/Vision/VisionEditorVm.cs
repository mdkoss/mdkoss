using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using MDKOSS.Core.Vision;

namespace MDKOSS.Config.Wpf.Debug.Vision;

/// <summary>Linear vision pipeline editor VM (start → ops → end).</summary>
public sealed class VisionEditorVm : INotifyPropertyChanged
{
    public const double LayoutNodeWidth = 200;
    public const double LayoutNodeHeight = 56;
    public const double LayoutGapY = 28;
    public const double LayoutCenterX = 340;

    private VisionNodeVm? _selected;
    private string _validationText = "";
    private string _jsonPreview = "";
    private string _cameraDeviceId = "";
    private string _algorithm = VisionAlgorithmRegistry.DefaultId;
    private string? _previewImagePath;
    private int _previewImageWidth;
    private int _previewImageHeight;
    private List<VisionNodeTrace> _traces = [];

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<VisionNodeVm> Nodes { get; } = [];
    public ObservableCollection<VisionEdgeVm> Edges { get; } = [];

    public string CameraDeviceId
    {
        get => _cameraDeviceId;
        set { if (_cameraDeviceId == value) return; _cameraDeviceId = value; OnPropertyChanged(); }
    }

    public string Algorithm
    {
        get => _algorithm;
        set
        {
            var next = string.IsNullOrWhiteSpace(value) ? VisionAlgorithmRegistry.DefaultId : value.Trim();
            if (string.Equals(_algorithm, next, StringComparison.OrdinalIgnoreCase)) return;
            _algorithm = next;
            OnPropertyChanged();
            RefreshPreview();
        }
    }

    /// <summary>Last try-run / preview image path shown in the result panel.</summary>
    public string? PreviewImagePath
    {
        get => _previewImagePath;
        private set { if (_previewImagePath == value) return; _previewImagePath = value; OnPropertyChanged(); }
    }

    public int PreviewImageWidth
    {
        get => _previewImageWidth;
        private set { if (_previewImageWidth == value) return; _previewImageWidth = value; OnPropertyChanged(); }
    }

    public int PreviewImageHeight
    {
        get => _previewImageHeight;
        private set { if (_previewImageHeight == value) return; _previewImageHeight = value; OnPropertyChanged(); }
    }

    public string ValidationText
    {
        get => _validationText;
        private set { if (_validationText == value) return; _validationText = value; OnPropertyChanged(); }
    }

    public string JsonPreview
    {
        get => _jsonPreview;
        private set { if (_jsonPreview == value) return; _jsonPreview = value; OnPropertyChanged(); }
    }

    public VisionNodeVm? Selected
    {
        get => _selected;
        set
        {
            if (_selected is not null)
            {
                _selected.IsSelected = false;
            }

            _selected = value;
            if (_selected is not null)
            {
                _selected.IsSelected = true;
            }

            OnPropertyChanged();
            OnPropertyChanged(nameof(SelectedTrace));
            OnPropertyChanged(nameof(SelectedTraceSummary));
        }
    }

    public VisionNodeTrace? SelectedTrace =>
        Selected is null
            ? null
            : _traces.FirstOrDefault(t => string.Equals(t.NodeId, Selected.Id, StringComparison.OrdinalIgnoreCase));

    public string SelectedTraceSummary
    {
        get
        {
            var t = SelectedTrace;
            if (t is null)
            {
                return _traces.Count == 0
                    ? "试运行后选择节点可查看输入图、输出图与输出变量"
                    : "该节点无试运行快照";
            }

            var vars = t.OutputVars.Count == 0
                ? "(无输出变量)"
                : string.Join("  ", t.OutputVars.Select(kv => $"{kv.Key}={kv.Value}"));
            return $"输入 {t.InputWidth}x{t.InputHeight}  输出 {t.OutputWidth}x{t.OutputHeight}\n{vars}";
        }
    }

    public void ApplyRunResult(VisionRunResult result)
    {
        _traces = result.NodeTraces ?? [];
        OnPropertyChanged(nameof(SelectedTrace));
        OnPropertyChanged(nameof(SelectedTraceSummary));
    }

    public void Load(VisionDocument doc, string? cameraDeviceId = null)
    {
        ArgumentNullException.ThrowIfNull(doc);
        doc.EnsureDataflow();
        Nodes.Clear();
        Edges.Clear();
        CameraDeviceId = cameraDeviceId ?? "";
        Algorithm = string.IsNullOrWhiteSpace(doc.Algorithm)
            ? VisionAlgorithmRegistry.DefaultId
            : doc.Algorithm.Trim();
        _traces = [];
        foreach (var n in doc.Nodes.OrderBy(x => x.Order).ThenBy(x => x.Id, StringComparer.OrdinalIgnoreCase))
        {
            Nodes.Add(VisionNodeVm.FromModel(n));
        }

        EnsureStartEnd();
        if (doc.HasDataEdges())
        {
            foreach (var e in doc.Edges)
            {
                Edges.Add(VisionEdgeVm.FromModel(e));
            }
        }
        else
        {
            RelayoutAndAutoWire();
        }

        RefreshPreview();
        OnPropertyChanged(nameof(SelectedTrace));
        OnPropertyChanged(nameof(SelectedTraceSummary));
    }

    public VisionDocument ToDocument()
    {
        var nodes = Nodes.Select(n => n.ToModel()).ToList();
        for (var i = 0; i < nodes.Count; i++)
        {
            nodes[i].Order = i;
        }

        var doc = new VisionDocument
        {
            Version = VisionVersions.Dataflow,
            Algorithm = string.IsNullOrWhiteSpace(Algorithm)
                ? VisionAlgorithmRegistry.DefaultId
                : Algorithm.Trim(),
            Nodes = nodes,
            Edges = Edges.Select(e => e.ToModel()).ToList(),
        };
        if (!doc.HasDataEdges())
        {
            doc.RebuildLinearEdges();
        }
        else
        {
            doc.EnsureDataflow();
        }

        return doc;
    }

    public void SetPreviewImage(string? path, int width = 0, int height = 0)
    {
        PreviewImagePath = path;
        PreviewImageWidth = Math.Max(0, width);
        PreviewImageHeight = Math.Max(0, height);
    }

    public VisionNodeVm? FindRoiNode() =>
        Selected is not null
        && string.Equals(Selected.Kind, VisionNodeKinds.Roi, StringComparison.OrdinalIgnoreCase)
            ? Selected
            : Nodes.FirstOrDefault(n =>
                string.Equals(n.Kind, VisionNodeKinds.Roi, StringComparison.OrdinalIgnoreCase));

    public void ApplyRoiRect(VisionRoiRect rect)
    {
        var node = FindRoiNode();
        if (node is null)
        {
            node = InsertNode(VisionNodeKinds.Roi);
        }

        var clamped = PreviewImageWidth > 0 && PreviewImageHeight > 0
            ? rect.ClampToImage(PreviewImageWidth, PreviewImageHeight)
            : rect.Normalize();
        node.SetProp("x", clamped.X.ToString());
        node.SetProp("y", clamped.Y.ToString());
        node.SetProp("w", clamped.W.ToString());
        node.SetProp("h", clamped.H.ToString());
        Selected = node;
        RefreshPreview();
    }

    public VisionRoiRect? TryGetRoiRect()
    {
        var node = FindRoiNode();
        if (node is null)
        {
            return null;
        }

        var props = node.Props
            .Where(p => !string.IsNullOrWhiteSpace(p.Key))
            .ToDictionary(p => p.Key.Trim(), p => p.Value ?? "", StringComparer.OrdinalIgnoreCase);
        var rect = VisionRoiRect.FromProps(props);
        return PreviewImageWidth > 0 && PreviewImageHeight > 0
            ? rect.ClampToImage(PreviewImageWidth, PreviewImageHeight)
            : rect.Normalize();
    }

    public VisionNodeVm InsertNode(string kind)
    {
        EnsureStartEnd();
        if (VisionNodeKinds.IsTerminal(kind))
        {
            var existing = Nodes.FirstOrDefault(n =>
                string.Equals(n.Kind, kind, StringComparison.OrdinalIgnoreCase));
            if (existing is not null)
            {
                Selected = existing;
                RelayoutAndAutoWire();
                RefreshPreview();
                return existing;
            }
        }

        var vm = new VisionNodeVm
        {
            Id = "n-" + Guid.NewGuid().ToString("N")[..8],
            Kind = kind,
            Title = kind,
        };
        ApplyDefaultProps(vm);

        var siblings = Nodes.OrderBy(n => n.Order).ToList();
        var insertAt = siblings.Count;
        if (Selected is not null)
        {
            var idx = siblings.FindIndex(n => string.Equals(n.Id, Selected.Id, StringComparison.OrdinalIgnoreCase));
            if (idx >= 0)
            {
                insertAt = idx + 1;
            }
        }

        var startIdx = siblings.FindIndex(n =>
            string.Equals(n.Kind, VisionNodeKinds.Start, StringComparison.OrdinalIgnoreCase));
        var endIdx = siblings.FindIndex(n =>
            string.Equals(n.Kind, VisionNodeKinds.End, StringComparison.OrdinalIgnoreCase));
        if (startIdx >= 0 && insertAt <= startIdx)
        {
            insertAt = startIdx + 1;
        }

        if (endIdx >= 0 && insertAt > endIdx)
        {
            insertAt = endIdx;
        }

        siblings.Insert(Math.Clamp(insertAt, 0, siblings.Count), vm);
        Nodes.Clear();
        for (var i = 0; i < siblings.Count; i++)
        {
            siblings[i].Order = i;
            Nodes.Add(siblings[i]);
        }

        Selected = vm;
        RelayoutAndAutoWire();
        RefreshPreview();
        return vm;
    }

    public void RemoveSelected()
    {
        if (Selected is null || VisionNodeKinds.IsTerminal(Selected.Kind))
        {
            return;
        }

        Nodes.Remove(Selected);
        Selected = null;
        RelayoutAndAutoWire();
        RefreshPreview();
    }

    public bool MoveSelected(int delta)
    {
        if (Selected is null || delta == 0 || VisionNodeKinds.IsTerminal(Selected.Kind))
        {
            return false;
        }

        var siblings = Nodes.OrderBy(n => n.Order).ToList();
        var idx = siblings.FindIndex(n => string.Equals(n.Id, Selected.Id, StringComparison.OrdinalIgnoreCase));
        if (idx < 0)
        {
            return false;
        }

        var startIdx = siblings.FindIndex(n =>
            string.Equals(n.Kind, VisionNodeKinds.Start, StringComparison.OrdinalIgnoreCase));
        var endIdx = siblings.FindIndex(n =>
            string.Equals(n.Kind, VisionNodeKinds.End, StringComparison.OrdinalIgnoreCase));
        var min = startIdx >= 0 ? startIdx + 1 : 0;
        var max = endIdx >= 0 ? endIdx - 1 : siblings.Count - 1;
        var target = Math.Clamp(idx + delta, min, max);
        if (target == idx)
        {
            return false;
        }

        siblings.RemoveAt(idx);
        siblings.Insert(target, Selected);
        Nodes.Clear();
        for (var i = 0; i < siblings.Count; i++)
        {
            siblings[i].Order = i;
            Nodes.Add(siblings[i]);
        }

        RelayoutAndAutoWire();
        RefreshPreview();
        return true;
    }

    public void RelayoutAndAutoWire()
    {
        EnsureStartEnd();
        var ordered = Nodes.OrderBy(n => n.Order).ThenBy(n => n.Id, StringComparer.OrdinalIgnoreCase).ToList();
        for (var i = 0; i < ordered.Count; i++)
        {
            ordered[i].Order = i;
            ordered[i].X = LayoutCenterX - LayoutNodeWidth / 2;
            ordered[i].Y = 40 + i * (LayoutNodeHeight + LayoutGapY);
        }

        Edges.Clear();
        for (var i = 0; i < ordered.Count - 1; i++)
        {
            Edges.Add(new VisionEdgeVm
            {
                From = ordered[i].Id,
                To = ordered[i + 1].Id,
                Port = VisionPorts.Next,
            });
        }
    }

    public void RefreshPreview()
    {
        var doc = ToDocument();
        JsonPreview = doc.ToJson();
        var errors = doc.Validate();
        ValidationText = errors.Count == 0 ? "OK" : string.Join(Environment.NewLine, errors);
    }

    public void SetStatusText(string text) => ValidationText = text ?? "";

    public IReadOnlyList<string> Validate() => ToDocument().Validate();

    private void EnsureStartEnd()
    {
        if (!Nodes.Any(n => string.Equals(n.Kind, VisionNodeKinds.Start, StringComparison.OrdinalIgnoreCase)))
        {
            Nodes.Insert(0, new VisionNodeVm
            {
                Id = "n-start",
                Kind = VisionNodeKinds.Start,
                Title = VisionNodeKinds.Start,
                Order = 0,
            });
        }

        if (!Nodes.Any(n => string.Equals(n.Kind, VisionNodeKinds.End, StringComparison.OrdinalIgnoreCase)))
        {
            Nodes.Add(new VisionNodeVm
            {
                Id = "n-end",
                Kind = VisionNodeKinds.End,
                Title = VisionNodeKinds.End,
                Order = Nodes.Count,
            });
        }
    }

    private static void ApplyDefaultProps(VisionNodeVm vm)
    {
        switch (vm.Kind.Trim().ToLowerInvariant())
        {
            case "vision.loadimage":
                vm.SetProp("path", "");
                break;
            case "vision.threshold":
                vm.SetProp("mode", "binary");
                vm.SetProp("thresh", "128");
                vm.SetProp("maxVal", "255");
                break;
            case "vision.blur":
                vm.SetProp("kind", "gaussian");
                vm.SetProp("ksize", "5");
                break;
            case "vision.morphology":
                vm.SetProp("op", "open");
                vm.SetProp("ksize", "3");
                vm.SetProp("iterations", "1");
                break;
            case "vision.roi":
                vm.SetProp("x", "0");
                vm.SetProp("y", "0");
                vm.SetProp("w", "200");
                vm.SetProp("h", "200");
                break;
            case "vision.templatematch":
                vm.SetProp("templatePath", "");
                vm.SetProp("minScore", "0.7");
                break;
            case "vision.findcontours":
                vm.SetProp("thresh", "128");
                vm.SetProp("minArea", "50");
                break;
            case "vision.findcircles":
                vm.SetProp("minDist", "20");
                vm.SetProp("minRadius", "5");
                vm.SetProp("maxRadius", "0");
                break;
            case "vision.findlines":
                vm.SetProp("threshold", "50");
                vm.SetProp("minLength", "30");
                break;
            case "vision.outputpose":
                vm.SetProp("prefix", "vision");
                vm.SetProp("requireOk", "false");
                break;
        }
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed class VisionNodeVm : INotifyPropertyChanged
{
    private string _id = "";
    private string _kind = VisionNodeKinds.Start;
    private string _title = "";
    private double _x;
    private double _y;
    private int _order;
    private bool _isSelected;

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Id { get => _id; set { _id = value; OnPropertyChanged(); } }
    public string Kind { get => _kind; set { _kind = value; Title = value; OnPropertyChanged(); } }
    public string Title { get => _title; set { _title = value; OnPropertyChanged(); } }
    public double X { get => _x; set { _x = value; OnPropertyChanged(); } }
    public double Y { get => _y; set { _y = value; OnPropertyChanged(); } }
    public int Order { get => _order; set { _order = value; OnPropertyChanged(); } }

    public bool IsSelected
    {
        get => _isSelected;
        set { _isSelected = value; OnPropertyChanged(); OnPropertyChanged(nameof(BorderBrush)); }
    }

    public Brush BorderBrush => IsSelected
        ? new SolidColorBrush(Color.FromRgb(0x0B, 0x6E, 0x4F))
        : new SolidColorBrush(Color.FromRgb(0xD0, 0xD7, 0xDE));

    public ObservableCollection<KvPairRow> Props { get; } = [];

    public static VisionNodeVm FromModel(VisionNode n)
    {
        var vm = new VisionNodeVm
        {
            Id = n.Id,
            Kind = n.Kind,
            Title = n.Kind,
            X = n.X,
            Y = n.Y,
            Order = n.Order,
        };
        foreach (var kv in n.Props.OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase))
        {
            vm.Props.Add(new KvPairRow { Key = kv.Key, Value = kv.Value });
        }

        return vm;
    }

    public VisionNode ToModel() => new()
    {
        Id = Id,
        Kind = Kind,
        X = X,
        Y = Y,
        Order = Order,
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

public sealed class VisionEdgeVm
{
    public string From { get; set; } = "";
    public string To { get; set; } = "";
    public string Port { get; set; } = VisionPorts.Next;
    public string? FromPort { get; set; }
    public string? ToPort { get; set; }

    public bool IsData =>
        !string.IsNullOrWhiteSpace(FromPort) || !string.IsNullOrWhiteSpace(ToPort);

    public static VisionEdgeVm FromModel(VisionEdge e) => new()
    {
        From = e.From,
        To = e.To,
        Port = e.Port,
        FromPort = e.FromPort,
        ToPort = e.ToPort,
    };

    public VisionEdge ToModel() => new()
    {
        From = From,
        To = To,
        Port = string.IsNullOrWhiteSpace(Port) ? VisionPorts.Next : Port,
        FromPort = FromPort,
        ToPort = ToPort,
    };
}
