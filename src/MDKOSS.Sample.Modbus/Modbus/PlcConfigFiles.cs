namespace MDKOSS.Sample.Modbus.Machine;

/// <summary>
/// Converts leftover local <c>.js</c> maps into runtime JSON. Loaders never read JS.
/// </summary>
public static class PlcConfigFiles
{
    public const string RegistersJs = "plc_registers.js";
    public const string RegistersJson = "plc_registers.json";
    public const string PanelsJs = "plcconfig.js";
    public const string PanelsJson = "plc_panels.json";

    /// <summary>
    /// Writes <c>plc_registers.json</c> / <c>plc_panels.json</c> from local JS if present.
    /// </summary>
    public static int ExportJsToJson(string dir, bool overwrite = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dir);
        Directory.CreateDirectory(dir);
        var written = 0;

        PlcRegisterCatalog? catalog = null;
        var jsReg = Path.Combine(dir, RegistersJs);
        var jsonReg = Path.Combine(dir, RegistersJson);
        if (File.Exists(jsReg) && (overwrite || !File.Exists(jsonReg)))
        {
            catalog = PlcRegisterCatalog.LoadFromJs(File.ReadAllText(jsReg), jsReg);
            File.WriteAllText(jsonReg, catalog.ToJson());
            written++;
        }
        else if (File.Exists(jsonReg))
        {
            catalog = PlcRegisterCatalog.LoadFromJson(File.ReadAllText(jsonReg), jsonReg);
        }

        var jsPan = Path.Combine(dir, PanelsJs);
        var jsonPan = Path.Combine(dir, PanelsJson);
        if (File.Exists(jsPan) && (overwrite || !File.Exists(jsonPan)))
        {
            var cfg = PlcPanelExport.ParsePlcConfigJs(File.ReadAllText(jsPan), catalog, jsPan);
            File.WriteAllText(jsonPan, PlcPanelExport.ToJson(cfg));
            written++;
        }

        return written;
    }
}
