using MDKOSS.Core;
using System.Text.Json;

namespace MDKOSS.Gui;

internal static class ConfigFormHelpers
{
    public static MdkSetting LoadSetting(string settingPath)
    {
        return MdkSetting.Load(settingPath);
    }

    public static void SaveSetting(string settingPath, MdkSetting setting)
    {
        var json = JsonSerializer.Serialize(setting, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(settingPath, json);
    }

    public static string ParametersToText(IReadOnlyDictionary<string, string> parameters)
    {
        return string.Join("; ", parameters
            .OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
            .Select(kv => $"{kv.Key}={kv.Value}"));
    }

    public static Dictionary<string, string> ParseParameters(string? text)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(text))
        {
            return result;
        }

        var segments = text.Split([';', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var segment in segments)
        {
            var equalIndex = segment.IndexOf('=');
            if (equalIndex <= 0)
            {
                continue;
            }

            var key = segment[..equalIndex].Trim();
            var value = segment[(equalIndex + 1)..].Trim();
            if (!string.IsNullOrWhiteSpace(key))
            {
                result[key] = value;
            }
        }

        return result;
    }

    public static List<T> ImportRows<T>(IWin32Window owner)
    {
        using var dialog = new OpenFileDialog
        {
            Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*"
        };

        if (dialog.ShowDialog(owner) != DialogResult.OK)
        {
            return [];
        }

        var json = File.ReadAllText(dialog.FileName);
        return JsonSerializer.Deserialize<List<T>>(json) ?? [];
    }

    public static void ExportRows<T>(IWin32Window owner, IReadOnlyCollection<T> rows)
    {
        using var dialog = new SaveFileDialog
        {
            Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*",
            DefaultExt = "json",
            AddExtension = true
        };

        if (dialog.ShowDialog(owner) != DialogResult.OK)
        {
            return;
        }

        var json = JsonSerializer.Serialize(rows, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(dialog.FileName, json);
    }
}
