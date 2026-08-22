using System.Text.Json;
using MDKOSS.Core;
using MDKOSS.Core.Flow;

namespace MDKOSS.Iec61131;

/// <summary>Builds an <see cref="IecProject"/> from an MDKOSS setting (Flow tasks + vars + GPIO).</summary>
public static class IecProjectBuilder
{
    public static IecProject FromSetting(MdkSetting setting, string? settingDirectory = null)
    {
        ArgumentNullException.ThrowIfNull(setting);
        var project = new IecProject
        {
            Name = string.IsNullOrWhiteSpace(setting.ProjectName) ? "MDKOSS" : setting.ProjectName.Trim(),
            CycleMs = setting.CycleMs <= 0 ? 20 : setting.CycleMs,
        };

        var reserved = new[]
        {
            IecNames.StepVar(), "Run", "Reset", "Done", "Busy", "Execute", "LastLog", "LastError",
        };
        var symbols = new IecSymbols(reserved);

        project.IoPoints = IecIoMapper.FromSetting(setting, project.Notes);
        foreach (var io in project.IoPoints)
        {
            symbols.Alias(io.Name, io.Name);
            symbols.Alias(io.Alias, io.Name);
            symbols.Alias($"{io.DeviceId}:{io.Alias}", io.Name);
        }

        foreach (var kv in setting.Vars)
        {
            if (string.IsNullOrWhiteSpace(kv.Key))
            {
                continue;
            }

            var type = IecTypeMap.FromObject(Unwrap(kv.Value));
            var name = symbols.Register(kv.Key);
            project.Globals.Add(new IecVariable
            {
                Name = name,
                Type = type,
                Init = IecExpr.Literal(Unwrap(kv.Value)),
                SourceKey = kv.Key,
                Comment = "setting.vars",
            });
        }

        var flowTasks = setting.Tasks
            .Where(t => string.Equals(t.Type, "flow", StringComparison.OrdinalIgnoreCase))
            .ToList();

        foreach (var task in setting.Tasks)
        {
            if (string.Equals(task.Type, "flow", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            project.Notes.Add(new IecNote
            {
                Severity = "info",
                Message = $"Task '{task.Name}' type={task.Type} is C# / PC-only and was not exported",
            });
        }

        if (flowTasks.Count == 0)
        {
            project.Notes.Add(new IecNote
            {
                Severity = "warn",
                Message = "No type=flow tasks found; PROGRAM_Main is empty",
            });
        }

        foreach (var task in flowTasks)
        {
            if (!TryLoadDocument(task, settingDirectory, out var doc, out var error))
            {
                project.Notes.Add(new IecNote { Severity = "error", Message = error ?? "flow load failed" });
                continue;
            }

            // Do not SyncEdges on the full document: extra functions may be
            // edge-only chains hanging off the same JSON.

            var errors = doc.Validate();
            if (errors.Count > 0)
            {
                project.Notes.Add(new IecNote
                {
                    Severity = "error",
                    Message = $"flow '{task.Name}' validation: " + string.Join("; ", errors),
                });
                continue;
            }

            var loop = true;
            if (task.Parameters.TryGetValue("loop", out var loopRaw)
                && bool.TryParse(loopRaw, out var parsed))
            {
                loop = parsed;
            }

            var taskFb = IecNames.PouFb(task.Name);
            var functionNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var fn in doc.Functions)
            {
                var fnName = string.IsNullOrWhiteSpace(fn.Name) ? "main" : fn.Name.Trim();
                functionNames[fnName] = string.Equals(fnName, "main", StringComparison.OrdinalIgnoreCase)
                    ? taskFb
                    : IecNames.PouFb(task.Name + "_" + fnName);
            }

            var otherFnIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var fn in doc.Functions)
            {
                var fnName = string.IsNullOrWhiteSpace(fn.Name) ? "main" : fn.Name.Trim();
                if (!string.Equals(fnName, "main", StringComparison.OrdinalIgnoreCase))
                {
                    otherFnIds.UnionWith(CollectReachable(doc, fn.EntryNodeId));
                }
            }

            foreach (var fn in doc.Functions)
            {
                var fnName = string.IsNullOrWhiteSpace(fn.Name) ? "main" : fn.Name.Trim();
                var isMain = string.Equals(fnName, "main", StringComparison.OrdinalIgnoreCase);
                var slice = isMain ? SliceMain(doc, otherFnIds) : SliceFunction(doc, fn);
                var pouSymbols = CloneWithGlobals(symbols);
                var pou = FlowStepCompiler.Compile(
                    slice,
                    functionNames[fnName],
                    isMain ? task.Name : task.Name + "." + fnName,
                    cyclic: isMain,
                    loop: isMain && loop,
                    pouSymbols,
                    project.IoPoints,
                    functionNames,
                    project.Notes,
                    project.NodeMaps);
                project.Pous.Add(pou);
            }
        }

        project.Pous.Add(BuildProgramMain(project));
        return project;
    }

