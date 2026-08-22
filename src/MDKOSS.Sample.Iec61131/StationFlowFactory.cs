using MDKOSS.Core;
using MDKOSS.Core.Flow;

namespace MDKOSS.Sample.Iec61131;

/// <summary>Composite station cycle used as the IEC export example.</summary>
public static class StationFlowFactory
{
    public static FlowDocument Create()
    {
        var doc = new FlowDocument { Version = 1 };
        doc.Variables.AddRange(
        [
            V("cycleCount", "number", "0"),
            V("recipeCount", "number", "3"),
            V("pickPos", "number", "10"),
            V("placePos", "number", "50"),
            V("startOk", "bool", "false"),
            V("stopHit", "bool", "false"),
            V("doorOk", "bool", "false"),
            V("wpPresent", "bool", "false"),
            V("tmp", "number", "0"),
        ]);
        doc.Functions.Add(new FlowFunction { Name = "main", EntryNodeId = "n-start" });
        doc.Functions.Add(new FlowFunction { Name = "FaultHold", EntryNodeId = "f-start" });

        var order = 0;
        N(doc, "n-start", FlowNodeKinds.Start, order++);
        N(doc, "n-decl", FlowNodeKinds.DeclareVar, order++, props: P("name", "tmp", "type", "number", "init", "0"));
        N(doc, "n-recipe", FlowNodeKinds.SetVar, order++, props: P("name", "recipeCount", "expr", "recipe.targetCount"));
        N(doc, "n-param", FlowNodeKinds.MotionSetParam, order++, props: P("key", "jogVel", "expr", "5"));
        N(doc, "n-ready-off", FlowNodeKinds.MotionSetGlobalVar, order++, props: P("key", "machine.ready", "expr", "false"));
        N(doc, "n-state-idle", FlowNodeKinds.MotionSetTaskVar, order++, props: P("key", "state", "expr", "\"idle\""));
        N(doc, "n-y-on", FlowNodeKinds.MotionGpioWrite, order++, props: Io("gpio-station", "tower.yellow", "true"));
        N(doc, "n-r-off", FlowNodeKinds.MotionGpioWrite, order++, props: Io("gpio-station", "tower.red", "false"));
        N(doc, "n-g-off", FlowNodeKinds.MotionGpioWrite, order++, props: Io("gpio-station", "tower.green", "false"));
        N(doc, "n-loop", FlowNodeKinds.While, order++, props: P("condition", "cycleCount < recipeCount"));
        N(doc, "n-done-green", FlowNodeKinds.MotionGpioWrite, order++, props: Io("gpio-station", "tower.green", "true"));
        N(doc, "n-done-state", FlowNodeKinds.MotionSetTaskVar, order++, props: P("key", "state", "expr", "\"done\""));
        N(doc, "n-done-ready", FlowNodeKinds.MotionSetGlobalVar, order++, props: P("key", "machine.ready", "expr", "true"));
        N(doc, "n-buzz-off", FlowNodeKinds.OpWriteIo, order++, props: Io("gpio-station", "buzzer", "false"));
        N(doc, "n-end", FlowNodeKinds.End, order++);

        var b = 0;
        N(doc, "n-rd-start", FlowNodeKinds.MotionGpioRead, b++, "n-loop", FlowSlots.Body,
            P("deviceId", "gpio-station", "alias", "startButton", "name", "startOk"));
        N(doc, "n-rd-stop", FlowNodeKinds.MotionGpioRead, b++, "n-loop", FlowSlots.Body,
            P("deviceId", "gpio-station", "alias", "stopButton", "name", "stopHit"));
        N(doc, "n-rd-door", FlowNodeKinds.MotionGpioRead, b++, "n-loop", FlowSlots.Body,
            P("deviceId", "gpio-station", "alias", "safetyDoor", "name", "doorOk"));
        N(doc, "n-if-stop", FlowNodeKinds.If, b++, "n-loop", FlowSlots.Body, P("condition", "stopHit"));

        N(doc, "n-call-fault", FlowNodeKinds.Call, 0, "n-if-stop", FlowSlots.Then, P("function", "FaultHold"));

        N(doc, "n-if-run", FlowNodeKinds.If, 0, "n-if-stop", FlowSlots.Else, P("condition", "doorOk && startOk"));

        var t = 0;
        N(doc, "n-state-run", FlowNodeKinds.MotionSetTaskVar, t++, "n-if-run", FlowSlots.Then, P("key", "state", "expr", "\"run\""));
        N(doc, "n-y-off", FlowNodeKinds.MotionGpioWrite, t++, "n-if-run", FlowSlots.Then, Io("gpio-station", "tower.yellow", "false"));
        N(doc, "n-ensure", FlowNodeKinds.MotionEnsureDriver, t++, "n-if-run", FlowSlots.Then, P("deviceId", "axis-x"));
        N(doc, "n-en", FlowNodeKinds.MotionAxisEnable, t++, "n-if-run", FlowSlots.Then, P("deviceId", "axis-x", "enabled", "true"));
        N(doc, "n-plat", FlowNodeKinds.MotionPlatformStart, t++, "n-if-run", FlowSlots.Then, P("deviceId", "head-xyz"));
        N(doc, "n-snap", FlowNodeKinds.MotionDeviceSnapshot, t++, "n-if-run", FlowSlots.Then, P("deviceId", "axis-x", "prefix", "snap"));
        N(doc, "n-pick", FlowNodeKinds.MotionAxisMoveTo, t++, "n-if-run", FlowSlots.Then, P("deviceId", "axis-x", "position", "pickPos"));
        N(doc, "n-d1", FlowNodeKinds.Delay, t++, "n-if-run", FlowSlots.Then, P("ms", "100"));
        N(doc, "n-grip-on", FlowNodeKinds.MotionGpioWrite, t++, "n-if-run", FlowSlots.Then, Io("gpio-station", "gripper", "true"));
        N(doc, "n-d2", FlowNodeKinds.Delay, t++, "n-if-run", FlowSlots.Then, P("ms", "50"));
        N(doc, "n-place", FlowNodeKinds.MotionPlatformAxisMoveTo, t++, "n-if-run", FlowSlots.Then,
            P("deviceId", "head-xyz", "axis", "X", "position", "placePos"));
        N(doc, "n-grip-off", FlowNodeKinds.MotionGpioWrite, t++, "n-if-run", FlowSlots.Then, Io("gpio-station", "gripper", "false"));
        N(doc, "n-rd-wp", FlowNodeKinds.MotionGpioRead, t++, "n-if-run", FlowSlots.Then,
            P("deviceId", "gpio-station", "alias", "workpiece.present", "name", "wpPresent"));
        N(doc, "n-if-wp", FlowNodeKinds.If, t++, "n-if-run", FlowSlots.Then, P("condition", "wpPresent"));
        N(doc, "n-jog", FlowNodeKinds.MotionAxisJog, t++, "n-if-run", FlowSlots.Then,
            P("deviceId", "axis-x", "direction", "1", "velocity", "5"));
        N(doc, "n-d4", FlowNodeKinds.Delay, t++, "n-if-run", FlowSlots.Then, P("ms", "30"));
        N(doc, "n-stop", FlowNodeKinds.MotionAxisStop, t++, "n-if-run", FlowSlots.Then, P("deviceId", "axis-x"));
        N(doc, "n-pstop", FlowNodeKinds.MotionPlatformAxisStop, t++, "n-if-run", FlowSlots.Then,
            P("deviceId", "head-xyz", "axis", "X"));
        N(doc, "n-inc", FlowNodeKinds.SetVar, t++, "n-if-run", FlowSlots.Then, P("name", "cycleCount", "expr", "cycleCount + 1"));
        N(doc, "n-done-cnt", FlowNodeKinds.MotionSetGlobalVar, t++, "n-if-run", FlowSlots.Then,
            P("key", "recipe.doneCount", "expr", "cycleCount"));
        N(doc, "n-getp", FlowNodeKinds.MotionGetParam, t++, "n-if-run", FlowSlots.Then, P("key", "jogVel", "name", "tmp"));
        N(doc, "n-log", FlowNodeKinds.OpLog, t++, "n-if-run", FlowSlots.Then, P("message", "\"cycle ok\""));

        N(doc, "n-valve-on", FlowNodeKinds.OpWriteIo, 0, "n-if-wp", FlowSlots.Then, Io("gpio-station", "valve", "true"));
        N(doc, "n-d3", FlowNodeKinds.Delay, 1, "n-if-wp", FlowSlots.Then, P("ms", "80"));
        N(doc, "n-valve-off", FlowNodeKinds.OpWriteIo, 2, "n-if-wp", FlowSlots.Then, Io("gpio-station", "valve", "false"));

        N(doc, "n-if-door", FlowNodeKinds.If, 0, "n-if-run", FlowSlots.Else, P("condition", "!doorOk"));
        N(doc, "n-door-red", FlowNodeKinds.MotionGpioWrite, 0, "n-if-door", FlowSlots.Then, Io("gpio-station", "tower.red", "true"));
        N(doc, "n-door-log", FlowNodeKinds.OpLog, 1, "n-if-door", FlowSlots.Then, P("message", "\"safety door\""));
        N(doc, "n-wait", FlowNodeKinds.Delay, 1, "n-if-run", FlowSlots.Else, P("ms", "50"));

        doc.SyncEdgesFromTree();
        AddFaultHold(doc);
        return doc;
    }

