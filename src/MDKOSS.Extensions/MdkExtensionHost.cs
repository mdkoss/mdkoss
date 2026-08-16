using System.Reflection;
using System.Runtime.Loader;

namespace MDKOSS.Extensions;

/// <summary>Options for discovering <see cref="IMdkExtension"/> plugin assemblies.</summary>
public sealed class ExtensionDiscoveryOptions
{
    /// <summary>Directories to scan (absolute or relative to <see cref="AppContext.BaseDirectory"/>).</summary>
    public IReadOnlyList<string>? SearchDirectories { get; init; }

    /// <summary>
    /// File-name glob patterns (e.g. <c>MDKOSS.Drivers.*.dll</c>).
    /// Matched against file names only, case-insensitive.
    /// </summary>
    public IReadOnlyList<string>? FileNamePatterns { get; init; }

    /// <summary>When true, also scan already-loaded assemblies in the default load context.</summary>
    public bool IncludeLoadedAssemblies { get; init; } = true;

    /// <summary>Optional diagnostics callback.</summary>
    public Action<string>? Log { get; init; }

    public static ExtensionDiscoveryOptions Default { get; } = new();
}

/// <summary>Result of an extension discovery pass.</summary>
public sealed class ExtensionDiscoveryResult
{
    public required IReadOnlyList<string> ScannedAssemblies { get; init; }

    public required IReadOnlyList<string> RegisteredExtensionIds { get; init; }

    public required IReadOnlyList<string> Errors { get; init; }
}

/// <summary>
/// Host-side entry for loading <see cref="IMdkExtension"/> packages.
/// Prefer <see cref="DiscoverAndRegister"/> so driver/device plugins load without host project references.
/// </summary>
public static class MdkExtensionHost
{
    private static readonly object Sync = new();
    private static readonly Dictionary<string, IMdkExtension> Registered = new(StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> LoadedPluginPaths = new(StringComparer.OrdinalIgnoreCase);

    private static readonly string[] DefaultFileNamePatterns =
    [
        "MDKOSS.Drivers.*.dll",
        "MDKOSS.Extensions.*.dll",
        "MDKOSS.Cef.Extensions.dll",
        "MDKOSS.Cef.Extensions.*.dll",
        "MDKOSS.Pnp.dll",
    ];

    /// <summary>Registers an extension once (idempotent by <see cref="IMdkExtension.Id"/>).</summary>
    public static void Register(IMdkExtension extension)
    {
        ArgumentNullException.ThrowIfNull(extension);
        if (string.IsNullOrWhiteSpace(extension.Id))
        {
            throw new ArgumentException("Extension Id cannot be empty.", nameof(extension));
        }

        lock (Sync)
        {
            var id = extension.Id.Trim();
            if (Registered.ContainsKey(id))
            {
                return;
            }

            extension.Register(new ExtensionRegistration());
            Registered[id] = extension;
        }
    }

    /// <summary>Registers multiple extensions in order.</summary>
    public static void RegisterAll(params IMdkExtension[] extensions)
    {
        ArgumentNullException.ThrowIfNull(extensions);
        foreach (var extension in extensions)
        {
            Register(extension);
        }
    }

    /// <summary>
    /// Scans default / configured directories for plugin DLLs, loads them, and registers all
    /// public parameterless <see cref="IMdkExtension"/> implementations.
    /// </summary>
    public static ExtensionDiscoveryResult DiscoverAndRegister(ExtensionDiscoveryOptions? options = null)
    {
        EnsureDefaultContextPluginResolve();
        options ??= ExtensionDiscoveryOptions.Default;
        var log = options.Log ?? (_ => { });
        var errors = new List<string>();
        var scanned = new List<string>();
        var registeredBefore = new HashSet<string>(RegisteredIds, StringComparer.OrdinalIgnoreCase);

        var roots = ResolveSearchDirectories(options.SearchDirectories);
        var patterns = options.FileNamePatterns is { Count: > 0 }
            ? options.FileNamePatterns
            : DefaultFileNamePatterns;

        foreach (var root in roots)
        {
            if (!Directory.Exists(root))
            {
                continue;
            }

            log($"Scanning extension plugins in: {root}");
            foreach (var dllPath in EnumerateCandidateDlls(root, patterns))
            {
                TryLoadAndRegister(dllPath, scanned, errors, log);
            }
        }

        if (options.IncludeLoadedAssemblies)
        {
            foreach (var assembly in AssemblyLoadContext.Default.Assemblies.ToArray())
            {
                if (assembly.IsDynamic || string.IsNullOrEmpty(assembly.Location))
                {
                    continue;
                }

                var fileName = Path.GetFileName(assembly.Location);
                if (!MatchesAnyPattern(fileName, patterns) || IsFrameworkOrHostAssembly(fileName))
                {
                    continue;
                }

                TryRegisterFromAssembly(assembly, scanned, errors, log);
            }
        }

        var newlyRegistered = RegisteredIds
            .Where(id => !registeredBefore.Contains(id))
            .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        log($"Extension discovery finished. newlyRegistered=[{string.Join(", ", newlyRegistered)}]");

        return new ExtensionDiscoveryResult
        {
            ScannedAssemblies = scanned,
            RegisteredExtensionIds = RegisteredIds,
            Errors = errors,
        };
    }

    /// <summary>Returns whether an extension with the given id has already been registered.</summary>
    public static bool IsRegistered(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return false;
        }

        lock (Sync)
        {
            return Registered.ContainsKey(id.Trim());
        }
    }

