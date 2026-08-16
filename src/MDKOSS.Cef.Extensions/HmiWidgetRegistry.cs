using System.Reflection;
using System.Text.Json;

namespace MDKOSS.Cef.Extensions;

/// <summary>
/// Runtime catalog of HMI widgets. Built-in types load from <c>views/widgets/</c>
/// the same way as drop-in folders and <see cref="IHmiWidgetPackage"/> DLLs.
/// </summary>
public static class HmiWidgetRegistry
{
    private static readonly object Sync = new();
    private static readonly Dictionary<string, Entry> Items = new(StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> LoadedPackages = new(StringComparer.OrdinalIgnoreCase);
    private static bool _loaded;

    private static readonly JsonSerializerOptions ManifestJson = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public static IReadOnlyList<HmiWidgetDescriptor> All
    {
        get
        {
            EnsureLoaded();
            lock (Sync)
            {
                return Items.Values
                    .Select(e => e.Descriptor)
                    .OrderBy(d => d.Category, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(d => d.Type, StringComparer.OrdinalIgnoreCase)
                    .ToArray();
            }
        }
    }

    public static bool IsKnown(string? type)
        => Find(type) is not null;

    public static HmiWidgetDescriptor? Find(string? type)
    {
        if (string.IsNullOrWhiteSpace(type))
        {
            return null;
        }

        EnsureLoaded();
        lock (Sync)
        {
            return Items.TryGetValue(type.Trim(), out var entry) ? entry.Descriptor : null;
        }
    }

    /// <summary>Loads builtin packs, extra folders, and <see cref="IHmiWidgetPackage"/> types (once).</summary>
    public static void EnsureLoaded()
    {
        lock (Sync)
        {
            if (_loaded)
            {
                return;
            }

            RegisterPackageLocked(new BuiltinHmiWidgetPackage());
            DiscoverExtraFoldersLocked();
            DiscoverPackagesFromLoadedAssembliesLocked();
            _loaded = true;
        }
    }

    public static void RegisterPackage(IHmiWidgetPackage package)
    {
        ArgumentNullException.ThrowIfNull(package);
        lock (Sync)
        {
            RegisterPackageLocked(package);
        }
    }

    /// <summary>Registers one widget. First type wins unless <paramref name="replace"/> is true.</summary>
    public static void Register(HmiWidgetDescriptor descriptor, HmiWidgetAssets? assets = null, string? packageId = null, bool replace = false)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        lock (Sync)
        {
            RegisterLocked(descriptor, assets, packageId, replace);
        }
    }

    /// <summary>Loads every <c>{type}/widget.json</c> under <paramref name="widgetsRoot"/>.</summary>
    public static void DiscoverFolder(string widgetsRoot, string packageId)
    {
        if (string.IsNullOrWhiteSpace(widgetsRoot) || !Directory.Exists(widgetsRoot))
        {
            return;
        }

        lock (Sync)
        {
            DiscoverFolderLocked(widgetsRoot, packageId);
        }
    }

    public static bool TryGetAsset(string fileName, out string contentType, out string body)
    {
        contentType = "";
        body = "";
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return false;
        }

        var ext = Path.GetExtension(fileName);
        var type = Path.GetFileNameWithoutExtension(fileName);
        if (string.IsNullOrWhiteSpace(type))
        {
            return false;
        }

        EnsureLoaded();
        lock (Sync)
        {
            if (!Items.TryGetValue(type, out var entry))
            {
                return false;
            }

            if (ext.Equals(".js", StringComparison.OrdinalIgnoreCase))
            {
                if (!TryReadAsset(entry.ScriptPath, entry.ScriptBody, out body))
                {
                    return false;
                }

                contentType = "application/javascript; charset=utf-8";
                return true;
            }

            if (ext.Equals(".css", StringComparison.OrdinalIgnoreCase))
            {
                if (!TryReadAsset(entry.CssPath, entry.CssBody, out body))
                {
                    return false;
                }

                contentType = "text/css; charset=utf-8";
                return true;
            }
        }

        return false;
    }

    private static void RegisterPackageLocked(IHmiWidgetPackage package)
    {
        var id = string.IsNullOrWhiteSpace(package.Id) ? package.GetType().FullName ?? "package" : package.Id.Trim();
        if (!LoadedPackages.Add(id))
        {
            return;
        }

        package.Register(new Registration(id));
    }