    public static MdkSetting CreateSetting()
    {
        var setting = new MdkSetting
        {
            ProjectName = "工位节拍示例（IEC 导出）",
            CycleMs = 20,
            MonitoringPrefix = "http://127.0.0.1:5090/",
            Vars = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["machine.mode"] = "AUTO",
                ["machine.ready"] = false,
                ["recipe.targetCount"] = 3,
                ["recipe.doneCount"] = 0,
            },
        };

        setting.Drivers.Add(new MdkSetting.DriverConfig
        {
            Id = "drv-motion",
            Type = "sim",
            Enabled = true,
            Parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["note"] = "运动仿真（导出后由 PLC 轴 FB 承接）",
            },
        });
        setting.Drivers.Add(new MdkSetting.DriverConfig
        {
            Id = "drv-io",
            Type = "s7",
            Enabled = true,
            Parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["simulate"] = "true",
                ["cpu"] = "S71200",
                ["note"] = "IO 按 S7 映像区映射",
            },
        });
        setting.Devices.Add(new MdkSetting.DeviceConfig
        {
            Id = "gpio-station",
            Name = "工位 IO",
            Type = "gpio",
            Enabled = true,
            Parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["in.startButton"] = "drv-io|di.gpi.bit.0|启动",
                ["in.stopButton"] = "drv-io|di.gpi.bit.1|停止",
                ["in.resetButton"] = "drv-io|di.gpi.bit.2|复位",
                ["in.safetyDoor"] = "drv-io|di.gpi.bit.3|安全门",
                ["in.workpiece.present"] = "drv-io|di.gpi.bit.4|工件到位",
                ["out.tower.red"] = "drv-io|do.gpo.bit.0|红灯",
                ["out.tower.yellow"] = "drv-io|do.gpo.bit.1|黄灯",
                ["out.tower.green"] = "drv-io|do.gpo.bit.2|绿灯",
                ["out.buzzer"] = "drv-io|do.gpo.bit.3|蜂鸣器",
                ["out.gripper"] = "drv-io|do.gpo.bit.4|夹爪",
                ["out.valve"] = "drv-io|do.gpo.bit.5|阀",
            },
        });
        setting.Axes.Add(new MdkSetting.DeviceConfig
        {
            Id = "axis-x",
            Name = "X",
            Type = "linear",
            DriverId = "drv-motion",
            Enabled = true,
            Parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["axis"] = "0",
                ["unit"] = "mm",
            },
        });
        setting.Platforms.Add(new MdkSetting.DeviceConfig
        {
            Id = "head-xyz",
            Name = "工位平台",
            Type = "xyz",
            DriverId = "drv-motion",
            Enabled = true,
        });
        setting.Tasks.Add(new MdkSetting.TaskConfig
        {
            Name = "station-cycle",
            Type = "flow",
            IntervalMs = 20,
            Parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["loop"] = "false",
                ["flowFile"] = "station.flow.json",
                ["note"] = "工位节拍：等启动 → 取放 → 计件",
            },
        });
        return setting;
    }

    private static void AddFaultHold(FlowDocument doc)
    {
        string[] chain =
        [
            "f-start", "f-axis-stop", "f-plat-stop", "f-paxis-stop", "f-grip", "f-valve",
            "f-red", "f-buzz", "f-state", "f-log", "f-end",
        ];
        doc.Nodes.Add(Node("f-start", FlowNodeKinds.Start));
        doc.Nodes.Add(Node("f-axis-stop", FlowNodeKinds.MotionAxisStop, P("deviceId", "axis-x")));
        doc.Nodes.Add(Node("f-plat-stop", FlowNodeKinds.MotionPlatformStop, P("deviceId", "head-xyz")));
        doc.Nodes.Add(Node("f-paxis-stop", FlowNodeKinds.MotionPlatformAxisStop, P("deviceId", "head-xyz", "axis", "X")));
        doc.Nodes.Add(Node("f-grip", FlowNodeKinds.MotionGpioWrite, Io("gpio-station", "gripper", "false")));
        doc.Nodes.Add(Node("f-valve", FlowNodeKinds.OpWriteIo, Io("gpio-station", "valve", "false")));
        doc.Nodes.Add(Node("f-red", FlowNodeKinds.MotionGpioWrite, Io("gpio-station", "tower.red", "true")));
        doc.Nodes.Add(Node("f-buzz", FlowNodeKinds.MotionGpioWrite, Io("gpio-station", "buzzer", "true")));
        doc.Nodes.Add(Node("f-state", FlowNodeKinds.MotionSetTaskVar, P("key", "state", "expr", "\"fault\"")));
        doc.Nodes.Add(Node("f-log", FlowNodeKinds.OpLog, P("message", "\"fault hold\"")));
        doc.Nodes.Add(Node("f-end", FlowNodeKinds.End));
        for (var i = 0; i < chain.Length - 1; i++)
        {
            doc.Edges.Add(new FlowEdge { From = chain[i], To = chain[i + 1], Port = FlowPorts.Next });
        }
    }

    private static FlowVariable V(string name, string type, string init) =>
        new() { Name = name, Type = type, Init = init };

    private static Dictionary<string, string> P(params string[] kv)
    {
        var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i + 1 < kv.Length; i += 2)
        {
            d[kv[i]] = kv[i + 1];
        }

        return d;
    }

    private static Dictionary<string, string> Io(string deviceId, string alias, string value) =>
        P("deviceId", deviceId, "alias", alias, "value", value);

    private static void N(
        FlowDocument doc,
        string id,
        string kind,
        int order,
        string? parent = null,
        string? slot = null,
        Dictionary<string, string>? props = null)
    {
        doc.Nodes.Add(new FlowNode
        {
            Id = id,
            Kind = kind,
            Order = order,
            ParentId = parent,
            Slot = slot,
            Props = props ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
        });
    }

    private static FlowNode Node(string id, string kind, Dictionary<string, string>? props = null) =>
        new()
        {
            Id = id,
            Kind = kind,
            Props = props ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
        };
}
