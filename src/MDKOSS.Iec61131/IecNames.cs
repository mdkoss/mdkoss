using System.Globalization;
using System.Text;

namespace MDKOSS.Iec61131;

/// <summary>IEC 61131-3 identifier sanitizer (letters, digits, underscore).</summary>
public static class IecNames
{
    private static readonly HashSet<string> Reserved = new(StringComparer.OrdinalIgnoreCase)
    {
        "AND", "OR", "XOR", "NOT", "MOD", "IF", "THEN", "ELSE", "ELSIF", "END_IF",
        "CASE", "OF", "END_CASE", "FOR", "TO", "BY", "DO", "END_FOR", "WHILE", "END_WHILE",
        "REPEAT", "UNTIL", "END_REPEAT", "EXIT", "RETURN", "TRUE", "FALSE",
        "FUNCTION", "FUNCTION_BLOCK", "END_FUNCTION", "END_FUNCTION_BLOCK",
        "PROGRAM", "END_PROGRAM", "VAR", "VAR_INPUT", "VAR_OUTPUT", "VAR_IN_OUT",
        "VAR_TEMP", "VAR_GLOBAL", "VAR_EXTERNAL", "END_VAR", "TYPE", "END_TYPE",
        "STRUCT", "END_STRUCT", "ARRAY", "STRING", "WSTRING", "BOOL", "BYTE", "WORD",
        "DWORD", "LWORD", "SINT", "INT", "DINT", "LINT", "USINT", "UINT", "UDINT",
        "ULINT", "REAL", "LREAL", "TIME", "DATE", "TOD", "DT", "TON", "TOF", "TP",
        "R_TRIG", "F_TRIG", "CTU", "CTD", "CTUD", "RS", "SR", "AT", "RETAIN", "CONSTANT",
        "CONFIGURATION", "RESOURCE", "TASK", "WITH", "INTERVAL", "PRIORITY",
    };

    public static string Sanitize(string? raw, string fallback = "v")
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return fallback;
        }

        var sb = new StringBuilder(raw.Length + 2);
        foreach (var c in raw.Trim())
        {
            if (char.IsLetterOrDigit(c) || c == '_')
            {
                sb.Append(c);
            }
            else
            {
                sb.Append('_');
            }
        }

        var name = sb.ToString().Trim('_');
        if (name.Length == 0)
        {
            name = fallback;
        }

        if (char.IsDigit(name[0]))
        {
            name = "v_" + name;
        }

        if (name.Length > 120)
        {
            name = name[..120];
        }

        if (Reserved.Contains(name))
        {
            name = "v_" + name;
        }

        return name;
    }

    public static string Unique(string candidate, ISet<string> used)
    {
        var name = Sanitize(candidate);
        var baseName = name;
        var i = 2;
        while (!used.Add(name))
        {
            name = $"{baseName}_{i.ToString(CultureInfo.InvariantCulture)}";
            i++;
        }

        return name;
    }

    public static string PouFb(string taskOrFunctionName)
    {
        var raw = Sanitize(taskOrFunctionName, "Pou");
        var parts = raw.Split('_', StringSplitOptions.RemoveEmptyEntries);
        var sb = new StringBuilder("FB_");
        foreach (var part in parts)
        {
            sb.Append(char.ToUpperInvariant(part[0]));
            if (part.Length > 1)
            {
                sb.Append(part[1..]);
            }
        }

        return sb.ToString();
    }

    public static string ProgramMain() => "PROGRAM_Main";

    public static string Gvl() => "GVL_MdkVars";

    public static string IoInput(string alias) => "I_" + Sanitize(alias, "in");

    public static string IoOutput(string alias) => "Q_" + Sanitize(alias, "out");

    public static string Timer(string nodeId) => "ton_" + Sanitize(nodeId, "delay");

    public static string FbInstance(string nodeId) => "fb_" + Sanitize(nodeId, "host");

    public static string Param(string key) => "param_" + Sanitize(key, "p");

    public static string TaskVar(string suffix) => "taskvar_" + Sanitize(suffix, "v");

    public static string StepVar() => "iStep";
}
