using MDKOSS.Core;
using MDKOSS.Core.Flow;
using MDKOSS.Iec61131;

namespace MDKOSS.Tests.Iec61131;

public sealed class IecExprTests
{
    [Fact]
    public void Translates_operators_and_idents()
    {
        var st = IecExpr.ToSt("doorOk && startOk", ident => IecNames.Sanitize(ident));
        Assert.Contains("AND", st, StringComparison.Ordinal);
        Assert.Contains("doorOk", st, StringComparison.Ordinal);
    }

    [Fact]
    public void Translates_compare_and_reals()
    {
        var st = IecExpr.ToSt("cycleCount < 3");
        Assert.DoesNotContain("<>", st, StringComparison.Ordinal);
        Assert.Contains("<", st, StringComparison.Ordinal);
        Assert.Contains("3.0", st, StringComparison.Ordinal);
    }

    [Fact]
    public void Translates_strings_and_not_equal()
    {
        var st = IecExpr.ToSt("x != \"idle\"");
        Assert.Contains("<>", st, StringComparison.Ordinal);
        Assert.Contains("'idle'", st, StringComparison.Ordinal);
    }
}

public sealed class IecIoMapperTests
{
    [Fact]
    public void Maps_gpi_bit_to_s7_address()
    {
        var setting = new MdkSetting();
        setting.Devices.Add(new MdkSetting.DeviceConfig
        {
            Id = "gpio1",
            Type = "gpio",
            Enabled = true,
            Parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["in.startButton"] = "drv|di.gpi.bit.0|启动",
                ["out.valve"] = "drv|do.gpo.bit.5|阀",
            },
        });
        var notes = new List<IecNote>();
        var points = IecIoMapper.FromSetting(setting, notes);
        Assert.Contains(points, p => p.Alias == "startButton" && p.AtAddress == "%I0.0" && !p.IsOutput);
        Assert.Contains(points, p => p.Alias == "valve" && p.AtAddress == "%Q0.5" && p.IsOutput);
    }
}

public sealed class IecExportTests
{
    [Fact]
    public void Exports_station_like_flow_to_scl()
    {
        var setting = MiniStation();
        var project = IecProjectBuilder.FromSetting(setting);
        Assert.DoesNotContain(project.Notes, n => n.Severity == "error");
        Assert.Contains(project.Pous, p => p.Name == IecNames.PouFb("station-cycle"));
        Assert.Contains(project.Pous, p => p.Name == IecNames.ProgramMain());

        var files = SclWriter.WriteFiles(project);
        var mainFb = files.Values.First(v => v.Contains("FUNCTION_BLOCK " + IecNames.PouFb("station-cycle"), StringComparison.Ordinal));
        Assert.Contains("CASE iStep OF", mainFb, StringComparison.Ordinal);
        Assert.Contains("Q_valve", mainFb, StringComparison.Ordinal);
        Assert.Contains("FB_AxisMoveTo", mainFb, StringComparison.Ordinal);
        Assert.Contains("TON", mainFb, StringComparison.Ordinal);
        Assert.Contains("AND", mainFb, StringComparison.Ordinal);

        var gvl = files["scl/00_GVL_MdkVars.scl"];
        Assert.Contains("%I0.0", gvl, StringComparison.Ordinal);
        Assert.Contains("machine_ready", gvl, StringComparison.Ordinal);

        var xml = PlcOpenXmlWriter.Write(project);
        Assert.Contains("plcopen.org/xml/tc6_0201", xml, StringComparison.Ordinal);
        Assert.Contains("PROGRAM_Main", xml, StringComparison.Ordinal);
    }

