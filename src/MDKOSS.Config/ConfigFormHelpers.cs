using MDKOSS.Core;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace MDKOSS.Gui;

internal static class ConfigFormHelpers
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static MdkSetting LoadSetting(string settingPath)
    {
        return MdkSetting.Load(settingPath);
    }

    public static void SaveSetting(string settingPath, MdkSetting setting)
    {
        setting.Save(settingPath);
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
        return ImportObject<List<T>>(owner) ?? [];
    }

    public static T? ImportObject<T>(IWin32Window owner)
    {
        using var dialog = new OpenFileDialog
        {
            Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*"
        };

        if (dialog.ShowDialog(owner) != DialogResult.OK)
        {
            return default;
        }

        var json = File.ReadAllText(dialog.FileName);
        return JsonSerializer.Deserialize<T>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
    }

    public static void ExportRows<T>(IWin32Window owner, IReadOnlyCollection<T> rows)
    {
        ExportObject(owner, rows);
    }

    public static void ExportObject<T>(IWin32Window owner, T value)
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

        var json = JsonSerializer.Serialize(value, JsonOptions);
        File.WriteAllText(dialog.FileName, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }
}
