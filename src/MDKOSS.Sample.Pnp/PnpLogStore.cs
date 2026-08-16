namespace MDKOSS.Pnp;

/// <summary>One execution-log line for the PNP HMI.</summary>
public sealed record PnpLogEntry(DateTime TimestampUtc, string Level, string Source, string Message);

/// <summary>In-memory ring buffer of PNP execution logs (shared by tasks and API).</summary>
public static class PnpLogStore
{
    private const int MaxEntries = 500;
    private static readonly object Sync = new();
    private static readonly LinkedList<PnpLogEntry> Entries = new();

    public static void Info(string source, string message) => Add("INFO", source, message);

    public static void Warn(string source, string message) => Add("WARN", source, message);

    public static void Error(string source, string message) => Add("ERROR", source, message);

    public static void Add(string level, string source, string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        var entry = new PnpLogEntry(
            DateTime.UtcNow,
            string.IsNullOrWhiteSpace(level) ? "INFO" : level.Trim().ToUpperInvariant(),
            string.IsNullOrWhiteSpace(source) ? "pnp" : source.Trim(),
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

    public static IReadOnlyList<PnpLogEntry> Snapshot(int limit = 200)
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
