using System.Text.Json;
using MDKOSS.Core;
using MDKOSS.Core.Drivers;
using MDKOSS.Extensions.ModServer;
using MDKOSS.Sample.Modbus.Machine;

namespace MDKOSS.Tests.Sample.Modbus;

public sealed class PlcRegisterCatalogTests
{
    [Fact]
    public void LoadFromJs_parses_reg_regi_regf_and_bits()
    {
        const string js = """
            const plcRegisters = [
              { index: 1, address: '0', name: 'encoder', type: 'REGI', description: 'enc', plcAddress: '(VD0)', access: 'R' },
              { index: 2, address: '1', name: '', type: 'REG', description: '', plcAddress: '', access: '' },
              { index: 3, address: 'B', name: 'weight_act', type: 'REGF', description: 'kg', plcAddress: '(VD22)', access: 'R' },
              { index: 4, address: 'C', name: '', type: 'REG', description: '', plcAddress: '', access: '' },
              { index: 5, address: '4', name: 'alarm', type: 'REG', description: 'alm', plcAddress: '', access: '',
                bits: [ { bit: '15', name: 'cyl', description: '气缸', plcAddress: '(V8.7)', access: '1' } ] },
            ];
            """;

        var catalog = PlcRegisterCatalog.LoadFromJs(js, "inline.js");
        Assert.Contains(catalog.Points, p => p.Id == "encoder" && p.Type == "regi" && p.Address == 0 && p.WordCount == 2);
        Assert.Contains(catalog.Points, p => p.IsContinuation && p.Address == 1);
        Assert.Contains(catalog.Points, p => p.Id == "weight_act" && p.Type == "regf" && p.Address == 11);
        Assert.Contains(catalog.Points, p => p.Type == "bit" && p.Bit == 15 && p.Address == 4);
        Assert.Contains(catalog.Points, p => p.Address == 4 && p.Group == "贴标故障");
    }

