using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.InteropServices;

namespace MDKOSS.Extensions.Camera;

/// <summary>Lets <c>nativeDll</c> override the hardcoded [DllImport] file name for this assembly.</summary>
internal static class NativeDllMap
{
    private static readonly ConcurrentDictionary<string, string> Map = new(StringComparer.OrdinalIgnoreCase);
    private static int _hooked;

    public static void Bind(string importedName, string actualFile)
    {
        EnsureHook();
        if (string.IsNullOrWhiteSpace(importedName) || string.IsNullOrWhiteSpace(actualFile))
        {
            return;
        }

        Map[importedName] = actualFile.Trim();
    }

    private static void EnsureHook()
    {
        if (Interlocked.Exchange(ref _hooked, 1) != 0)
        {
            return;
        }

        NativeLibrary.SetDllImportResolver(typeof(NativeDllMap).Assembly, Resolve);
    }

    private static IntPtr Resolve(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
    {
        var file = Map.TryGetValue(libraryName, out var mapped) ? mapped : libraryName;
        return NativeLibrary.TryLoad(file, assembly, searchPath, out var handle) ? handle : IntPtr.Zero;
    }
}
