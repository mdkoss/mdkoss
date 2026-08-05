using System.Globalization;

namespace MDKOSS.Extensions.PyScript;

/// <summary>Parsed parameters for <see cref="PyScriptDevice"/> (config type <c>devpyscript</c>).</summary>
public sealed class PyScriptDeviceParameters
{
    public string PythonPath { get; init; } = "python";

    public string ScriptPath { get; init; } = string.Empty;

    public string WorkingDirectory { get; init; } = string.Empty;

    public string Arguments { get; init; } = string.Empty;

    /// <summary>Process timeout in milliseconds. 0 or negative means no timeout.</summary>
    public int TimeoutMs { get; init; } = 30_000;

    public bool CaptureOutput { get; init; } = true;

    public static PyScriptDeviceParameters ParseConfig(IReadOnlyDictionary<string, string>? parameters)
    {
        parameters ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        return new PyScriptDeviceParameters
        {
            PythonPath = ReadString(parameters, "pythonPath", "python"),
            ScriptPath = ReadString(parameters, "scriptPath", string.Empty),
            WorkingDirectory = ReadString(parameters, "workingDirectory", string.Empty),
            Arguments = ReadString(parameters, "arguments", string.Empty),
            TimeoutMs = Math.Max(0, ReadInt(parameters, "timeoutMs", 30_000)),
            CaptureOutput = ReadBool(parameters, "captureOutput", true),
        };
    }

    private static string ReadString(IReadOnlyDictionary<string, string> parameters, string key, string fallback)
    {
        return parameters.TryGetValue(key, out var raw) && !string.IsNullOrWhiteSpace(raw)
            ? raw.Trim()
            : fallback;
    }

    private static int ReadInt(IReadOnlyDictionary<string, string> parameters, string key, int fallback)
    {
        if (!parameters.TryGetValue(key, out var raw) || string.IsNullOrWhiteSpace(raw))
        {
            return fallback;
        }

        return int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : fallback;
    }

    private static bool ReadBool(IReadOnlyDictionary<string, string> parameters, string key, bool fallback)
    {
        if (!parameters.TryGetValue(key, out var raw) || string.IsNullOrWhiteSpace(raw))
        {
            return fallback;
        }

        return bool.TryParse(raw, out var value) ? value : fallback;
    }
}
