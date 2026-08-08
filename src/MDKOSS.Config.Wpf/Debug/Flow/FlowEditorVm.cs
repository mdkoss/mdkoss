using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using MDKOSS.Core.Flow;

namespace MDKOSS.Config.Wpf.Debug.Flow;

/// <summary>
/// Composite-block workflow editor: root sequence + if(then/else) / while(body) trees.
/// <see cref="FlowNode.ParentId"/> / <see cref="FlowNode.Slot"/> / <see cref="FlowNode.Order"/> are authoritative;
/// edges are derived via <see cref="FlowComposite.BuildEdges"/>.
/// </summary>
public sealed class FlowEditorVm : INotifyPropertyChanged
{
    public const double LayoutNodeWidth = 200;
    public const double LayoutNodeHeight = 64;
    public const double LayoutGapY = 36;
    public const double LayoutTop = 40;
    public const double LayoutCenterX = 480;
    public const double LayoutBranchOffsetX = 230;
    public const double LayoutBodyIndentX = 40;

    private FlowNodeVm? _selected;
    private string _jsonPreview = string.Empty;
    private string _validationText = string.Empty;
    private string? _focusParentId;
    private string? _focusSlot;

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<FlowNodeVm> Nodes { get; } = [];
    public ObservableCollection<FlowEdgeVm> Edges { get; } = [];
    public ObservableCollection<FlowVarVm> Variables { get; } = [];
    public ObservableCollection<FlowFuncVm> Functions { get; } = [];
    public ObservableCollection<FlowRegionVm> Regions { get; } = [];

    /// <summary>Insert context: null parent = root spine; else parentId + slot (then/else/body).</summary>
    public string? FocusParentId
    {
        get => _focusParentId;
        private set { _focusParentId = value; OnPropertyChanged(); OnPropertyChanged(nameof(FocusLabel)); }
    }

    public string? FocusSlot
    {
        get => _focusSlot;
        private set { _focusSlot = value; OnPropertyChanged(); OnPropertyChanged(nameof(FocusLabel)); }
    }

    public string FocusLabel
    {
        get
        {
            if (string.IsNullOrWhiteSpace(FocusParentId))
            {
                return "插入位置：根序列";
            }

            var parent = Nodes.FirstOrDefault(n =>
                string.Equals(n.Id, FocusParentId, StringComparison.OrdinalIgnoreCase));
            return $"插入位置：{parent?.Kind ?? "?"} [{FocusSlot}]  ({FocusParentId})";
        }
    }