    private static void RegisterLocked(HmiWidgetDescriptor descriptor, HmiWidgetAssets? assets, string? packageId, bool replace)
    {
        if (string.IsNullOrWhiteSpace(descriptor.Type))
        {
            throw new ArgumentException("Widget type cannot be empty.", nameof(descriptor));
        }

        var type = descriptor.Type.Trim();
        if (!replace && Items.ContainsKey(type))
        {
            return;
        }

        var hasScript = assets is not null
            && (!string.IsNullOrWhiteSpace(assets.ScriptPath) || !string.IsNullOrWhiteSpace(assets.Script));
        var hasCss = assets is not null
            && (!string.IsNullOrWhiteSpace(assets.CssPath) || !string.IsNullOrWhiteSpace(assets.Css));

        var filled = new HmiWidgetDescriptor
        {
            Type = type,
            DisplayName = string.IsNullOrWhiteSpace(descriptor.DisplayName) ? type : descriptor.DisplayName,
            Category = string.IsNullOrWhiteSpace(descriptor.Category) ? "扩展" : descriptor.Category,
            DefaultW = descriptor.DefaultW > 0 ? descriptor.DefaultW : 160,
            DefaultH = descriptor.DefaultH > 0 ? descriptor.DefaultH : 48,
            Props = descriptor.Props ?? [],
            Script = hasScript ? $"/api/hmi/widget/{Uri.EscapeDataString(type)}.js" : descriptor.Script,
            Css = hasCss ? $"/api/hmi/widget/{Uri.EscapeDataString(type)}.css" : descriptor.Css,
            Package = string.IsNullOrWhiteSpace(packageId) ? descriptor.Package : packageId,
        };

        Items[type] = new Entry
        {
            Descriptor = filled,
            ScriptPath = assets?.ScriptPath,
            CssPath = assets?.CssPath,
            ScriptBody = assets?.Script,
            CssBody = assets?.Css,
        };
    }

    private static void DiscoverFolderLocked(string widgetsRoot, string packageId)
    {
        foreach (var dir in Directory.EnumerateDirectories(widgetsRoot))
        {
            var manifestPath = Path.Combine(dir, "widget.json");
            if (!File.Exists(manifestPath))
            {
                continue;
            }

            try
            {
                var json = File.ReadAllText(manifestPath);
                var manifest = JsonSerializer.Deserialize<WidgetManifest>(json, ManifestJson);
                var type = string.IsNullOrWhiteSpace(manifest?.Type)
                    ? Path.GetFileName(dir)
                    : manifest!.Type.Trim();
                if (string.IsNullOrWhiteSpace(type) || Items.ContainsKey(type))
                {
                    continue;
                }

                var scriptPath = FirstExisting(dir, "widget.js", "render.js");
                var cssPath = FirstExisting(dir, "widget.css");
                var props = (manifest?.Props ?? [])
                    .Where(p => !string.IsNullOrWhiteSpace(p.Key))
                    .Select(p => new HmiWidgetProp
                    {
                        Key = p.Key.Trim(),
                        Label = string.IsNullOrWhiteSpace(p.Label) ? p.Key : p.Label,
                        Kind = string.IsNullOrWhiteSpace(p.Kind) ? "text" : p.Kind,
                        Default = p.Default,
                        Options = p.Options,
                    })
                    .ToArray();

                RegisterLocked(
                    new HmiWidgetDescriptor
                    {
                        Type = type,
                        DisplayName = string.IsNullOrWhiteSpace(manifest?.DisplayName) ? type : manifest!.DisplayName,
                        Category = string.IsNullOrWhiteSpace(manifest?.Category) ? "扩展" : manifest!.Category,
                        DefaultW = manifest?.DefaultW > 0 ? manifest.DefaultW : 160,
                        DefaultH = manifest?.DefaultH > 0 ? manifest.DefaultH : 48,
                        Props = props,
                    },
                    new HmiWidgetAssets
                    {
                        ScriptPath = scriptPath,
                        CssPath = cssPath,
                    },
                    packageId,
                    replace: false);
            }
            catch
            {
                // Skip a broken pack; other widgets still load.
            }
        }
    }

    private static void DiscoverExtraFoldersLocked()
    {
        foreach (var root in EnumerateExtraWidgetRoots())
        {
            DiscoverFolderLocked(root, "file:" + Path.GetFileName(Path.GetDirectoryName(root) ?? root));
        }
    }

