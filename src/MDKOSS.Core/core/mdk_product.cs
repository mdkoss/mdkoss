using System.Reflection;

namespace MDKOSS.Core;

/// <summary>
/// Product identity for MDKOSS runtime assemblies (shared across hosts and APIs).
/// </summary>
public static class MdkProduct
{
    /// <summary>Semantic product version, e.g. 1.1.0.</summary>
    public static string Version { get; } = ResolveVersion();

    private static string ResolveVersion()
    {
        var asm = typeof(MdkProduct).Assembly;
        var info = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(info))
        {
            // Strip optional "+build" / commit metadata from InformationalVersion.
            var plus = info.IndexOf('+');
            return plus >= 0 ? info[..plus] : info.Trim();
        }

        var ver = asm.GetName().Version;
        return ver is null ? "0.0.0" : $"{ver.Major}.{ver.Minor}.{ver.Build}";
    }
}
