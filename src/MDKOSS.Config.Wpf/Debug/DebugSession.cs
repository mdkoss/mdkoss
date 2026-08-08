using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;
using System.Windows.Controls;
using MDKOSS.Core;
using MDKOSS.Core.Drivers;

namespace MDKOSS.Config.Wpf.Debug;

/// <summary>Shared helpers for component debug windows (driver connect, axis index, logging).</summary>
public static class DebugUi
{
    public static readonly string[] ConfigPathKeys =
    [
        "configPath", "configFile", "cfgPath", "cfgFile", "cfg", "iniPath", "xmlPath", "dllPath",
    ];

    public static short ParseAxisIndex(IReadOnlyDictionary<string, string>? parameters, short fallback = 0) =>
        AxisDeviceParameterSet.ParseAxisIndex(parameters, fallback);

    public static string? FindConfigPath(IReadOnlyDictionary<string, string>? parameters)
    {
        if (parameters is null)
        {
            return null;
        }

        foreach (var key in ConfigPathKeys)
        {
            if (parameters.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v))
            {
                return v.Trim();
            }
        }

        return null;
    }

    public static void Log(TextBox? box, string message)
    {
        if (box is null)
        {
            return;
        }

        var line = $"[{DateTime.Now:HH:mm:ss.fff}] {message}";
        if (string.IsNullOrEmpty(box.Text))
        {
            box.Text = line;
        }
        else
        {
            box.AppendText(Environment.NewLine + line);
        }

        box.ScrollToEnd();
    }

    public static string FormatBool(bool ok) => ok ? "OK" : "FAIL";
}

/// <summary>Owns a connected <see cref="IDriver"/> for debug UI lifetime.</summary>
public sealed class ConnectedDriver : IDisposable
{
    public ConnectedDriver(MdkSetting.DriverConfig config, IDriver driver)
    {
        Config = config;
        Driver = driver;
    }

    public MdkSetting.DriverConfig Config { get; }
    public IDriver Driver { get; }
    public bool IsConnected => Driver.IsConnected;

    public static ConnectedDriver Open(MdkSetting.DriverConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        if (string.IsNullOrWhiteSpace(config.Type))
        {
            throw new InvalidOperationException("Driver type is empty.");
        }

        if (!DriverFactory.IsSupported(config.Type))
        {
            throw new InvalidOperationException(
                $"Driver type '{config.Type}' is not registered. Ensure plugins were discovered.");
        }

        var driver = DriverFactory.Create(config.Type);
        try
        {
            driver.Initialize(config);
            return new ConnectedDriver(CloneConfig(config), driver);
        }
        catch
        {
            driver.Dispose();
            throw;
        }
    }

    public void Dispose() => Driver.Dispose();

    private static MdkSetting.DriverConfig CloneConfig(MdkSetting.DriverConfig src) => new()
    {
        Id = src.Id,
        Type = src.Type,
        Enabled = src.Enabled,
        Parameters = new Dictionary<string, string>(src.Parameters, StringComparer.OrdinalIgnoreCase),
    };
}

/// <summary>Connects one or more drivers referenced by a platform / axis device.</summary>
public sealed class MultiDriverBag : IDisposable
{
    private readonly Dictionary<string, ConnectedDriver> _byId = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyDictionary<string, ConnectedDriver> Drivers => _byId;

    public ConnectedDriver GetOrOpen(MdkSetting setting, string driverId)
    {
        if (_byId.TryGetValue(driverId, out var existing))
        {
            return existing;
        }

        var cfg = setting.Drivers.FirstOrDefault(d =>
            string.Equals(d.Id, driverId, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"Driver '{driverId}' not found in setting.");

        var opened = ConnectedDriver.Open(cfg);
        _byId[driverId] = opened;
        return opened;
    }

    public void Dispose()
    {
        foreach (var d in _byId.Values)
        {
            d.Dispose();
        }

        _byId.Clear();
    }
}

/// <summary>Observable IO bit row for driver DI/DO grids.</summary>
public sealed class IoBitRow
{
    public short Group { get; set; }
    public short Index { get; set; }
    public string Label { get; set; } = string.Empty;
    public bool Value { get; set; }
}

/// <summary>Builds DI/DO bit rows from one or more 32-bit group words.</summary>
public static class IoBitGrid
{
    public const int BitsPerGroup = 32;
    public const int DefaultBitCount = 32;

    public static ObservableCollection<IoBitRow> FromWord(short group, int word, string prefix) =>
        FromWords(group, [word], BitsPerGroup, prefix);

    /// <summary>
    /// Expands consecutive group words into absolute bit rows (e.g. 128 bits → groups base..base+3).
    /// </summary>
    public static ObservableCollection<IoBitRow> FromWords(
        short baseGroup,
        IReadOnlyList<int> words,
        int bitCount,
        string prefix)
    {
        var rows = new ObservableCollection<IoBitRow>();
        var total = Math.Max(0, bitCount);
        for (var abs = 0; abs < total; abs++)
        {
            var groupOffset = abs / BitsPerGroup;
            var index = (short)(abs % BitsPerGroup);
            var word = groupOffset < words.Count ? words[groupOffset] : 0;
            rows.Add(new IoBitRow
            {
                Group = (short)(baseGroup + groupOffset),
                Index = index,
                Label = $"{prefix}{abs}",
                Value = (word & (1 << index)) != 0,
            });
        }

        return rows;
    }

    public static int ToWord(IEnumerable<IoBitRow> rows)
    {
        var word = 0;
        foreach (var row in rows)
        {
            if (row.Value)
            {
                word |= 1 << row.Index;
            }
        }

        return word;
    }

    /// <summary>Packs rows belonging to <paramref name="group"/> into a 32-bit word.</summary>
    public static int ToWord(IEnumerable<IoBitRow> rows, short group)
    {
        var word = 0;
        foreach (var row in rows)
        {
            if (row.Group == group && row.Value)
            {
                word |= 1 << row.Index;
            }
        }

        return word;
    }

    public static int GroupCount(int bitCount) =>
        Math.Max(1, (Math.Max(1, bitCount) + BitsPerGroup - 1) / BitsPerGroup);

    public static int ResolveBitCount(IReadOnlyDictionary<string, string>? parameters, string key, int fallback = DefaultBitCount)
    {
        if (parameters is not null
            && parameters.TryGetValue(key, out var raw)
            && int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n)
            && n > 0)
        {
            return Math.Min(n, 512);
        }

        return fallback;
    }
}

/// <summary>Formats driver parameter dictionary for display.</summary>
public static class ParamText
{
    public static string Format(IReadOnlyDictionary<string, string>? parameters)
    {
        if (parameters is null || parameters.Count == 0)
        {
            return "(empty)";
        }

        var sb = new StringBuilder();
        foreach (var kv in parameters.OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase))
        {
            sb.Append(kv.Key).Append(" = ").AppendLine(kv.Value);
        }

        return sb.ToString();
    }
}
