namespace MDKOSS.Sample.DieBonder.Machine;

/// <summary>One execution-log line for the Die Bonder HMI.</summary>
public sealed record BondLogEntry(DateTime TimestampUtc, string Level, string Source, string Message);

/// <summary>In-memory ring buffer of bond-cycle logs (shared by tasks and API).</summary>
public static class BondLogStore
{
    private const int MaxEntries = 500;
    private static readonly object Sync = new();
    private static readonly LinkedList<BondLogEntry> Entries = new();

    public static void Info(string source, string message) => Add("INFO", source, message);

    public static void Warn(string source, string message) => Add("WARN", source, message);

    public static void Error(string source, string message) => Add("ERROR", source, message);

    public static void Add(string level, string source, string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        var entry = new BondLogEntry(
            DateTime.UtcNow,
            string.IsNullOrWhiteSpace(level) ? "INFO" : level.Trim().ToUpperInvariant(),
            string.IsNullOrWhiteSpace(source) ? "bond" : source.Trim(),
            message.Trim());

        lock (Sync)
        {
            Entries.AddLast(entry);
            while (Entries.Count > MaxEntries)
            {
                Entries.RemoveFirst();
            }
        }
    }

    public static IReadOnlyList<BondLogEntry> Snapshot(int limit = 200)
    {
        limit = Math.Clamp(limit, 1, MaxEntries);
        lock (Sync)
        {
            return Entries.Reverse().Take(limit).Reverse().ToList();
        }
    }

    public static void Clear()
    {
        lock (Sync)
        {
            Entries.Clear();
        }
    }

    public static int Count
    {
        get
        {
            lock (Sync)
            {
                return Entries.Count;
            }
        }
    }
}
