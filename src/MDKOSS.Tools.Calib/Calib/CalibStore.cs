using MDKOSS.Core;
using MDKOSS.Core.Data;

namespace MDKOSS.Tools.Calib.Calib;

/// <summary>Persists calibration parameters and run results through <see cref="MdkDataStore"/>.</summary>
public static class CalibStore
{
    public static Dictionary<string, string> CollectVisibleParams(MdkSetting.TaskConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var kv in config.Parameters)
        {
            if (CalibCatalog.HiddenParamKeys.Contains(kv.Key))
            {
                continue;
            }

            dict[kv.Key] = kv.Value ?? "";
        }

        return dict;
    }

    public static Dictionary<string, string> CollectResults(IReadOnlyDictionary<string, object?> snapshot, string taskName)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var prefix = $"task.{taskName}.calib.";
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var kv in snapshot)
        {
            if (!kv.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            dict[kv.Key[prefix.Length..]] = kv.Value?.ToString() ?? "";
        }

        return dict;
    }

    public static bool TrySaveParams(
        MdkDataStore store,
        string projectName,
        string taskName,
        IReadOnlyDictionary<string, string> parameters,
        out string? error)
    {
        ArgumentNullException.ThrowIfNull(store);
        return store.TryUpsertCalibParams(
            new CalibParamsRecord
            {
                ProjectName = projectName ?? "",
                TaskName = taskName,
                Params = new Dictionary<string, string>(parameters, StringComparer.OrdinalIgnoreCase),
            },
            out error);
    }

    public static bool TryLoadParams(
        MdkDataStore store,
        string projectName,
        string taskName,
        out Dictionary<string, string> parameters)
    {
        ArgumentNullException.ThrowIfNull(store);
        parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!store.TryGetCalibParams(projectName, taskName, out var record) || record is null)
        {
            return false;
        }

        foreach (var kv in record.Params)
        {
            parameters[kv.Key] = kv.Value ?? "";
        }

        return true;
    }

    public static bool TrySaveResult(
        MdkDataStore store,
        string projectName,
        string taskName,
        IReadOnlyDictionary<string, string> parameters,
        IReadOnlyDictionary<string, string> results,
        bool ok,
        string message,
        out string? error)
    {
        ArgumentNullException.ThrowIfNull(store);
        return store.TryInsertCalibResult(
            new CalibResultRecord
            {
                ProjectName = projectName ?? "",
                TaskName = taskName,
                Params = new Dictionary<string, string>(parameters, StringComparer.OrdinalIgnoreCase),
                Results = new Dictionary<string, string>(results, StringComparer.OrdinalIgnoreCase),
                Ok = ok,
                Message = message ?? "",
            },
            out error);
    }

    public static bool TryLoadLatestResult(
        MdkDataStore store,
        string projectName,
        string taskName,
        out CalibResultRecord? record)
    {
        ArgumentNullException.ThrowIfNull(store);
        return store.TryGetLatestCalibResult(projectName, taskName, out record);
    }

    public static bool IsTruthyResult(IReadOnlyDictionary<string, string> results)
    {
        if (results.TryGetValue("ok", out var raw) && !string.IsNullOrWhiteSpace(raw))
        {
            return raw.Trim() is "1" or "true" or "True" or "yes";
        }

        return false;
    }
}