    public FlowNodeVm? Selected
    {
        get => _selected;
        set
        {
            _selected = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasSelection));
            OnPropertyChanged(nameof(SelectedIsIf));
            OnPropertyChanged(nameof(SelectedIsWhile));
            OnPropertyChanged(nameof(SelectedIsComposite));
        }
    }

    public bool HasSelection => Selected is not null;
    public bool SelectedIsIf =>
        Selected is not null && string.Equals(Selected.Kind, FlowNodeKinds.If, StringComparison.OrdinalIgnoreCase);
    public bool SelectedIsWhile =>
        Selected is not null && string.Equals(Selected.Kind, FlowNodeKinds.While, StringComparison.OrdinalIgnoreCase);
    public bool SelectedIsComposite => SelectedIsIf || SelectedIsWhile;

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
        Regions.Clear();
        Selected = null;
        FocusParentId = null;
        FocusSlot = null;

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

        if (!FlowComposite.HasTreeMetadata(doc.Nodes))
        {
            // Legacy: promote edge spine to root Order; keep orphan nodes as root after end.
            PromoteLegacySpine(doc);
        }

        if (Functions.Count == 0)
        {
            var start = Nodes.FirstOrDefault(n =>
                string.Equals(n.Kind, FlowNodeKinds.Start, StringComparison.OrdinalIgnoreCase));
            Functions.Add(new FlowFuncVm { Name = "main", EntryNodeId = start?.Id ?? "" });
        }

        EnsureStartEnd();
        RelayoutAndAutoWire();
        RefreshPreview();
    }

    private void PromoteLegacySpine(FlowDocument doc)
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
                var nextId = doc.Edges.FirstOrDefault(e =>
                    string.Equals(e.From, cur.Id, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(e.Port, FlowPorts.Next, StringComparison.OrdinalIgnoreCase))?.To;
                if (nextId is null)
                {
                    var kind = cur.Kind.Trim().ToLowerInvariant();
                    if (kind == "if")
                    {
                        nextId = doc.Edges.FirstOrDefault(e =>
                            string.Equals(e.From, cur.Id, StringComparison.OrdinalIgnoreCase)
                            && string.Equals(e.Port, FlowPorts.False, StringComparison.OrdinalIgnoreCase))?.To;
                    }
                    else if (kind == "while")
                    {
                        nextId = doc.Edges.FirstOrDefault(e =>
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

        for (var i = 0; i < ordered.Count; i++)
        {
            ordered[i].ParentId = null;
            ordered[i].Slot = null;
            ordered[i].Order = i;
        }
    }

    public FlowDocument ToDocument()
    {
        var nodes = Nodes.Select(n => n.ToModel()).ToList();
        FlowComposite.RenumberOrders(nodes);
        var edges = FlowComposite.BuildEdges(nodes);
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
            Nodes = nodes,
            Edges = edges,
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

    public void FocusRoot()
    {
        FocusParentId = null;
        FocusSlot = null;
    }

    public void AdoptFocusFromNode(FlowNodeVm node)
    {
        if (string.IsNullOrWhiteSpace(node.ParentId))
        {
            if (FlowComposite.IsCompositeKind(node.Kind))
            {
                // keep current slot if already focused on this composite; else default then/body
                if (!string.Equals(FocusParentId, node.Id, StringComparison.OrdinalIgnoreCase))
                {
                    FocusParentId = null;
                    FocusSlot = null;
                }
            }
            else
            {
                FocusParentId = null;
                FocusSlot = null;
            }

            return;
        }

        FocusParentId = node.ParentId;
        FocusSlot = node.Slot;
    }

    public void FocusSlotOfSelected(string slot)
    {
        if (Selected is null || !FlowComposite.IsCompositeKind(Selected.Kind))
        {
            return;
        }

        FocusParentId = Selected.Id;
        FocusSlot = slot;
    }

    public void FocusParentOfSelected()
    {
        if (Selected is null || string.IsNullOrWhiteSpace(Selected.ParentId))
        {
            FocusRoot();
            return;
        }

        var parent = Nodes.FirstOrDefault(n =>
            string.Equals(n.Id, Selected.ParentId, StringComparison.OrdinalIgnoreCase));
        FocusParentId = Selected.ParentId;
        FocusSlot = Selected.Slot;
        if (parent is not null)
        {
            Selected = parent;
            foreach (var n in Nodes)
            {
                n.IsSelected = ReferenceEquals(n, parent);
            }
        }
    }

    public void ToggleCollapseSelected()
    {
        if (Selected is null || !FlowComposite.IsCompositeKind(Selected.Kind))
        {
            return;
        }

        Selected.IsCollapsed = !Selected.IsCollapsed;
        RelayoutAndAutoWire();
        RefreshPreview();
    }

    /// <summary>Insert activity into current focus sequence (after selected sibling when possible).</summary>
    public FlowNodeVm InsertNode(string kind)
    {
        EnsureStartEnd();
        var id = "n-" + Guid.NewGuid().ToString("N")[..8];
        var vm = new FlowNodeVm
        {
            Id = id,
            Kind = kind,
            Title = kind,
            ParentId = FocusParentId,
            Slot = string.IsNullOrWhiteSpace(FocusParentId) ? null : FocusSlot,
        };
        ApplyDefaultProps(vm);

        if (string.Equals(kind, FlowNodeKinds.End, StringComparison.OrdinalIgnoreCase)
            || string.Equals(kind, FlowNodeKinds.Start, StringComparison.OrdinalIgnoreCase))
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

        // Resolve insert context from selection when focus empty
        if (string.IsNullOrWhiteSpace(FocusParentId) && Selected is not null
            && !string.IsNullOrWhiteSpace(Selected.ParentId))
        {
            FocusParentId = Selected.ParentId;
            FocusSlot = Selected.Slot;
            vm.ParentId = FocusParentId;
            vm.Slot = FocusSlot;
        }
        else if (Selected is not null && FlowComposite.IsCompositeKind(Selected.Kind)
                 && string.IsNullOrWhiteSpace(FocusParentId))
        {
            // Selecting composite without slot → insert after it in its parent sequence
            FocusParentId = Selected.ParentId;
            FocusSlot = Selected.Slot;
            vm.ParentId = FocusParentId;
            vm.Slot = string.IsNullOrWhiteSpace(FocusParentId) ? null : FocusSlot;
        }

        var siblings = GetSiblingVms(vm.ParentId, vm.Slot).ToList();
        var insertAt = siblings.Count;
        if (Selected is not null
            && string.Equals(Selected.ParentId ?? "", vm.ParentId ?? "", StringComparison.OrdinalIgnoreCase)
            && string.Equals(Selected.Slot ?? "", vm.Slot ?? "", StringComparison.OrdinalIgnoreCase))
        {
            var idx = siblings.FindIndex(n => string.Equals(n.Id, Selected.Id, StringComparison.OrdinalIgnoreCase));
            if (idx >= 0)
            {
                insertAt = idx + 1;
            }
        }

        // Root: never insert before start / after end
        if (string.IsNullOrWhiteSpace(vm.ParentId))
        {
            var startIdx = siblings.FindIndex(n =>
                string.Equals(n.Kind, FlowNodeKinds.Start, StringComparison.OrdinalIgnoreCase));
            var endIdx = siblings.FindIndex(n =>
                string.Equals(n.Kind, FlowNodeKinds.End, StringComparison.OrdinalIgnoreCase));
            if (startIdx >= 0 && insertAt <= startIdx)
            {
                insertAt = startIdx + 1;
            }

            if (endIdx >= 0 && insertAt > endIdx)
            {
                insertAt = endIdx;
            }

            if (endIdx >= 0 && insertAt == endIdx + 1)
            {
                insertAt = endIdx;
            }
        }

        siblings.Insert(Math.Clamp(insertAt, 0, siblings.Count), vm);
        for (var i = 0; i < siblings.Count; i++)
        {
            siblings[i].Order = i;
            siblings[i].ParentId = vm.ParentId;
            siblings[i].Slot = vm.Slot;
        }

        if (!Nodes.Contains(vm))
        {
            Nodes.Add(vm);
        }

        // Composite templates
        if (string.Equals(kind, FlowNodeKinds.If, StringComparison.OrdinalIgnoreCase))
        {
            FocusParentId = vm.Id;
            FocusSlot = FlowSlots.Then;
        }
        else if (string.Equals(kind, FlowNodeKinds.While, StringComparison.OrdinalIgnoreCase))
        {
            var body = new FlowNodeVm
            {
                Id = "n-" + Guid.NewGuid().ToString("N")[..8],
                Kind = FlowNodeKinds.Delay,
                Title = FlowNodeKinds.Delay,
                ParentId = vm.Id,
                Slot = FlowSlots.Body,
                Order = 0,
            };
            ApplyDefaultProps(body);
            body.SetProp("ms", "0");
            Nodes.Add(body);
            FocusParentId = vm.Id;
            FocusSlot = FlowSlots.Body;
        }

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
            return;
        }

        var models = Nodes.Select(n => n.ToModel()).ToList();
        var removeIds = FlowComposite.CollectSubtreeIds(models, Selected.Id);
        foreach (var id in removeIds.ToList())
        {
            var vm = Nodes.FirstOrDefault(n => string.Equals(n.Id, id, StringComparison.OrdinalIgnoreCase));
            if (vm is not null)
            {
                Nodes.Remove(vm);
            }
        }

        Selected = null;
        if (!string.IsNullOrWhiteSpace(FocusParentId) && removeIds.Contains(FocusParentId))
        {
            FocusRoot();
        }

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

        var siblings = GetSiblingVms(Selected.ParentId, Selected.Slot).ToList();
        var idx = siblings.FindIndex(n => string.Equals(n.Id, Selected.Id, StringComparison.OrdinalIgnoreCase));
        if (idx < 0)
        {
            return false;
        }

        var target = idx + delta;
        var min = 0;
        var max = siblings.Count - 1;
        if (string.IsNullOrWhiteSpace(Selected.ParentId))
        {
            var startIdx = siblings.FindIndex(n =>
                string.Equals(n.Kind, FlowNodeKinds.Start, StringComparison.OrdinalIgnoreCase));
            var endIdx = siblings.FindIndex(n =>
                string.Equals(n.Kind, FlowNodeKinds.End, StringComparison.OrdinalIgnoreCase));
            min = startIdx >= 0 ? startIdx + 1 : 0;
            max = endIdx >= 0 ? endIdx - 1 : siblings.Count - 1;
        }

        if (target < min || target > max)
        {
            return false;
        }

        (siblings[idx], siblings[target]) = (siblings[target], siblings[idx]);
        for (var i = 0; i < siblings.Count; i++)
        {
            siblings[i].Order = i;
        }

        RelayoutAndAutoWire();
        RefreshPreview();
        return true;
    }

    public void RelayoutAndAutoWire()
    {
        EnsureStartEnd();
        SyncMainEntry();
        RenumberAllOrders();
        LayoutTree();
        SyncEdgesFromTree();
        RebuildRegions();
    }

    private void RenumberAllOrders()
    {
        var models = Nodes.Select(n => n.ToModel()).ToList();
        FlowComposite.RenumberOrders(models);
        foreach (var m in models)
        {
            var vm = Nodes.First(n => string.Equals(n.Id, m.Id, StringComparison.OrdinalIgnoreCase));
            vm.Order = m.Order;
        }
    }

    private void SyncEdgesFromTree()
    {
        var models = Nodes.Select(n => n.ToModel()).ToList();
        var built = FlowComposite.BuildEdges(models);
        Edges.Clear();
        foreach (var e in built)
        {
            Edges.Add(new FlowEdgeVm { From = e.From, To = e.To, Port = e.Port });
        }
    }

    private List<FlowNodeVm> GetSiblingVms(string? parentId, string? slot)
    {
        var parentKey = string.IsNullOrWhiteSpace(parentId) ? null : parentId.Trim();
        var slotKey = string.IsNullOrWhiteSpace(slot) ? null : slot.Trim();
        return Nodes
            .Where(n =>
            {
                var p = string.IsNullOrWhiteSpace(n.ParentId) ? null : n.ParentId.Trim();
                var s = string.IsNullOrWhiteSpace(n.Slot) ? null : n.Slot.Trim();
                var parentMatch = string.Equals(p, parentKey, StringComparison.OrdinalIgnoreCase)
                                  || (p is null && parentKey is null);
                if (!parentMatch)
                {
                    return false;
                }

                if (parentKey is null)
                {
                    return s is null;
                }

                return string.Equals(s, slotKey, StringComparison.OrdinalIgnoreCase);
            })
            .OrderBy(n => n.Order)
            .ThenBy(n => n.Id, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private void LayoutTree()
    {
        var root = GetSiblingVms(null, null);
        LayoutSequence(root, LayoutCenterX, LayoutTop);
    }

    private double LayoutSequence(IReadOnlyList<FlowNodeVm> seq, double centerX, double startY)
    {
        var y = startY;
        foreach (var node in seq)
        {
            node.X = centerX - LayoutNodeWidth / 2;
            node.Y = y;
            var kind = node.Kind.Trim().ToLowerInvariant();

            if (node.IsCollapsed && FlowComposite.IsCompositeKind(node.Kind))
            {
                y += LayoutNodeHeight + LayoutGapY;
                continue;
            }

            if (kind == "if")
            {
                y += LayoutNodeHeight + 12;
                var thenKids = GetSiblingVms(node.Id, FlowSlots.Then);
                var elseKids = GetSiblingVms(node.Id, FlowSlots.Else);
                var yThen = LayoutSequence(thenKids, centerX - LayoutBranchOffsetX, y);
                var yElse = LayoutSequence(elseKids, centerX + LayoutBranchOffsetX, y);
                y = Math.Max(yThen, yElse) + LayoutGapY;
            }
            else if (kind == "while")
            {
                y += LayoutNodeHeight + 12;
                var body = GetSiblingVms(node.Id, FlowSlots.Body);
                y = LayoutSequence(body, centerX + LayoutBodyIndentX, y) + LayoutGapY;
            }
            else
            {
                y += LayoutNodeHeight + LayoutGapY;
            }
        }

        return y;
    }

    private void RebuildRegions()
    {
        Regions.Clear();
        foreach (var node in Nodes.Where(n => FlowComposite.IsCompositeKind(n.Kind) && !n.IsCollapsed))
        {
            var kind = node.Kind.Trim().ToLowerInvariant();
            if (kind == "if")
            {
                AddRegion(node, FlowSlots.Then, "THEN", -LayoutBranchOffsetX);
                AddRegion(node, FlowSlots.Else, "ELSE", LayoutBranchOffsetX);
            }
            else if (kind == "while")
            {
                AddRegion(node, FlowSlots.Body, "BODY", LayoutBodyIndentX);
            }
        }
    }

    private void AddRegion(FlowNodeVm parent, string slot, string label, double dx)
    {
        var kids = GetSiblingVms(parent.Id, slot);
        var x = parent.X + dx;
        var y = parent.Y + LayoutNodeHeight + 4;
        double w = LayoutNodeWidth + 24;
        double h = LayoutNodeHeight + 16;
        if (kids.Count > 0)
        {
            var minX = kids.Min(k => k.X) - 12;
            var maxX = kids.Max(k => k.X + LayoutNodeWidth) + 12;
            var minY = kids.Min(k => k.Y) - 8;
            var maxY = kids.Max(k => k.Y + LayoutNodeHeight) + 8;
            // also include nested descendants roughly via all nodes with ancestor parent
            foreach (var n in Nodes)
            {
                if (IsUnder(n, parent.Id) && string.Equals(GetRootSlot(n, parent.Id), slot, StringComparison.OrdinalIgnoreCase))
                {
                    minX = Math.Min(minX, n.X - 12);
                    maxX = Math.Max(maxX, n.X + LayoutNodeWidth + 12);
                    minY = Math.Min(minY, n.Y - 8);
                    maxY = Math.Max(maxY, n.Y + LayoutNodeHeight + 8);
                }
            }

            x = minX;
            y = minY;
            w = Math.Max(w, maxX - minX);
            h = Math.Max(h, maxY - minY);
        }

        var focused = string.Equals(FocusParentId, parent.Id, StringComparison.OrdinalIgnoreCase)
                      && string.Equals(FocusSlot, slot, StringComparison.OrdinalIgnoreCase);
        Regions.Add(new FlowRegionVm
        {
            ParentId = parent.Id,
            Slot = slot,
            Label = label,
            X = x,
            Y = y,
            Width = w,
            Height = h,
            IsFocused = focused,
        });
    }

    private bool IsUnder(FlowNodeVm n, string ancestorId)
    {
        var cur = n;
        var guard = 0;
        while (!string.IsNullOrWhiteSpace(cur.ParentId) && guard++ < 64)
        {
            if (string.Equals(cur.ParentId, ancestorId, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            cur = Nodes.FirstOrDefault(x =>
                string.Equals(x.Id, cur.ParentId, StringComparison.OrdinalIgnoreCase));
            if (cur is null)
            {
                return false;
            }
        }

        return false;
    }

    private string? GetRootSlot(FlowNodeVm n, string compositeId)
    {
        var cur = n;
        var guard = 0;
        while (!string.IsNullOrWhiteSpace(cur.ParentId) && guard++ < 64)
        {
            if (string.Equals(cur.ParentId, compositeId, StringComparison.OrdinalIgnoreCase))
            {
                return cur.Slot;
            }

            cur = Nodes.FirstOrDefault(x =>
                string.Equals(x.Id, cur.ParentId, StringComparison.OrdinalIgnoreCase));
            if (cur is null)
            {
                return null;
            }
        }

        return null;
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
                Order = 0,
            });
        }

        if (!Nodes.Any(n => string.Equals(n.Kind, FlowNodeKinds.End, StringComparison.OrdinalIgnoreCase)))
        {
            Nodes.Add(new FlowNodeVm
            {
                Id = "n-end",
                Kind = FlowNodeKinds.End,
                Title = FlowNodeKinds.End,
                Order = 999,
            });
        }

        foreach (var n in Nodes.Where(n =>
                     string.Equals(n.Kind, FlowNodeKinds.Start, StringComparison.OrdinalIgnoreCase)
                     || string.Equals(n.Kind, FlowNodeKinds.End, StringComparison.OrdinalIgnoreCase)))
        {
            n.ParentId = null;
            n.Slot = null;
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
                vm.SetProp("deviceId", "");
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
                vm.SetProp("deviceId", "");
                vm.SetProp("alias", "out.tower.green");
                vm.SetProp("value", "true");
                break;
            case "motion.gpioread":
                vm.SetProp("deviceId", "");
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

public sealed class FlowRegionVm
{
    public string ParentId { get; set; } = "";
    public string Slot { get; set; } = "";
    public string Label { get; set; } = "";
    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
    public bool IsFocused { get; set; }
}

public sealed class FlowNodeVm : INotifyPropertyChanged
{
    private string _id = "";
    private string _kind = FlowNodeKinds.Start;
    private string _title = "";
    private double _x;
    private double _y;
    private bool _isSelected;
    private bool _isCollapsed;
    private string? _parentId;
    private string? _slot;
    private int _order;

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Id { get => _id; set { _id = value; OnPropertyChanged(); } }
    public string Kind { get => _kind; set { _kind = value; Title = value; OnPropertyChanged(); } }
    public string Title { get => _title; set { _title = value; OnPropertyChanged(); } }
    public double X { get => _x; set { _x = value; OnPropertyChanged(); } }
    public double Y { get => _y; set { _y = value; OnPropertyChanged(); } }
    public string? ParentId { get => _parentId; set { _parentId = value; OnPropertyChanged(); } }
    public string? Slot { get => _slot; set { _slot = value; OnPropertyChanged(); } }
    public int Order { get => _order; set { _order = value; OnPropertyChanged(); } }

    public bool IsCollapsed
    {
        get => _isCollapsed;
        set { _isCollapsed = value; OnPropertyChanged(); }
    }

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
            ParentId = string.IsNullOrWhiteSpace(n.ParentId) ? null : n.ParentId,
            Slot = string.IsNullOrWhiteSpace(n.Slot) ? null : n.Slot,
            Order = n.Order,
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
        ParentId = string.IsNullOrWhiteSpace(ParentId) ? null : ParentId,
        Slot = string.IsNullOrWhiteSpace(Slot) ? null : Slot,
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

public sealed class FlowEdgeVm : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    public string From { get; set; } = "";
    public string To { get; set; } = "";
    public string Port { get; set; } = FlowPorts.Next;
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
