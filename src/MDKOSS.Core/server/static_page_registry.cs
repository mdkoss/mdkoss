namespace MDKOSS.Core.Monitor;

/// <summary>
/// Registry for optional static HTML pages supplied by extension / machine assemblies.
/// </summary>
public static class StaticPageRegistry
{
    private static readonly Dictionary<string, Func<string>> Pages = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Registers a static page. <paramref name="path"/> should look like <c>/monitorPnp.html</c>.
    /// </summary>
    public static void Register(string path, Func<string> htmlFactory)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("Path cannot be empty.", nameof(path));
        }

        ArgumentNullException.ThrowIfNull(htmlFactory);
        var key = NormalizePath(path);
        Pages[key] = htmlFactory;
    }

    /// <summary>Returns a snapshot of registered pages (path → HTML).</summary>
    public static IReadOnlyDictionary<string, string> CreatePages()
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (path, factory) in Pages)
        {
            result[path] = factory();
        }

        return result;
    }

    private static string NormalizePath(string path)
    {
        path = path.Trim();
        if (!path.StartsWith('/'))
        {
            path = "/" + path;
        }

        return path.TrimEnd('/');
    }
}
