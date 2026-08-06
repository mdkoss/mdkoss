using System.Text.Json.Serialization;

namespace MDKOSS.Core.Flow;

/// <summary>Slot names for composite <c>if</c> / <c>while</c> children (<see cref="FlowNode.Slot"/>).</summary>
public static class FlowSlots
{
    public const string Then = "then";
    public const string Else = "else";
    public const string Body = "body";

    public static bool IsKnown(string? slot) =>
        string.Equals(slot, Then, StringComparison.OrdinalIgnoreCase)
        || string.Equals(slot, Else, StringComparison.OrdinalIgnoreCase)
        || string.Equals(slot, Body, StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// Tree helpers for composite blocks: parentId/slot/order ↔ runtime edges.
/// Editor is authoritative on the tree; edges are derived for <see cref="FlowInterpreter"/>.
/// </summary>
public static class FlowComposite
{
    public static bool HasTreeMetadata(IEnumerable<FlowNode> nodes) =>
        nodes.Any(n => !string.IsNullOrWhiteSpace(n.ParentId));

    public static bool IsCompositeKind(string? kind)
    {
        var k = (kind ?? "").Trim().ToLowerInvariant();
        return k is "if" or "while";
    }

    public static IReadOnlyList<FlowNode> GetChildren(
        IReadOnlyList<FlowNode> nodes,
        string? parentId,
        string? slot)
    {
        var parentKey = string.IsNullOrWhiteSpace(parentId) ? null : parentId.Trim();
        var slotKey = string.IsNullOrWhiteSpace(slot) ? null : slot.Trim();
        return nodes
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
                    // root: slot must be empty
                    return s is null;
                }

                return string.Equals(s, slotKey, StringComparison.OrdinalIgnoreCase);
            })
            .OrderBy(n => n.Order)
            .ThenBy(n => n.Id, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static IReadOnlyList<FlowNode> GetRootSequence(IReadOnlyList<FlowNode> nodes)
    {
        var roots = GetChildren(nodes, null, null).ToList();
        var start = roots.FirstOrDefault(n =>
            string.Equals(n.Kind, FlowNodeKinds.Start, StringComparison.OrdinalIgnoreCase));
        var end = roots.FirstOrDefault(n =>
            string.Equals(n.Kind, FlowNodeKinds.End, StringComparison.OrdinalIgnoreCase));
        var mid = roots
            .Where(n =>
                !string.Equals(n.Kind, FlowNodeKinds.Start, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(n.Kind, FlowNodeKinds.End, StringComparison.OrdinalIgnoreCase))
            .ToList();

        var seq = new List<FlowNode>();
        if (start is not null)
        {
            seq.Add(start);
        }

        seq.AddRange(mid);
        if (end is not null)
        {
            seq.Add(end);
        }

        return seq;
    }

    /// <summary>Renumber <see cref="FlowNode.Order"/> within each (parentId, slot) group.</summary>
    public static void RenumberOrders(IList<FlowNode> nodes)
    {
        var groups = nodes.GroupBy(n =>
        {
            var p = string.IsNullOrWhiteSpace(n.ParentId) ? "" : n.ParentId.Trim().ToLowerInvariant();
            var s = string.IsNullOrWhiteSpace(n.Slot) ? "" : n.Slot.Trim().ToLowerInvariant();
            return p + "\n" + s;
        });

        foreach (var g in groups)
        {
            var ordered = g.OrderBy(n => n.Order).ThenBy(n => n.Id, StringComparer.OrdinalIgnoreCase).ToList();
            for (var i = 0; i < ordered.Count; i++)
            {
                ordered[i].Order = i;
            }
        }
    }

    /// <summary>Derive runtime edges from composite tree (parentId/slot/order).</summary>
    public static List<FlowEdge> BuildEdges(IReadOnlyList<FlowNode> nodes)
    {
        var edges = new List<FlowEdge>();
        var byId = nodes.ToDictionary(n => n.Id, StringComparer.OrdinalIgnoreCase);
        WireSequence(GetRootSequence(nodes).ToList(), edges, byId, nodes);
        return edges;
    }

    private static void WireSequence(
        IReadOnlyList<FlowNode> seq,
        List<FlowEdge> edges,
        IReadOnlyDictionary<string, FlowNode> byId,
        IReadOnlyList<FlowNode> all)
    {
        for (var i = 0; i < seq.Count - 1; i++)
        {
            var from = seq[i];
            var join = seq[i + 1];
            WireNodeToJoin(from, join, edges, all);
        }
    }

    private static void WireNodeToJoin(
        FlowNode from,
        FlowNode join,
        List<FlowEdge> edges,
        IReadOnlyList<FlowNode> all)
    {
        var kind = (from.Kind ?? "").Trim().ToLowerInvariant();
        switch (kind)
        {
            case "if":
                WireIf(from, join, edges, all);
                break;
            case "while":
                WireWhile(from, join, edges, all);
                break;
            case "end":
                break;
            default:
                AddEdge(edges, from.Id, FlowPorts.Next, join.Id);
                break;
        }
    }

    private static void WireIf(
        FlowNode ifNode,
        FlowNode join,
        List<FlowEdge> edges,
        IReadOnlyList<FlowNode> all)
    {
        var thenKids = GetChildren(all, ifNode.Id, FlowSlots.Then).ToList();
        var elseKids = GetChildren(all, ifNode.Id, FlowSlots.Else).ToList();
        AddEdge(edges, ifNode.Id, FlowPorts.True, thenKids.Count > 0 ? thenKids[0].Id : join.Id);
        AddEdge(edges, ifNode.Id, FlowPorts.False, elseKids.Count > 0 ? elseKids[0].Id : join.Id);
        WireChildChain(thenKids, join, edges, all);
        WireChildChain(elseKids, join, edges, all);
    }

    private static void WireWhile(
        FlowNode whileNode,
        FlowNode exitTo,
        List<FlowEdge> edges,
        IReadOnlyList<FlowNode> all)
    {
        var body = GetChildren(all, whileNode.Id, FlowSlots.Body).ToList();
        if (body.Count == 0)
        {
            // Empty body: re-enter while (editor should insert a placeholder).
            AddEdge(edges, whileNode.Id, FlowPorts.Body, whileNode.Id);
        }
        else
        {
            AddEdge(edges, whileNode.Id, FlowPorts.Body, body[0].Id);
            WireChildChain(body, whileNode, edges, all); // last → while
        }

        AddEdge(edges, whileNode.Id, FlowPorts.Exit, exitTo.Id);
    }

    private static void WireChildChain(
        IReadOnlyList<FlowNode> kids,
        FlowNode join,
        List<FlowEdge> edges,
        IReadOnlyList<FlowNode> all)
    {
        for (var i = 0; i < kids.Count; i++)
        {
            var cur = kids[i];
            var next = i + 1 < kids.Count ? kids[i + 1] : join;
            WireNodeToJoin(cur, next, edges, all);
        }
    }

    private static void AddEdge(List<FlowEdge> edges, string from, string port, string to)
    {
        edges.Add(new FlowEdge { From = from, To = to, Port = port });
    }

    /// <summary>Collect node id and all descendants.</summary>
    public static HashSet<string> CollectSubtreeIds(IReadOnlyList<FlowNode> nodes, string rootId)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { rootId };
        var changed = true;
        while (changed)
        {
            changed = false;
            foreach (var n in nodes)
            {
                if (string.IsNullOrWhiteSpace(n.ParentId) || set.Contains(n.Id))
                {
                    continue;
                }

                if (set.Contains(n.ParentId))
                {
                    set.Add(n.Id);
                    changed = true;
                }
            }
        }

        return set;
    }

    public static IReadOnlyList<string> ValidateTree(IReadOnlyList<FlowNode> nodes)
    {
        var errors = new List<string>();
        var ids = new HashSet<string>(nodes.Select(n => n.Id), StringComparer.OrdinalIgnoreCase);
        foreach (var n in nodes)
        {
            if (string.IsNullOrWhiteSpace(n.ParentId))
            {
                continue;
            }

            if (!ids.Contains(n.ParentId))
            {
                errors.Add($"node '{n.Id}' parentId '{n.ParentId}' not found");
                continue;
            }

            var parent = nodes.First(x => string.Equals(x.Id, n.ParentId, StringComparison.OrdinalIgnoreCase));
            var pk = (parent.Kind ?? "").Trim().ToLowerInvariant();
            var slot = (n.Slot ?? "").Trim().ToLowerInvariant();
            if (pk == "if" && slot is not ("then" or "else"))
            {
                errors.Add($"node '{n.Id}' under if must use slot then|else");
            }
            else if (pk == "while" && slot is not "body")
            {
                errors.Add($"node '{n.Id}' under while must use slot body");
            }
            else if (pk is not ("if" or "while"))
            {
                errors.Add($"node '{n.Id}' parent '{n.ParentId}' is not a composite");
            }
        }

        // no cycles in parent chain
        foreach (var n in nodes)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var cur = n;
            while (!string.IsNullOrWhiteSpace(cur.ParentId))
            {
                if (!seen.Add(cur.Id))
                {
                    errors.Add($"parent cycle involving '{n.Id}'");
                    break;
                }

                var p = nodes.FirstOrDefault(x =>
                    string.Equals(x.Id, cur.ParentId, StringComparison.OrdinalIgnoreCase));
                if (p is null)
                {
                    break;
                }

                cur = p;
            }
        }

        return errors;
    }
}