    /// <summary>Snapshot of registered extension ids.</summary>
    public static IReadOnlyList<string> RegisteredIds
    {
        get
        {
            lock (Sync)
            {
                return Registered.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase).ToArray();
            }
        }
    }

    private static int _defaultResolveHooked;

    /// <summary>
    /// Plugin assemblies live in a collectible-false <see cref="PluginLoadContext"/>, but some
    /// dependencies (System.IO.Ports) are requested from the default context. Probe plugins/ and RID folders.
    /// </summary>
    private static void EnsureDefaultContextPluginResolve()
    {
        if (Interlocked.Exchange(ref _defaultResolveHooked, 1) != 0)
        {
            return;
        }

        AssemblyLoadContext.Default.Resolving += (_, assemblyName) =>
        {
            var name = assemblyName.Name;
            if (string.IsNullOrEmpty(name) || name.StartsWith("System.Runtime", StringComparison.Ordinal))
            {
                return null;
            }

            var fileName = name + ".dll";
            var rid = System.Runtime.InteropServices.RuntimeInformation.RuntimeIdentifier;
            var baseDir = AppContext.BaseDirectory;
            var plugins = Path.Combine(baseDir, "plugins");
            foreach (var dir in new[]
            {
                baseDir,
                plugins,
                Path.Combine(baseDir, "runtimes", "win", "lib", "net8.0"),
                Path.Combine(plugins, "runtimes", "win", "lib", "net8.0"),
                Path.Combine(baseDir, "runtimes", rid, "lib", "net8.0"),
                Path.Combine(plugins, "runtimes", rid, "lib", "net8.0"),
            })
            {
                var path = Path.Combine(dir, fileName);
                if (!File.Exists(path))
                {
                    continue;
                }

                try
                {
                    return AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.GetFullPath(path));
                }
                catch
                {
                    // Another context may already own this path.
                }
            }

            return null;
        };
    }

    private static void TryLoadAndRegister(
        string dllPath,
        List<string> scanned,
        List<string> errors,
        Action<string> log)
    {
        var fullPath = Path.GetFullPath(dllPath);
        var fileName = Path.GetFileName(fullPath);
        if (IsFrameworkOrHostAssembly(fileName))
        {
            return;
        }

        lock (Sync)
        {
            if (LoadedPluginPaths.Contains(fullPath))
            {
                return;
            }
        }

        try
        {
            var assembly = LoadPluginAssembly(fullPath);
            lock (Sync)
            {
                LoadedPluginPaths.Add(fullPath);
            }

            TryRegisterFromAssembly(assembly, scanned, errors, log);
        }
        catch (Exception ex)
        {
            var message = $"Failed to load plugin '{fullPath}': {ex.Message}";
            errors.Add(message);
            log(message);
        }
    }

    private static void TryRegisterFromAssembly(
        Assembly assembly,
        List<string> scanned,
        List<string> errors,
        Action<string> log)
    {
        var location = string.IsNullOrEmpty(assembly.Location) ? assembly.GetName().Name ?? "?" : assembly.Location;
        if (!scanned.Contains(location, StringComparer.OrdinalIgnoreCase))
        {
            scanned.Add(location);
        }

        Type[] types;
        try
        {
            types = assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            types = ex.Types.Where(t => t is not null).Cast<Type>().ToArray();
            foreach (var loaderEx in ex.LoaderExceptions.Where(e => e is not null))
            {
                errors.Add($"Type load error in '{location}': {loaderEx!.Message}");
            }
        }

        foreach (var type in types)
        {
            if (type is null
                || type.IsAbstract
                || type.IsInterface
                || !typeof(IMdkExtension).IsAssignableFrom(type))
            {
                continue;
            }

            var ctor = type.GetConstructor(Type.EmptyTypes);
            if (ctor is null)
            {
                errors.Add($"Skip {type.FullName}: no public parameterless constructor.");
                continue;
            }

            try
            {
                if (Activator.CreateInstance(type) is not IMdkExtension extension)
                {
                    continue;
                }

                var already = IsRegistered(extension.Id);
                Register(extension);
                if (!already)
                {
                    log($"Registered extension '{extension.Id}' ({extension.DisplayName}) from {Path.GetFileName(location)}");
                }
            }
            catch (Exception ex)
            {
                var message = $"Failed to register '{type.FullName}': {ex.Message}";
                errors.Add(message);
                log(message);
            }
        }
    }

    private static Assembly LoadPluginAssembly(string fullPath)
    {
        var asmName = AssemblyName.GetAssemblyName(fullPath);
        var simpleName = asmName.Name;

        // Prefer an already-loaded Default-ALC copy (host ProjectReference).
        // Otherwise Sample/DieBonder `is TrayDevice` fails when plugins/ loads a second identity.
        foreach (var loaded in AssemblyLoadContext.Default.Assemblies)
        {
            if (loaded.IsDynamic)
            {
                continue;
            }

            if (!string.IsNullOrEmpty(loaded.Location)
                && string.Equals(Path.GetFullPath(loaded.Location), fullPath, StringComparison.OrdinalIgnoreCase))
            {
                return loaded;
            }

            if (!string.IsNullOrEmpty(simpleName)
                && string.Equals(loaded.GetName().Name, simpleName, StringComparison.OrdinalIgnoreCase))
            {
                return loaded;
            }
        }

        // Host output directory DLLs (ProjectReference copies) must share Default ALC types.
        var baseDir = Path.GetFullPath(AppContext.BaseDirectory)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var dllDir = Path.GetFullPath(Path.GetDirectoryName(fullPath) ?? fullPath)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (string.Equals(baseDir, dllDir, StringComparison.OrdinalIgnoreCase))
        {
            return AssemblyLoadContext.Default.LoadFromAssemblyPath(fullPath);
        }

        var context = new PluginLoadContext(fullPath);
        return context.LoadFromAssemblyPath(fullPath);
    }

    private static IEnumerable<string> ResolveSearchDirectories(IReadOnlyList<string>? configured)
    {
        var baseDir = AppContext.BaseDirectory;
        var list = new List<string>();

        void Add(string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            var full = Path.IsPathRooted(path)
                ? Path.GetFullPath(path)
                : Path.GetFullPath(Path.Combine(baseDir, path));
            if (!list.Contains(full, StringComparer.OrdinalIgnoreCase))
            {
                list.Add(full);
            }
        }

        if (configured is { Count: > 0 })
        {
            foreach (var dir in configured)
            {
                Add(dir);
            }
        }
        else
        {
            Add(baseDir);
            Add("plugins");
            Add("extensions");
        }

        return list;
    }

    private static IEnumerable<string> EnumerateCandidateDlls(string root, IReadOnlyList<string> patterns)
    {
        foreach (var file in Directory.EnumerateFiles(root, "*.dll", SearchOption.TopDirectoryOnly))
        {
            var name = Path.GetFileName(file);
            if (MatchesAnyPattern(name, patterns) && !IsFrameworkOrHostAssembly(name))
            {
                yield return file;
            }
        }

        // One-level subfolders under plugins/ (e.g. plugins/Serial/MDKOSS.Extensions.Serial.dll)
        foreach (var sub in Directory.EnumerateDirectories(root))
        {
            foreach (var file in Directory.EnumerateFiles(sub, "*.dll", SearchOption.TopDirectoryOnly))
            {
                var name = Path.GetFileName(file);
                if (MatchesAnyPattern(name, patterns) && !IsFrameworkOrHostAssembly(name))
                {
                    yield return file;
                }
            }
        }
    }

    private static bool MatchesAnyPattern(string fileName, IReadOnlyList<string> patterns)
    {
        foreach (var pattern in patterns)
        {
            if (MatchesGlob(fileName, pattern))
            {
                return true;
            }
        }

        return false;
    }

    private static bool MatchesGlob(string fileName, string pattern)
    {
        // Simple * wildcard matcher for file names.
        pattern = pattern.Trim();
        if (pattern.Length == 0)
        {
            return false;
        }

        var parts = pattern.Split('*');
        if (parts.Length == 1)
        {
            return fileName.Equals(pattern, StringComparison.OrdinalIgnoreCase);
        }

        var remaining = fileName.AsSpan();
        if (!fileName.StartsWith(parts[0], StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        remaining = remaining[parts[0].Length..];
        for (var i = 1; i < parts.Length; i++)
        {
            var part = parts[i];
            if (part.Length == 0)
            {
                continue;
            }

            var idx = remaining.ToString().IndexOf(part, StringComparison.OrdinalIgnoreCase);
            if (idx < 0)
            {
                return false;
            }

            remaining = remaining[(idx + part.Length)..];
        }

        return parts[^1].Length == 0 || remaining.Length == 0 || fileName.EndsWith(parts[^1], StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsFrameworkOrHostAssembly(string fileName)
    {
        return fileName.Equals("MDKOSS.Core.dll", StringComparison.OrdinalIgnoreCase)
               || fileName.Equals("MDKOSS.Extensions.dll", StringComparison.OrdinalIgnoreCase)
               || fileName.Equals("MDKOSS.Cef.dll", StringComparison.OrdinalIgnoreCase)
               || fileName.Equals("MDKOSS.Sample.dll", StringComparison.OrdinalIgnoreCase)
               || fileName.Equals("MDKOSS.Cef.Sample.dll", StringComparison.OrdinalIgnoreCase)
               || fileName.Equals("MDKOSS.Config.dll", StringComparison.OrdinalIgnoreCase)
               || fileName.StartsWith("System.", StringComparison.OrdinalIgnoreCase)
               || fileName.StartsWith("Microsoft.", StringComparison.OrdinalIgnoreCase)
               || fileName.StartsWith("netstandard", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Loads a plugin assembly so shared MDKOSS.Core / MDKOSS.Extensions resolve from the default context.
    /// Extra probe paths cover NuGet RID assets copied beside the plugin (e.g. System.IO.Ports).
    /// </summary>
    private sealed class PluginLoadContext : AssemblyLoadContext
    {
        private readonly AssemblyDependencyResolver _resolver;
        private readonly string _pluginDirectory;

        public PluginLoadContext(string pluginPath)
            : base(isCollectible: false)
        {
            _resolver = new AssemblyDependencyResolver(pluginPath);
            _pluginDirectory = Path.GetDirectoryName(Path.GetFullPath(pluginPath)) ?? AppContext.BaseDirectory;
        }

        protected override Assembly? Load(AssemblyName assemblyName)
        {
            var name = assemblyName.Name;
            if (string.IsNullOrEmpty(name))
            {
                return null;
            }

            // Prefer already-loaded shared runtime assemblies.
            foreach (var loaded in Default.Assemblies)
            {
                if (string.Equals(loaded.GetName().Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    return loaded;
                }
            }

            var path = _resolver.ResolveAssemblyToPath(assemblyName)
                       ?? ProbeManagedAssembly(name);
            return path is null ? null : LoadFromAssemblyPath(path);
        }

        protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
        {
            var path = _resolver.ResolveUnmanagedDllToPath(unmanagedDllName)
                       ?? ProbeNativeLibrary(unmanagedDllName);
            return path is null ? IntPtr.Zero : LoadUnmanagedDllFromPath(path);
        }

        private string? ProbeManagedAssembly(string assemblyName)
        {
            var fileName = assemblyName + ".dll";
            foreach (var dir in EnumerateProbeDirectories())
            {
                var candidate = Path.Combine(dir, fileName);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }

            return null;
        }

        private string? ProbeNativeLibrary(string unmanagedDllName)
        {
            var fileName = unmanagedDllName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
                ? unmanagedDllName
                : unmanagedDllName + ".dll";

            foreach (var dir in EnumerateProbeDirectories())
            {
                var candidate = Path.Combine(dir, fileName);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }

            return null;
        }

        private IEnumerable<string> EnumerateProbeDirectories()
        {
            var rid = System.Runtime.InteropServices.RuntimeInformation.RuntimeIdentifier;
            var baseDir = AppContext.BaseDirectory;
            string[] roots = [_pluginDirectory, baseDir];
            string[] tfms = ["net8.0", "net9.0"];

            foreach (var root in roots)
            {
                yield return root;
                yield return Path.Combine(root, "runtimes", rid, "native");
                yield return Path.Combine(root, "runtimes", "win", "native");
                foreach (var tfm in tfms)
                {
                    yield return Path.Combine(root, "runtimes", rid, "lib", tfm);
                    yield return Path.Combine(root, "runtimes", "win", "lib", tfm);
                }
            }
        }
    }
}
