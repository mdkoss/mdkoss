using MDKOSS.Core;

namespace MDKOSS.Tests.Core;

/// <summary>Org promo static page under src/site, served as /promo.html.</summary>
public sealed class PromoSiteTests
{
    [Fact]
    public void Promo_page_exists_and_promotes_mdkoss()
    {
        var path = FindPromoIndex();
        Assert.True(File.Exists(path), $"missing promo page at {path}");

        var html = File.ReadAllText(path);
        Assert.Contains("MDKOSS", html, StringComparison.Ordinal);
        Assert.Contains(MdkProduct.GitHubRepoUrl, html, StringComparison.Ordinal);
        Assert.Contains(MdkProduct.GitHubReleasesUrl, html, StringComparison.Ordinal);
        Assert.Contains("开源简化设备运行时", html, StringComparison.Ordinal);
        Assert.Contains("配置流程", html, StringComparison.Ordinal);
        Assert.Contains("hero", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Product_constants_point_at_promo_route()
    {
        Assert.Equal("/promo.html", MdkProduct.PromoPagePath);
        Assert.StartsWith("https://github.com/mdkoss/", MdkProduct.GitHubRepoUrl, StringComparison.Ordinal);
    }

    private static string FindPromoIndex()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "src", "site", "index.html");
            if (File.Exists(candidate))
            {
                return candidate;
            }

            dir = dir.Parent;
        }

        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "site", "index.html"));
    }
}
