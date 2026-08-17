using System.Reflection;

namespace MDKOSS.Core;

/// <summary>
/// Product identity for MDKOSS runtime assemblies (shared across hosts and APIs).
/// Keep in sync with <c>src/Directory.Build.props</c> and the version called out in root <c>readme.md</c> / <c>src/README.md</c>.
/// </summary>
public static class MdkProduct
{
    /// <summary>Semantic product version (e.g. 1.2.0); exposed on <c>/api/status</c> and About UI.</summary>
    public static string Version { get; } = ResolveVersion();

    /// <summary>
    /// GitHub release / Actions tag for this build (<c>v</c> + <see cref="Version"/>),
    /// matching workflow trigger <c>refs/tags/v*</c>.
    /// </summary>
    public static string ReleaseTag => "v" + Version;

    /// <summary>Canonical repository URL for the mdkoss organization project.</summary>
    public const string GitHubRepoUrl = "https://github.com/mdkoss/mdkoss";

    /// <summary>Canonical releases page for this repository.</summary>
    public const string GitHubReleasesUrl = "https://github.com/mdkoss/mdkoss/releases";

    /// <summary>Org promo landing path served by <c>MonitoringServer</c> (see <c>src/site/index.html</c>).</summary>
    public const string PromoPagePath = "/promo.html";

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
