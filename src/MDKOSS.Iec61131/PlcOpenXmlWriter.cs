using System.Globalization;
using System.Net;
using System.Text;

namespace MDKOSS.Iec61131;

/// <summary>PLCopen TC6 XML (IEC 61131-10 interchange) for TIA / CODESYS import.</summary>
public static class PlcOpenXmlWriter
{
    public static string Write(IecProject project)
    {
        var stFiles = SclWriter.WriteFiles(project);
        var sb = new StringBuilder();
        sb.AppendLine(@"<?xml version=""1.0"" encoding=""utf-8""?>");
        sb.AppendLine(@"<project xmlns=""http://www.plcopen.org/xml/tc6_0201"">");
        // Fixed timestamp keeps checked-in sample exports stable across regenerations.
        sb.AppendLine(@"  <fileHeader companyName=""MDKOSS"" productName=""MDKOSS.Iec61131"" productVersion=""1.0"" creationDateTime=""2000-01-01T00:00:00""/>");
        sb.AppendLine($@"  <contentHeader name=""{Xml(project.Name)}"">");
        sb.AppendLine(@"    <coordinateInfo>");
        sb.AppendLine(@"      <fbd><scaling x=""1"" y=""1""/></fbd>");
        sb.AppendLine(@"      <ld><scaling x=""1"" y=""1""/></ld>");
        sb.AppendLine(@"      <sfc><scaling x=""1"" y=""1""/></sfc>");
        sb.AppendLine(@"    </coordinateInfo>");
        sb.AppendLine(@"  </contentHeader>");
        sb.AppendLine(@"  <types>");
        sb.AppendLine(@"    <dataTypes/>");
        sb.AppendLine(@"    <pous>");

        foreach (var pou in project.Pous)
        {
            var pouType = pou.Kind == IecPouKind.Program ? "program" : "functionBlock";
            var body = pou.Kind == IecPouKind.Program
                ? SclWriter.WriteProgram(pou)
                : SclWriter.WritePou(project, pou);
            sb.AppendLine($@"      <pou name=""{Xml(pou.Name)}"" pouType=""{pouType}"">");
            sb.AppendLine(@"        <interface/>");
            sb.AppendLine(@"        <body>");
            sb.AppendLine(@"          <ST>");
            sb.AppendLine(@"            <xhtml xmlns=""http://www.w3.org/1999/xhtml"">");
            sb.AppendLine(Xml(body));
            sb.AppendLine(@"            </xhtml>");
            sb.AppendLine(@"          </ST>");
            sb.AppendLine(@"        </body>");
            sb.AppendLine(@"      </pou>");
        }

        sb.AppendLine(@"    </pous>");
        sb.AppendLine(@"  </types>");
        sb.AppendLine(@"  <instances>");
        sb.AppendLine(@"    <configurations>");
        sb.AppendLine(@"      <configuration name=""Config"">");
        sb.AppendLine(@"        <resource name=""Resource1"">");
        var interval = $"T#{Math.Max(1, project.CycleMs).ToString(CultureInfo.InvariantCulture)}MS";
        sb.AppendLine($@"          <task name=""MainTask"" interval=""{interval}"" priority=""1"">");
        sb.AppendLine($@"            <pouInstance name=""MainInstance"" typeName=""{Xml(IecNames.ProgramMain())}""/>");
        sb.AppendLine(@"          </task>");
        sb.AppendLine(@"          <globalVars name=""GVL_MdkVars"">");
        foreach (var io in project.IoPoints)
        {
            sb.AppendLine($@"            <variable name=""{Xml(io.Name)}"" address=""{Xml(io.AtAddress)}"">");
            sb.AppendLine(@"              <type><BOOL/></type>");
            sb.AppendLine(@"            </variable>");
        }

        foreach (var g in project.Globals)
        {
            sb.AppendLine($@"            <variable name=""{Xml(g.Name)}"">");
            sb.AppendLine($"              <type><{XmlType(g.Type)}/></type>");
            sb.AppendLine(@"            </variable>");
        }

        sb.AppendLine(@"          </globalVars>");
        sb.AppendLine(@"        </resource>");
        sb.AppendLine(@"      </configuration>");
        sb.AppendLine(@"    </configurations>");
        sb.AppendLine(@"  </instances>");
        sb.AppendLine(@"</project>");
        _ = stFiles;
        return sb.ToString();
    }

    private static string XmlType(IecType type) => type switch
    {
        IecType.Bool => "BOOL",
        IecType.Int => "DINT",
        IecType.String => "string",
        IecType.Time => "TIME",
        _ => "REAL",
    };

    private static string Xml(string? value) => WebUtility.HtmlEncode(value ?? "");
}
