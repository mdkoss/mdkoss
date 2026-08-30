using MDKOSS.Core.Flow;

namespace MDKOSS.Tools.Calib.Calib;

/// <summary>Builds the linear platform-axis calibration flow used by sample hosts.</summary>
public static class CalibFlowFactory
{
    public static FlowDocument CreatePlatformAxisMove(
        string deviceId,
        string axis,
        string position,
        string resultKey,
        string resultExpr,
        string startMessage,
        string doneMessage)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(axis);

        var start = Quote(startMessage);
        var done = Quote(doneMessage);
        var pos = string.IsNullOrWhiteSpace(position) ? "0" : position.Trim();
        var key = string.IsNullOrWhiteSpace(resultKey) ? "offset" : resultKey.Trim();
        var expr = string.IsNullOrWhiteSpace(resultExpr) ? pos : resultExpr.Trim();

        return new FlowDocument
        {
            Version = 1,
            Functions = [new FlowFunction { Name = "main", EntryNodeId = "n-start" }],
            Nodes =
            [
                Node("n-start", FlowNodeKinds.Start, 40, 0),
                Node("n-log1", FlowNodeKinds.OpLog, 120, 1, ("message", start)),
                Node("n-en", FlowNodeKinds.MotionPlatformStart, 200, 2, ("deviceId", deviceId)),
                Node("n-mv", FlowNodeKinds.MotionPlatformAxisMoveTo, 280, 3,
                    ("deviceId", deviceId), ("axis", axis), ("position", pos)),
                Node("n-delay", FlowNodeKinds.Delay, 360, 4, ("ms", "200")),
                Node("n-ok", FlowNodeKinds.MotionSetTaskVar, 440, 5, ("key", "calib.ok"), ("expr", "true")),
                Node("n-off", FlowNodeKinds.MotionSetTaskVar, 520, 6, ("key", "calib." + key), ("expr", expr)),
                Node("n-msg", FlowNodeKinds.MotionSetTaskVar, 600, 7, ("key", "calib.message"), ("expr", done)),
                Node("n-log2", FlowNodeKinds.OpLog, 680, 8, ("message", "\"标定完成\"")),
                Node("n-end", FlowNodeKinds.End, 760, 9),
            ],
            Edges =
            [
                Edge("n-start", "n-log1"),
                Edge("n-log1", "n-en"),
                Edge("n-en", "n-mv"),
                Edge("n-mv", "n-delay"),
                Edge("n-delay", "n-ok"),
                Edge("n-ok", "n-off"),
                Edge("n-off", "n-msg"),
                Edge("n-msg", "n-log2"),
                Edge("n-log2", "n-end"),
            ],
        };
    }

    public static string ToJson(FlowDocument document) => document.ToJson();

    private static FlowNode Node(string id, string kind, double y, int order, params (string Key, string Value)[] props)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in props)
        {
            map[key] = value;
        }

        return new FlowNode
        {
            Id = id,
            Kind = kind,
            X = 300,
            Y = y,
            Order = order,
            Props = map,
        };
    }

    private static FlowEdge Edge(string from, string to) =>
        new() { From = from, To = to, Port = FlowPorts.Next };

    private static string Quote(string text)
    {
        var escaped = (text ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"");
        return "\"" + escaped + "\"";
    }
}
