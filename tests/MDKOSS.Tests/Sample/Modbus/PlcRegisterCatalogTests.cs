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
    public void Load_real_plc_registers_js_when_present()
    {
        var path = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..",
            "src", "MDKOSS.Sample.Modbus", "configs", "plc_registers.js"));
        if (!File.Exists(path))
        {
            return;
        }

        var catalog = PlcRegisterCatalog.LoadFromJs(File.ReadAllText(path), path);
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
