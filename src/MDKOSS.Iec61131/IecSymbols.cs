namespace MDKOSS.Iec61131;

/// <summary>Source-key → IEC identifier table for one export.</summary>
public sealed class IecSymbols
{
    private readonly Dictionary<string, string> _map = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _used;

    public IecSymbols(IEnumerable<string>? reserved = null)
    {
        _used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (reserved is null)
        {
            return;
        }

        foreach (var r in reserved)
        {
            _used.Add(r);
        }
    }

    public IReadOnlyDictionary<string, string> Map => _map;

    public string Register(string sourceKey, string? preferred = null)
    {
        if (_map.TryGetValue(sourceKey, out var existing))
        {
            return existing;
        }

        var name = IecNames.Unique(preferred ?? sourceKey, _used);
        _map[sourceKey] = name;
        return name;
    }

    /// <summary>Maps <paramref name="sourceKey"/> onto an already unique IEC name.</summary>
    public string Alias(string sourceKey, string iecName)
    {
        _map[sourceKey] = iecName;
        _used.Add(iecName);
        return iecName;
    }

    public string Resolve(string ident)
    {
        if (ident.Equals("true", StringComparison.OrdinalIgnoreCase)
            || ident.Equals("false", StringComparison.OrdinalIgnoreCase))
        {
            return ident.Equals("true", StringComparison.OrdinalIgnoreCase) ? "TRUE" : "FALSE";
        }

        if (_map.TryGetValue(ident, out var name))
        {
            return name;
        }

        var sanitized = IecNames.Sanitize(ident);
        if (_map.TryGetValue(sanitized, out name))
        {
            return name;
        }

        return Register(ident, sanitized);
    }

    public bool TryGet(string sourceKey, out string name) => _map.TryGetValue(sourceKey, out name!);
}