    [Fact]
    public void Write_creates_export_folder()
    {
        var dir = Path.Combine(Path.GetTempPath(), "mdkoss-iec-" + Guid.NewGuid().ToString("N"));
        try
        {
            var result = IecExport.Write(IecProjectBuilder.FromSetting(MiniStation()), dir);
            Assert.True(File.Exists(Path.Combine(dir, "plcopen.xml")));
            Assert.True(File.Exists(Path.Combine(dir, "mapping.json")));
            Assert.True(Directory.Exists(Path.Combine(dir, "scl")));
            Assert.NotEmpty(result.Files);
        }
        finally
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
            }
        }
    }

    [Fact]
    public void Covers_every_flow_node_kind()
    {
        var doc = AllKindsDocument();
        Assert.Empty(doc.Validate());
        var setting = new MdkSetting { ProjectName = "all-kinds" };
        setting.Devices.Add(new MdkSetting.DeviceConfig
        {
            Id = "g",
            Type = "gpio",
            Enabled = true,
            Parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["out.y"] = "d|do.gpo.bit.0",
                ["in.x"] = "d|di.gpi.bit.0",
            },
        });
        setting.Vars["g1"] = true;
        setting.Tasks.Add(new MdkSetting.TaskConfig
        {
            Name = "all",
            Type = "flow",
            Parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["loop"] = "false",
                ["flowJson"] = doc.ToJson(),
            },
        });
        var project = IecProjectBuilder.FromSetting(setting);
        Assert.DoesNotContain(project.Notes, n => n.Severity == "error");
        var kinds = project.NodeMaps.Select(m => m.Kind).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var kind in FlowNodeKinds.All)
        {
            Assert.Contains(kind, kinds);
        }
    }

    private static MdkSetting MiniStation()
    {
        var doc = new FlowDocument
        {
            Version = 1,
            Variables =
            [
                new FlowVariable { Name = "n", Type = "number", Init = "0" },
                new FlowVariable { Name = "ok", Type = "bool", Init = "false" },
            ],
            Functions = [new FlowFunction { Name = "main", EntryNodeId = "s" }],
            Nodes =
            [
                new FlowNode { Id = "s", Kind = FlowNodeKinds.Start, Order = 0 },
                new FlowNode
                {
                    Id = "w",
                    Kind = FlowNodeKinds.MotionGpioWrite,
                    Order = 1,
                    Props = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["deviceId"] = "gpio1",
                        ["alias"] = "valve",
                        ["value"] = "true",
                    },
                },
                new FlowNode
                {
                    Id = "i",
                    Kind = FlowNodeKinds.If,
                    Order = 2,
                    Props = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["condition"] = "machine.ready && n < 3",
                    },
                },
                new FlowNode
                {
                    Id = "m",
                    Kind = FlowNodeKinds.MotionAxisMoveTo,
                    Order = 0,
                    ParentId = "i",
                    Slot = FlowSlots.Then,
                    Props = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["deviceId"] = "axis-x",
                        ["position"] = "10",
                    },
                },
                new FlowNode
                {
                    Id = "d",
                    Kind = FlowNodeKinds.Delay,
                    Order = 1,
                    ParentId = "i",
                    Slot = FlowSlots.Then,
                    Props = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["ms"] = "50" },
                },
                new FlowNode { Id = "e", Kind = FlowNodeKinds.End, Order = 3 },
            ],
        };
        doc.SyncEdgesFromTree();

        var setting = new MdkSetting { ProjectName = "mini" };
        setting.Vars["machine.ready"] = true;
        setting.Devices.Add(new MdkSetting.DeviceConfig
        {
            Id = "gpio1",
            Type = "gpio",
            Enabled = true,
            Parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["in.startButton"] = "drv|di.gpi.bit.0|启动",
                ["out.valve"] = "drv|do.gpo.bit.5|阀",
            },
        });
        setting.Tasks.Add(new MdkSetting.TaskConfig
        {
            Name = "station-cycle",
            Type = "flow",
            Parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["loop"] = "false",
                ["flowJson"] = doc.ToJson(),
            },
        });
        setting.Tasks.Add(new MdkSetting.TaskConfig { Name = "pc-only", Type = "motion" });
        return setting;
    }

    private static FlowDocument AllKindsDocument()
    {
        var doc = new FlowDocument { Version = 1 };
        doc.Variables.Add(new FlowVariable { Name = "x", Type = "number", Init = "0" });
        doc.Functions.Add(new FlowFunction { Name = "main", EntryNodeId = "n-start" });
        doc.Functions.Add(new FlowFunction { Name = "sub", EntryNodeId = "u-start" });
        var o = 0;
        void Root(string id, string kind, Dictionary<string, string>? props = null) =>
            doc.Nodes.Add(new FlowNode { Id = id, Kind = kind, Order = o++, Props = props ?? [] });

        Root("n-start", FlowNodeKinds.Start);
        Root("n-decl", FlowNodeKinds.DeclareVar, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            { ["name"] = "y", ["type"] = "number", ["init"] = "1" });
        Root("n-set", FlowNodeKinds.SetVar, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            { ["name"] = "x", ["expr"] = "x + 1" });
        Root("n-if", FlowNodeKinds.If, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            { ["condition"] = "true" });
        doc.Nodes.Add(new FlowNode
        {
            Id = "n-then-log",
            Kind = FlowNodeKinds.OpLog,
            ParentId = "n-if",
            Slot = FlowSlots.Then,
            Order = 0,
            Props = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["message"] = "\"t\"" },
        });
        Root("n-while", FlowNodeKinds.While, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            { ["condition"] = "false" });
        doc.Nodes.Add(new FlowNode
        {
            Id = "n-body",
            Kind = FlowNodeKinds.Delay,
            ParentId = "n-while",
            Slot = FlowSlots.Body,
            Order = 0,
            Props = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["ms"] = "0" },
        });
        Root("n-delay", FlowNodeKinds.Delay, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["ms"] = "10" });
        Root("n-call", FlowNodeKinds.Call, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["function"] = "sub" });
        Root("n-wio", FlowNodeKinds.OpWriteIo, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            { ["deviceId"] = "g", ["alias"] = "y", ["value"] = "true" });
        Root("n-act", FlowNodeKinds.OpDeviceAction, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            { ["deviceId"] = "g", ["action"] = "pulse", ["parametersJson"] = "{}" });
        Root("n-log", FlowNodeKinds.OpLog, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["message"] = "\"k\"" });
        Root("n-mv", FlowNodeKinds.MotionAxisMoveTo, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            { ["deviceId"] = "a", ["position"] = "1" });
        Root("n-en", FlowNodeKinds.MotionAxisEnable, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            { ["deviceId"] = "a", ["enabled"] = "true" });
        Root("n-jg", FlowNodeKinds.MotionAxisJog, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            { ["deviceId"] = "a", ["direction"] = "1", ["velocity"] = "1" });
        Root("n-st", FlowNodeKinds.MotionAxisStop, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            { ["deviceId"] = "a" });
        Root("n-ps", FlowNodeKinds.MotionPlatformSetMotion, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            { ["deviceId"] = "p", ["enabled"] = "true" });
        Root("n-pstart", FlowNodeKinds.MotionPlatformStart, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            { ["deviceId"] = "p" });
        Root("n-pstop", FlowNodeKinds.MotionPlatformStop, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            { ["deviceId"] = "p" });
        Root("n-pmv", FlowNodeKinds.MotionPlatformAxisMoveTo, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            { ["deviceId"] = "p", ["axis"] = "X", ["position"] = "2" });
        Root("n-pj", FlowNodeKinds.MotionPlatformAxisJog, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            { ["deviceId"] = "p", ["axis"] = "X", ["direction"] = "1", ["velocity"] = "1" });
        Root("n-pst", FlowNodeKinds.MotionPlatformAxisStop, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            { ["deviceId"] = "p", ["axis"] = "X" });
        Root("n-gw", FlowNodeKinds.MotionGpioWrite, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            { ["deviceId"] = "g", ["alias"] = "y", ["value"] = "false" });
        Root("n-gr", FlowNodeKinds.MotionGpioRead, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            { ["deviceId"] = "g", ["alias"] = "x", ["name"] = "ok" });
        Root("n-sn", FlowNodeKinds.MotionDeviceSnapshot, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            { ["deviceId"] = "a", ["prefix"] = "snap" });
        Root("n-ed", FlowNodeKinds.MotionEnsureDriver, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            { ["deviceId"] = "a" });
        Root("n-sp", FlowNodeKinds.MotionSetParam, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            { ["key"] = "k", ["expr"] = "1" });
        Root("n-gp", FlowNodeKinds.MotionGetParam, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            { ["key"] = "k", ["name"] = "x" });
        Root("n-tv", FlowNodeKinds.MotionSetTaskVar, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            { ["key"] = "st", ["expr"] = "\"run\"" });
        Root("n-gv", FlowNodeKinds.MotionSetGlobalVar, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            { ["key"] = "g1", ["expr"] = "true" });
        Root("n-end", FlowNodeKinds.End);
        doc.SyncEdgesFromTree();
        doc.Nodes.Add(new FlowNode { Id = "u-start", Kind = FlowNodeKinds.Start });
        doc.Nodes.Add(new FlowNode { Id = "u-end", Kind = FlowNodeKinds.End });
        doc.Edges.Add(new FlowEdge { From = "u-start", To = "u-end", Port = FlowPorts.Next });
        return doc;
    }
}
