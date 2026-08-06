using System.Globalization;
using System.IO;
using System.Text;
using System.Xml.Linq;

namespace MDKOSS.Config.Wpf;

/// <summary>
/// Minimal Excel I/O via SpreadsheetML (Excel 2003 XML). Files open in Excel as <c>.xls</c>.
/// </summary>
public static class ExcelSheetIo
{
    private static readonly XNamespace Ss = "urn:schemas-microsoft-com:office:spreadsheet";
    private static readonly XNamespace O = "urn:schemas-microsoft-com:office:office";
    private static readonly XNamespace X = "urn:schemas-microsoft-com:office:excel";

    public static void WriteSheet(string path, string sheetName, IReadOnlyList<string> headers, IEnumerable<IReadOnlyList<string>> rows)
    {
        var table = new XElement(Ss + "Table");
        var headerRow = new XElement(Ss + "Row");
        foreach (var h in headers)
        {
            headerRow.Add(Cell(h, "String"));
        }

        table.Add(headerRow);
        foreach (var row in rows)
        {
            var xr = new XElement(Ss + "Row");
            for (var i = 0; i < headers.Count; i++)
            {
                var value = i < row.Count ? row[i] ?? "" : "";
                xr.Add(Cell(value, "String"));
            }

            table.Add(xr);
        }

        var workbook = new XDocument(
            new XDeclaration("1.0", "utf-8", null),
            new XElement(
                Ss + "Workbook",
                new XAttribute(XNamespace.Xmlns + "ss", Ss),
                new XAttribute(XNamespace.Xmlns + "o", O),
                new XAttribute(XNamespace.Xmlns + "x", X),
                new XElement(
                    Ss + "Worksheet",
                    new XAttribute(Ss + "Name", string.IsNullOrWhiteSpace(sheetName) ? "Sheet1" : sheetName),
                    table)));

        var full = Path.GetFullPath(path);
        var dir = Path.GetDirectoryName(full);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        // Excel expects UTF-8 with BOM for SpreadsheetML .xls
        var settings = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);
        File.WriteAllText(full, workbook.ToString(), settings);
    }

    public static (IReadOnlyList<string> Headers, List<Dictionary<string, string>> Rows) ReadSheet(string path)
    {
        var full = Path.GetFullPath(path);
        var ext = Path.GetExtension(full);
        if (ext.Equals(".csv", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".tsv", StringComparison.OrdinalIgnoreCase))
        {
            return ReadDelimited(full, ext.Equals(".tsv", StringComparison.OrdinalIgnoreCase) ? '\t' : ',');
        }

        var doc = XDocument.Load(full);
        XNamespace ss = Ss;
        var table = doc.Descendants(ss + "Table").FirstOrDefault()
                    ?? throw new InvalidOperationException("Excel 文件中未找到 Table。");
        var xmlRows = table.Elements(ss + "Row").ToList();
        if (xmlRows.Count == 0)
        {
            return ([], []);
        }

        var headers = ReadCells(xmlRows[0], ss)
            .Select(h => h.Trim())
            .Where(h => h.Length > 0)
            .ToList();
        if (headers.Count == 0)
        {
            throw new InvalidOperationException("Excel 首行没有列名。");
        }

        var result = new List<Dictionary<string, string>>();
        for (var r = 1; r < xmlRows.Count; r++)
        {
            var cells = ReadCells(xmlRows[r], ss);
            if (cells.All(string.IsNullOrWhiteSpace))
            {
                continue;
            }

            var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (var c = 0; c < headers.Count; c++)
            {
                dict[headers[c]] = c < cells.Count ? cells[c] : "";
            }

            result.Add(dict);
        }

        return (headers, result);
    }

    private static (IReadOnlyList<string> Headers, List<Dictionary<string, string>> Rows) ReadDelimited(string path, char sep)
    {
        var lines = File.ReadAllLines(path);
        if (lines.Length == 0)
        {
            return ([], []);
        }

        var headers = SplitDelimited(lines[0], sep).Select(h => h.Trim()).Where(h => h.Length > 0).ToList();
        var rows = new List<Dictionary<string, string>>();
        for (var i = 1; i < lines.Length; i++)
        {
            if (string.IsNullOrWhiteSpace(lines[i]))
            {
                continue;
            }

            var cells = SplitDelimited(lines[i], sep);
            var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (var c = 0; c < headers.Count; c++)
            {
                dict[headers[c]] = c < cells.Count ? cells[c] : "";
            }

            rows.Add(dict);
        }

        return (headers, rows);
    }

    private static List<string> SplitDelimited(string line, char sep)
    {
        var list = new List<string>();
        var sb = new StringBuilder();
        var inQuotes = false;
        for (var i = 0; i < line.Length; i++)
        {
            var ch = line[i];
            if (ch == '"')
            {
                if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                {
                    sb.Append('"');
                    i++;
                }
                else
                {
                    inQuotes = !inQuotes;
                }

                continue;
            }

            if (ch == sep && !inQuotes)
            {
                list.Add(sb.ToString());
                sb.Clear();
                continue;
            }

            sb.Append(ch);
        }

        list.Add(sb.ToString());
        return list;
    }

    private static List<string> ReadCells(XElement row, XNamespace ss)
    {
        var cells = new List<string>();
        var index = 1;
        foreach (var cell in row.Elements(ss + "Cell"))
        {
            var indexAttr = cell.Attribute(ss + "Index");
            if (indexAttr is not null && int.TryParse(indexAttr.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var abs))
            {
                while (index < abs)
                {
                    cells.Add("");
                    index++;
                }
            }

            var data = cell.Element(ss + "Data");
            cells.Add(data?.Value ?? "");
            index++;
        }

        return cells;
    }

    private static XElement Cell(string value, string type) =>
        new(
            Ss + "Cell",
            new XElement(
                Ss + "Data",
                new XAttribute(Ss + "Type", type),
                value ?? ""));
}
