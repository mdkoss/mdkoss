using MDKOSS.Core;
using MDKOSS.Iec61131;

namespace MDKOSS.Sample.Iec61131;

internal static class Program
{
    public static int Main(string[] args)
    {
        var root = FindProjectRoot() ?? AppContext.BaseDirectory;
        var configs = Path.Combine(root, "configs");
        Directory.CreateDirectory(configs);

        var settingPath = Path.Combine(configs, "station.setting.json");
        var flowPath = Path.Combine(configs, "station.flow.json");
        var exportDir = Path.Combine(root, "export");

        for (var i = 0; i < args.Length; i++)
        {
            if (args[i] == "-o" && i + 1 < args.Length)
            {
                exportDir = args[++i];
            }
            else if (!args[i].StartsWith('-') && args[i].EndsWith(".json", StringComparison.OrdinalIgnoreCase)
                     && File.Exists(args[i]))
            {
                settingPath = args[i];
            }
        }

        var setting = StationFlowFactory.CreateSetting();
        var flow = StationFlowFactory.Create();
        File.WriteAllText(flowPath, flow.ToJson());
        setting.Save(settingPath);

        var result = IecExport.FromSettingFile(settingPath, exportDir);
        Console.WriteLine($"Exported IEC 61131 project '{result.Project.Name}'");
        Console.WriteLine($"  setting: {settingPath}");
        Console.WriteLine($"  flow:    {flowPath}");
        Console.WriteLine($"  output:  {result.Directory}");
        Console.WriteLine($"  POUs:    {result.Project.Pous.Count}");
        Console.WriteLine($"  files:   {result.Files.Count}");
        foreach (var note in result.Project.Notes)
        {
            Console.WriteLine($"  [{note.Severity}] {note.Message}");
        }

        return result.Project.Notes.Any(n => n.Severity == "error") ? 1 : 0;
    }

    private static string? FindProjectRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "MDKOSS.Sample.Iec61131.csproj")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        return null;
    }
}