    private static IecPou BuildProgramMain(IecProject project)
    {
        var cyclic = project.Pous.Where(p => p.Cyclic).ToList();
        var pou = new IecPou
        {
            Name = IecNames.ProgramMain(),
            Kind = IecPouKind.Program,
            SourceName = "Main",
            Cyclic = true,
        };

        var n = 10;
        foreach (var fb in cyclic)
        {
            var inst = fb.Name.StartsWith("FB_", StringComparison.Ordinal)
                ? "fb" + fb.Name[3..]
                : "fb_" + IecNames.Sanitize(fb.Name);
            pou.Instances.Add(new IecInstance { Name = inst, TypeName = fb.Name, Comment = fb.SourceName });
            pou.Steps.Add(new IecStep
            {
                Number = n,
                Kind = IecStepKind.HostCall,
                HostType = fb.Name,
                HostInstance = inst,
                Comment = $"run {fb.SourceName}",
                FlowKind = "program.call",
            });
            n += 10;
        }

        if (pou.Steps.Count == 0)
        {
            pou.Steps.Add(new IecStep
            {
                Number = 10,
                Kind = IecStepKind.Halt,
                Comment = "no flow tasks",
            });
        }

        return pou;
    }

    internal static bool TryLoadDocument(
        MdkSetting.TaskConfig task,
        string? settingDirectory,
        out FlowDocument document,
        out string? error)
    {
        document = FlowDocument.CreateEmpty();
        error = null;
        if (task.Parameters.TryGetValue("flowJson", out var json) && !string.IsNullOrWhiteSpace(json))
        {
            return FlowDocument.TryParse(json, out document, out error);
        }

        if (task.Parameters.TryGetValue("flowFile", out var file) && !string.IsNullOrWhiteSpace(file))
        {
            var path = Path.IsPathRooted(file)
                ? file
                : Path.Combine(settingDirectory ?? Environment.CurrentDirectory, file);
            if (!File.Exists(path))
            {
                error = $"flowFile not found: {path}";
                return false;
            }

            return FlowDocument.TryParse(File.ReadAllText(path), out document, out error);
        }

        error = $"Task '{task.Name}' has no flowJson / flowFile";
        return false;
    }

    private static FlowDocument SliceMain(FlowDocument doc, HashSet<string> otherFnIds)
    {
        var nodes = doc.Nodes.Where(n => !otherFnIds.Contains(n.Id)).ToList();
        var ids = new HashSet<string>(nodes.Select(n => n.Id), StringComparer.OrdinalIgnoreCase);
        return new FlowDocument
        {
            Version = doc.Version,
            Variables = doc.Variables,
            Functions = doc.Functions.Where(f =>
                string.Equals(f.Name, "main", StringComparison.OrdinalIgnoreCase)).ToList(),
            Nodes = nodes,
            Edges = doc.Edges.Where(e => ids.Contains(e.From) && ids.Contains(e.To)).ToList(),
        };
    }

    private static FlowDocument SliceFunction(FlowDocument doc, FlowFunction fn)
    {
        var ids = CollectReachable(doc, fn.EntryNodeId);
        var nodes = doc.Nodes.Where(n => ids.Contains(n.Id)).ToList();
        return new FlowDocument
        {
            Version = doc.Version,
            Variables = doc.Variables,
            Functions = [new FlowFunction { Name = fn.Name, EntryNodeId = fn.EntryNodeId }],
            Nodes = nodes,
            Edges = doc.Edges.Where(e => ids.Contains(e.From) && ids.Contains(e.To)).ToList(),
        };
    }

    private static HashSet<string> CollectReachable(FlowDocument doc, string entryId)
    {
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var q = new Queue<string>();
        q.Enqueue(entryId);
        while (q.Count > 0)
        {
            var id = q.Dequeue();
            if (!ids.Add(id))
            {
                continue;
            }

            foreach (var e in doc.Edges.Where(x => string.Equals(x.From, id, StringComparison.OrdinalIgnoreCase)))
            {
                q.Enqueue(e.To);
            }
        }

        return ids;
    }

    private static IecSymbols CloneWithGlobals(IecSymbols source)
    {
        var clone = new IecSymbols([
            IecNames.StepVar(), "Run", "Reset", "Done", "Busy", "Execute", "LastLog", "LastError",
        ]);
        foreach (var kv in source.Map)
        {
            clone.Alias(kv.Key, kv.Value);
        }

        return clone;
    }

    private static object? Unwrap(object? value)
    {
        if (value is JsonElement json)
        {
            return json.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.String => json.GetString(),
                JsonValueKind.Number when json.TryGetInt64(out var l) => l,
                JsonValueKind.Number => json.GetDouble(),
                _ => json.ToString(),
            };
        }

        return value;
    }
}