    [Fact]
    public void Load_real_plc_registers_json_when_present()
    {
        var dir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..",
            "src", "MDKOSS.Sample.Modbus", "configs"));
        var json = Path.Combine(dir, PlcConfigFiles.RegistersJson);
        var js = Path.Combine(dir, PlcConfigFiles.RegistersJs);
        if (!File.Exists(json) && File.Exists(js))
        {
            PlcConfigFiles.ExportJsToJson(dir, overwrite: false);
        }

        if (!File.Exists(json))
        {
            return;
        }

        var catalog = PlcRegisterCatalog.LoadFromJson(File.ReadAllText(json), json);
        Assert.True(catalog.Points.Count > 20);
        Assert.Contains(catalog.Points, p => p.Type == "regi");
        Assert.Contains(catalog.Points, p => p.Type == "regf");
        Assert.Contains(catalog.Points, p => p.Type == "bit");
        Assert.Contains(catalog.Points, p => p.Type == "reg");
    }

    [Fact]
    public void Roundtrip_reg_regi_regf_bit()
    {
        using var drv = CreateSimDriver();
        var regi = new PlcRegisterPoint { Id = "encoder", Type = "regi", Address = 0, WordCount = 2, Writable = true };
        var regf = new PlcRegisterPoint { Id = "weight", Type = "regf", Address = 10, WordCount = 2, Writable = true };
        var bit = new PlcRegisterPoint { Id = "alm", Type = "bit", Address = 4, Bit = 3, Writable = true };
        var reg = new PlcRegisterPoint { Id = "mode", Type = "reg", Address = 20, Writable = true };

        Assert.True(PlcRegisterAccess.TryWrite(drv, regi, Json("123456")));
        Assert.True(PlcRegisterAccess.TryRead(drv, regi, out var iv));
        Assert.Equal(123456, Convert.ToInt32(iv));

        Assert.True(PlcRegisterAccess.TryWrite(drv, regf, Json("12.5")));
        Assert.True(PlcRegisterAccess.TryRead(drv, regf, out var fv));
        Assert.Equal(12.5f, Convert.ToSingle(fv), 3);

        Assert.True(PlcRegisterAccess.TryWrite(drv, bit, Json("true")));
        Assert.True(PlcRegisterAccess.TryRead(drv, bit, out var bv));
        Assert.Equal(true, bv);

        Assert.True(PlcRegisterAccess.TryWrite(drv, reg, Json("65500")));
        Assert.True(PlcRegisterAccess.TryRead(drv, reg, out var rv));
        Assert.Equal((ushort)65500, Convert.ToUInt16(rv));
    }

    [Fact]
    public void Default_layout_groups_named_registers()
    {
        var catalog = PlcRegisterCatalog.LoadFromJs("""
            const plcRegisters = [
              { index: 1, address: '0', name: 'encoder', type: 'REGI', description: '', plcAddress: '', access: '' },
              { index: 2, address: '1', name: '', type: 'REG', description: '', plcAddress: '', access: '' },
              { index: 3, address: '10', name: 'op_start', type: 'DO', description: '', plcAddress: '', access: '' },
            ];
            """, "t.js");
        var layout = ModbusHmiLayoutStore.CreateDefault(catalog);
        Assert.Equal(100, layout.RefreshMs);
        Assert.Contains(layout.Widgets, w => w.PointId == "encoder" && w.Kind == "regi");
        Assert.Contains(layout.Widgets, w => w.PointId == "op_start");
        Assert.DoesNotContain(layout.Widgets, w => string.IsNullOrEmpty(w.PointId) is false && catalog.Find(w.PointId)?.IsContinuation == true);
    }

    [Fact]
    public void ToPanels_maps_types_commands_and_skips_continuation()
    {
        var catalog = PlcRegisterCatalog.LoadFromJs("""
            const plcRegisters = [
              { index: 1, address: '0', name: 'encoder', type: 'REGI', description: '编码器当前累计数 高16位', plcAddress: '(VD0)', access: 'R' },
              { index: 2, address: '1', name: '', type: 'REG', description: '', plcAddress: '', access: '' },
              { index: 3, address: '12', name: 'set_rollerm_vf', type: 'REGF', description: '设定牵引电机速度 米/S', plcAddress: '', access: 'W' },
              { index: 4, address: '13', name: '', type: 'REG', description: '', plcAddress: '', access: '' },
              { index: 5, address: '22', name: 'op_start', type: 'DO', description: '', plcAddress: '', access: '' },
              { index: 6, address: '2F', name: 'do蜂鸣器等控制', type: 'REG', description: '', plcAddress: '', access: '',
                bits: [ { bit: '05', description: '(V95.5)', plcAddress: '1', access: '贴标机归零' } ] },
            ];
            """, "t.js");

        var panels = catalog.ToPanels();
        Assert.Equal(100, panels.RefreshMs);
        Assert.Equal("encoder", panels.MainDisplay.PositionPointId);
        Assert.Contains(panels.Commands, c => c.Id == "cmd_start" && c.PointId == "op_start" && c.Kind == "set" && c.Value == 1);
        Assert.Contains(panels.Commands, c => c.Id == "cmd_stop" && c.PointId == "op_start" && c.Value == 0);
        Assert.Contains(panels.Commands, c => c.Label == "贴标机归零" && c.Kind == "pulse");
        Assert.DoesNotContain(panels.Panels.SelectMany(p => p.Fields), f => catalog.Find(f.PointId)?.IsContinuation == true);

        var enc = panels.Panels.SelectMany(p => p.Fields).First(f => f.Id == "encoder");
        Assert.Equal("int", enc.Type);
        Assert.Equal(0, enc.Addr);
        Assert.Contains("编码器", enc.Label);

        var vf = panels.Panels.SelectMany(p => p.Fields).First(f => f.Id == "set_rollerm_vf");
        Assert.Equal("float", vf.Type);
        Assert.Equal("m/s", vf.Unit);
        Assert.True(vf.Writable);

        var start = panels.Panels.SelectMany(p => p.Fields).First(f => f.Id == "op_start");
        Assert.Equal("bit", start.Type);
        Assert.Contains(panels.Panels.SelectMany(p => p.Fields), f => f.Label == "贴标机归零" && f.Bit == 5);

        var json = PlcPanelExport.ToJson(panels);
        Assert.Contains("\"id\": \"encoder\"", json);
        var roundtrip = System.Text.Json.JsonSerializer.Deserialize<PlcPanelConfig>(json, new System.Text.Json.JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase,
        });
        Assert.NotNull(roundtrip);
        Assert.Contains(roundtrip!.Panels.SelectMany(p => p.Fields), f => f.Id == "encoder" && f.Type == "int");
    }

    [Fact]
    public void ParsePlcConfigJs_loads_panels_and_binds_catalog_points()
    {
        const string js = """
            const PLC_PANELS = {
              // mainDisplay: { title: "skip" },
              realTimeStatus: {
                title: "实时状态",
                fields: [
                  { id: "encoder-value", label: "编码器值", type: "int", addr: 0 },
                  { id: "flag", label: "气缸", type: "bit", addr: 4, bit: 15 },
                ],
              },
            };
            """;
        var catalog = PlcRegisterCatalog.LoadFromJs("""
            const plcRegisters = [
              { index: 1, address: '0', name: 'encoder', type: 'REGI', description: 'enc', plcAddress: '', access: 'R' },
              { index: 2, address: '1', name: '', type: 'REG', description: '', plcAddress: '', access: '' },
              { index: 3, address: '4', name: 'alarm', type: 'REG', description: 'alm', plcAddress: '', access: '',
                bits: [ { bit: '15', name: 'cyl', description: '气缸', plcAddress: '(V8.7)', access: '1' } ] },
            ];
            """, "t.js");

        var cfg = PlcPanelExport.ParsePlcConfigJs(js, catalog, "plcconfig.js");
        Assert.Contains(cfg.Source, "plcconfig.js");
        Assert.Equal("实时状态", Assert.Single(cfg.Panels).Title);
        var enc = cfg.Panels[0].Fields.First(f => f.Id == "encoder-value");
        Assert.Equal("encoder", enc.PointId);
        Assert.Equal("int", enc.Type);
        Assert.Equal(0, enc.Addr);
        var bit = cfg.Panels[0].Fields.First(f => f.Id == "flag");
        Assert.Equal(4, bit.Addr);
        Assert.Equal(15, bit.Bit);
        Assert.NotEmpty(bit.PointId);
    }

    [Fact]
    public void ParsePlcConfigJs_unmapped_addr_stays_writable_and_augmentable()
    {
        const string js = """
            const PLC_PANELS = {
              systemParameters: {
                title: "系统参数",
                fields: [
                  { id: "Unwinding-Correction-Method", label: "放卷纠偏方式", type: "correctionMethod", addr: 110, bit: 0,
                    offLabel: "电缸纠偏", onLabel: "齿轮齿条纠偏" },
                ],
              },
            };
            """;
        var cfg = PlcPanelExport.ParsePlcConfigJs(js, PlcRegisterCatalog.Empty, "plcconfig.js");
        var field = Assert.Single(Assert.Single(cfg.Panels).Fields);
        Assert.Equal("Unwinding-Correction-Method", field.PointId);
        Assert.True(field.Writable);
        Assert.Equal(110, field.Addr);
        Assert.Equal(0, field.Bit);
        var merged = PlcPanelExport.AugmentCatalog(PlcRegisterCatalog.Empty, cfg);
        Assert.Contains(merged.Points, p => p.Id == field.PointId && p.Type == "bit" && p.Bit == 0 && p.Address == 110);
    }

    [Fact]
    public void ExportJsToJson_writes_runtime_json_and_Load_ignores_js()
    {
        var dir = Path.Combine(Path.GetTempPath(), "mdkoss-plc-json-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, PlcConfigFiles.RegistersJs), """
                const plcRegisters = [
                  { index: 1, address: '0', name: 'encoder', type: 'REGI', description: 'enc', plcAddress: '', access: 'R' },
                  { index: 2, address: '1', name: '', type: 'REG', description: '', plcAddress: '', access: '' },
                ];
                """);
            File.WriteAllText(Path.Combine(dir, PlcConfigFiles.PanelsJs), """
                const PLC_PANELS = {
                  realTimeStatus: {
                    title: "实时状态",
                    fields: [
                      { id: "encoder-value", label: "编码器值", type: "int", addr: 0 },
                    ],
                  },
                };
                """);

            Assert.Equal(2, PlcConfigFiles.ExportJsToJson(dir, overwrite: true));
            Assert.True(File.Exists(Path.Combine(dir, PlcConfigFiles.RegistersJson)));
            Assert.True(File.Exists(Path.Combine(dir, PlcConfigFiles.PanelsJson)));

            File.WriteAllText(Path.Combine(dir, PlcConfigFiles.RegistersJs), "const plcRegisters = [];");
            File.WriteAllText(Path.Combine(dir, PlcConfigFiles.PanelsJs), "const PLC_PANELS = {};");

            var setting = Path.Combine(dir, "sample.setting.json");
            File.WriteAllText(setting, "{}");
            var catalog = PlcRegisterCatalog.Load(setting, dir);
            Assert.Contains(catalog.Points, p => p.Id == "encoder" && p.Type == "regi");

            var panels = PlcPanelExport.TryLoad(setting, dir, catalog);
            Assert.NotNull(panels);
            Assert.Equal("实时状态", Assert.Single(panels!.Panels).Title);
            Assert.Equal("encoder", panels.Panels[0].Fields[0].PointId);
            Assert.Contains(PlcConfigFiles.PanelsJson, panels.Source, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* temp */ }
        }
    }

    [Fact]
    public void Export_repo_js_to_json_when_present()
    {
        var dir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..",
            "src", "MDKOSS.Sample.Modbus", "configs"));
        if (!Directory.Exists(dir))
        {
            return;
        }

        if (!File.Exists(Path.Combine(dir, PlcConfigFiles.RegistersJs))
            && !File.Exists(Path.Combine(dir, PlcConfigFiles.PanelsJs)))
        {
            return;
        }

        PlcConfigFiles.ExportJsToJson(dir, overwrite: false);
        var jsonReg = Path.Combine(dir, PlcConfigFiles.RegistersJson);
        var jsonPan = Path.Combine(dir, PlcConfigFiles.PanelsJson);
        Assert.True(File.Exists(jsonReg));
        Assert.True(File.Exists(jsonPan));
        var catalog = PlcRegisterCatalog.LoadFromJson(File.ReadAllText(jsonReg), jsonReg);
        Assert.True(catalog.Points.Count > 20);
        var cfg = PlcPanelExport.TryLoad(jsonReg, dir, catalog);
        Assert.NotNull(cfg);
        Assert.True(cfg!.Panels.Count >= 8);
        Assert.Contains(cfg.Panels, p => p.Id == "realTimeStatus" && p.Title == "实时状态");
        Assert.Contains(cfg.Panels, p => p.Id == "manualControl");
        Assert.All(cfg.Panels.SelectMany(p => p.Fields), f =>
        {
            Assert.False(string.IsNullOrWhiteSpace(f.Id));
            Assert.False(string.IsNullOrWhiteSpace(f.PointId));
            Assert.True(f.Writable);
        });
        Assert.Contains(cfg.Panels.SelectMany(p => p.Fields), f => f.Type == "correctionMethod" && f.OffLabel == "电缸纠偏");
    }

    private static JsonElement Json(string literal)
    {
        var json = literal.Trim() switch
        {
            "true" or "false" => literal.Trim(),
            var s when double.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out _) => s,
            _ => System.Text.Json.JsonSerializer.Serialize(literal),
        };
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }

    private static DrvModbus CreateSimDriver()
    {
        var drv = new DrvModbus();
        drv.Initialize(new MdkSetting.DriverConfig
        {
            Id = "drv-modbus",
            Type = "modbus-tcp",
            Enabled = true,
            Parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["simulate"] = "true",
            },
        });
        return drv;
    }
}
