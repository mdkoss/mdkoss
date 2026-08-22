using System.Globalization;
using System.Text;

namespace MDKOSS.Iec61131;

/// <summary>Writes Siemens-friendly IEC 61131-3 Structured Text (SCL).</summary>
public static class SclWriter
{
    public static Dictionary<string, string> WriteFiles(IecProject project)
    {
        var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["scl/00_GVL_MdkVars.scl"] = WriteGvl(project),
            ["scl/01_FB_HostStubs.scl"] = HostFunctionBlocks.WriteAll(),
        };

        foreach (var pou in project.Pous.Where(p => p.Kind == IecPouKind.FunctionBlock))
        {
            files[$"scl/{SanitizeFile(pou.Name)}.scl"] = WritePou(project, pou);
        }

        var program = project.Pous.FirstOrDefault(p => p.Kind == IecPouKind.Program);
        if (program is not null)
        {
            files[$"scl/{SanitizeFile(program.Name)}.scl"] = WriteProgram(program);
        }

        return files;
    }

    public static string WriteGvl(IecProject project)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"(* Global vars + IO image for '{project.Name}' *)");
        sb.AppendLine("VAR_GLOBAL");
        foreach (var io in project.IoPoints)
        {
            var dir = io.IsOutput ? "Q" : "I";
            sb.AppendLine($"    {io.Name} AT {io.AtAddress} : BOOL; (* {dir} {io.DeviceId}.{io.Alias} {io.Label} *)");
        }

        foreach (var g in project.Globals)
        {
            sb.AppendLine($"    {g.Name} : {IecTypeMap.ToSt(g.Type)} := {g.Init ?? IecTypeMap.DefaultInit(g.Type)}; (* {g.SourceKey} *)");
        }

        sb.AppendLine("END_VAR");
        return sb.ToString();
    }

    public static string WritePou(IecProject project, IecPou pou)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"(* Flow task '{pou.SourceName}' — step sequencer, cycle ~{project.CycleMs}ms *)");
        sb.AppendLine($"FUNCTION_BLOCK {pou.Name}");
        sb.AppendLine("VAR_INPUT");
        if (pou.Cyclic)
        {
            sb.AppendLine("    Run : BOOL := TRUE;");
            sb.AppendLine("    Reset : BOOL;");
        }
        else
        {
            sb.AppendLine("    Execute : BOOL;");
        }

        sb.AppendLine("END_VAR");
        sb.AppendLine("VAR_OUTPUT");
        sb.AppendLine("    Done : BOOL;");
        sb.AppendLine("    Busy : BOOL;");
        sb.AppendLine("    LastLog : STRING[80];");
        sb.AppendLine("    LastError : STRING[80];");
        sb.AppendLine($"    {IecNames.StepVar()}Out : DINT;");
        sb.AppendLine("END_VAR");
        sb.AppendLine("VAR");
        sb.AppendLine($"    {IecNames.StepVar()} : DINT := {pou.StartStep.ToString(CultureInfo.InvariantCulture)};");
        foreach (var v in pou.Locals)
        {
            sb.AppendLine($"    {v.Name} : {IecTypeMap.ToSt(v.Type)} := {v.Init ?? IecTypeMap.DefaultInit(v.Type)}; (* {v.Comment} *)");
        }

        foreach (var inst in pou.Instances)
        {
            sb.AppendLine($"    {inst.Name} : {inst.TypeName}; (* {inst.Comment} *)");
        }

        sb.AppendLine("END_VAR");
        sb.AppendLine();
        if (pou.Cyclic)
        {
            sb.AppendLine("IF Reset THEN");
            sb.AppendLine($"    {IecNames.StepVar()} := {pou.StartStep.ToString(CultureInfo.InvariantCulture)};");
            sb.AppendLine("    Done := FALSE;");
            sb.AppendLine("    Busy := FALSE;");
            sb.AppendLine("    LastError := '';");
            sb.AppendLine("END_IF;");
            sb.AppendLine();
            sb.AppendLine("IF NOT Run THEN");
            sb.AppendLine("    Busy := FALSE;");
            sb.AppendLine("    RETURN;");
            sb.AppendLine("END_IF;");
        }
        else
        {
            sb.AppendLine("IF NOT Execute THEN");
            sb.AppendLine("    Done := FALSE;");
            sb.AppendLine("    Busy := FALSE;");
            sb.AppendLine($"    {IecNames.StepVar()} := {pou.StartStep.ToString(CultureInfo.InvariantCulture)};");
            sb.AppendLine("    RETURN;");
            sb.AppendLine("END_IF;");
        }

        sb.AppendLine();
        sb.AppendLine("Busy := NOT Done;");
        sb.AppendLine($"CASE {IecNames.StepVar()} OF");
        foreach (var step in pou.Steps)
        {
            sb.AppendLine($"    {step.Number.ToString(CultureInfo.InvariantCulture)}: (* {step.Comment} *)");
            WriteStepBody(sb, pou, step, indent: "        ");
        }

        sb.AppendLine("    ELSE");
        sb.AppendLine("        LastError := 'bad_step';");
        sb.AppendLine("        Done := TRUE;");
        sb.AppendLine("END_CASE;");
        sb.AppendLine($"iStepOut := {IecNames.StepVar()};");
        sb.AppendLine("END_FUNCTION_BLOCK");
        return sb.ToString();
    }

    public static string WriteProgram(IecPou pou)
    {
        var sb = new StringBuilder();
        sb.AppendLine("(* Cyclic entry — assign to a 20ms task / OB1 in TIA *)");
        sb.AppendLine($"PROGRAM {pou.Name}");
        sb.AppendLine("VAR");
        foreach (var inst in pou.Instances)
        {
            sb.AppendLine($"    {inst.Name} : {inst.TypeName}; (* {inst.Comment} *)");
        }

        sb.AppendLine("END_VAR");
        sb.AppendLine();
        foreach (var inst in pou.Instances)
        {
            sb.AppendLine($"{inst.Name}(Run := TRUE, Reset := FALSE);");
        }

        if (pou.Instances.Count == 0)
        {
            sb.AppendLine("(* no flow tasks to run *)");
        }

        sb.AppendLine("END_PROGRAM");
        return sb.ToString();
    }

    private static void WriteStepBody(StringBuilder sb, IecPou pou, IecStep step, string indent)
    {
        switch (step.Kind)
        {
            case IecStepKind.Goto:
                WriteGotoNext(sb, step, indent);
                break;
            case IecStepKind.Assign:
                sb.AppendLine($"{indent}{step.Target} := {step.Expression};");
                WriteGotoNext(sb, step, indent);
                break;
            case IecStepKind.IfGoto:
                sb.AppendLine($"{indent}IF {step.Expression} THEN");
                sb.AppendLine($"{indent}    {IecNames.StepVar()} := {step.Next.ToString(CultureInfo.InvariantCulture)};");
                sb.AppendLine($"{indent}ELSE");
                sb.AppendLine($"{indent}    {IecNames.StepVar()} := {step.AltNext.ToString(CultureInfo.InvariantCulture)};");
                sb.AppendLine($"{indent}END_IF;");
                break;
            case IecStepKind.Delay:
                sb.AppendLine($"{indent}{step.TimerName}(IN := TRUE, PT := {IecExpr.TimeFromMs(step.DelayMs)});");
                sb.AppendLine($"{indent}IF {step.TimerName}.Q THEN");
                sb.AppendLine($"{indent}    {step.TimerName}(IN := FALSE, PT := {IecExpr.TimeFromMs(step.DelayMs)});");
                sb.AppendLine($"{indent}    {IecNames.StepVar()} := {step.Next.ToString(CultureInfo.InvariantCulture)};");
                sb.AppendLine($"{indent}END_IF;");
                break;
            case IecStepKind.WriteIo:
                sb.AppendLine($"{indent}{step.IoName} := {step.Expression};");
                WriteGotoNext(sb, step, indent);
                break;
            case IecStepKind.ReadIo:
                sb.AppendLine($"{indent}{step.Target} := {step.IoName};");
                WriteGotoNext(sb, step, indent);
                break;
            case IecStepKind.Log:
                sb.AppendLine($"{indent}LastLog := {CoerceLog(step.Expression)};");
                WriteGotoNext(sb, step, indent);
                break;
            case IecStepKind.HostCall:
                WriteHostCall(sb, pou, step, indent);
                break;
            case IecStepKind.Complete:
                sb.AppendLine($"{indent}Done := TRUE;");
                sb.AppendLine($"{indent}Busy := FALSE;");
                break;
            default:
                sb.AppendLine($"{indent}LastError := '{EscapeSt(step.Comment)}';");
                sb.AppendLine($"{indent}Done := TRUE;");
                break;
        }
    }

    private static void WriteHostCall(StringBuilder sb, IecPou pou, IecStep step, string indent)
    {
        var inst = step.HostInstance ?? "fb";
        var args = new List<string> { "Execute := TRUE" };
        if (string.Equals(step.HostType, pou.Name, StringComparison.OrdinalIgnoreCase))
        {
            args = ["Run := TRUE"];
        }

        foreach (var a in step.HostArgs)
        {
            args.Add($"{a.Parameter} := {a.Value}");
        }

        sb.AppendLine($"{indent}{inst}({string.Join(", ", args)});");
        sb.AppendLine($"{indent}IF {inst}.Done THEN");
        foreach (var o in step.HostOutputs)
        {
            sb.AppendLine($"{indent}    {o.Value} := {inst}.{o.Parameter};");
        }

        var reset = string.Equals(step.HostType, pou.Name, StringComparison.OrdinalIgnoreCase)
            ? "Run := FALSE"
            : "Execute := FALSE";
        var resetArgs = new List<string> { reset };
        foreach (var a in step.HostArgs)
        {
            resetArgs.Add($"{a.Parameter} := {a.Value}");
        }

        sb.AppendLine($"{indent}    {inst}({string.Join(", ", resetArgs)});");
        WriteGotoNext(sb, step, indent + "    ");
        sb.AppendLine($"{indent}END_IF;");
    }

    private static void WriteGotoNext(StringBuilder sb, IecStep step, string indent)
    {
        if (step.Next <= 0)
        {
            sb.AppendLine($"{indent}Done := TRUE;");
            sb.AppendLine($"{indent}Busy := FALSE;");
            return;
        }

        sb.AppendLine($"{indent}{IecNames.StepVar()} := {step.Next.ToString(CultureInfo.InvariantCulture)};");
    }

    private static string CoerceLog(string? expr)
    {
        if (string.IsNullOrWhiteSpace(expr))
        {
            return "''";
        }

        var t = expr.Trim();
        if (t.StartsWith('\''))
        {
            return t;
        }

        return $"ANY_TO_STRING({t})";
    }

    private static string EscapeSt(string? text) =>
        (text ?? "").Replace("'", "''", StringComparison.Ordinal);

    private static string SanitizeFile(string name)
    {
        var s = IecNames.Sanitize(name, "pou");
        return s;
    }
}