    private static void DiscoverPackagesFromLoadedAssembliesLocked()
    {
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (assembly.IsDynamic)
            {
                continue;
            }

            Type[] types;
            try
            {
                types = assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                types = ex.Types.Where(t => t is not null).Cast<Type>().ToArray();
            }

            foreach (var type in types)
            {
                if (type is null
                    || type.IsAbstract
                    || type.IsInterface
                    || !typeof(IHmiWidgetPackage).IsAssignableFrom(type)
                    || type.GetConstructor(Type.EmptyTypes) is null)
                {
                    continue;
                }

                try
                {
                    if (Activator.CreateInstance(type) is IHmiWidgetPackage package)
                    {
                        RegisterPackageLocked(package);
                    }
                }
                catch
                {
                    // Skip a broken package type.
                }
            }
        }
    }

    internal static IEnumerable<string> EnumerateBuiltinWidgetRoots()
    {
        var candidates = new List<string?>
        {
            BesideAssembly("views", "widgets"),
            Path.Combine(AppContext.BaseDirectory, "views", "widgets"),
        };
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            candidates.Add(Path.Combine(dir.FullName, "src", "MDKOSS.Cef.Extensions", "views", "widgets"));
            if (dir.Parent is null)
            {
                break;
            }
        }

        foreach (var root in DistinctExisting(candidates.ToArray()))
        {
            yield return root;
        }
    }

    private static IEnumerable<string> EnumerateExtraWidgetRoots()
    {
        var baseDir = AppContext.BaseDirectory;
        var candidates = new List<string>
        {
            Path.Combine(baseDir, "plugins", "widgets"),
            Path.Combine(baseDir, "extensions", "widgets"),
        };

        foreach (var parent in new[]
                 {
                     Path.Combine(baseDir, "plugins"),
                     Path.Combine(baseDir, "extensions"),
                 })
        {
            if (!Directory.Exists(parent))
            {
                continue;
            }

            foreach (var sub in Directory.EnumerateDirectories(parent))
            {
                candidates.Add(Path.Combine(sub, "widgets"));
            }
        }

        return DistinctExisting(candidates.ToArray());
    }

    private static IEnumerable<string> DistinctExisting(params string?[] paths)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in paths)
        {
            if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
            {
                continue;
            }

            var full = Path.GetFullPath(path);
            if (seen.Add(full))
            {
                yield return full;
            }
        }
    }

    private static string? BesideAssembly(params string[] parts)
    {
        var asmDir = Path.GetDirectoryName(typeof(HmiWidgetRegistry).Assembly.Location);
        return string.IsNullOrEmpty(asmDir) ? null : Path.Combine(new[] { asmDir }.Concat(parts).ToArray());
    }

    private static string? FirstExisting(string dir, params string[] names)
    {
        foreach (var name in names)
        {
            var path = Path.Combine(dir, name);
            if (File.Exists(path))
            {
                return path;
            }
        }

        return null;
    }

    private static bool TryReadAsset(string? path, string? body, out string text)
    {
        if (!string.IsNullOrEmpty(body))
        {
            text = body;
            return true;
        }

        if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
        {
            text = File.ReadAllText(path);
            return true;
        }

        text = "";
        return false;
    }

    private sealed class Registration : IHmiWidgetRegistration
    {
        private readonly string _packageId;

        public Registration(string packageId) => _packageId = packageId;

        public void Widget(HmiWidgetDescriptor descriptor, HmiWidgetAssets? assets = null)
            => RegisterLocked(descriptor, assets, _packageId, replace: false);

        public void Folder(string widgetsRoot)
        {
            if (string.IsNullOrWhiteSpace(widgetsRoot) || !Directory.Exists(widgetsRoot))
            {
                return;
            }

            DiscoverFolderLocked(widgetsRoot, _packageId);
        }
    }

    private sealed class Entry
    {
        public required HmiWidgetDescriptor Descriptor { get; init; }

        public string? ScriptPath { get; init; }

        public string? CssPath { get; init; }

        public string? ScriptBody { get; init; }

        public string? CssBody { get; init; }
    }

    private sealed class WidgetManifest
    {
        public string? Type { get; set; }

        public string? DisplayName { get; set; }

        public string? Category { get; set; }

        public int DefaultW { get; set; }

        public int DefaultH { get; set; }

        public List<HmiWidgetPropDto>? Props { get; set; }
    }

    private sealed class HmiWidgetPropDto
    {
        public string Key { get; set; } = "";

        public string Label { get; set; } = "";

        public string Kind { get; set; } = "text";

        public string? Default { get; set; }

        public IReadOnlyList<string>? Options { get; set; }
    }
}
