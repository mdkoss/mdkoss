using MDKOSS.Core;

namespace MDKOSS.Tests.Core;

public sealed class MdkProductTests
{
    [Fact]
    public void Version_is_semver_and_matches_release_1_1_0()
    {
        Assert.False(string.IsNullOrWhiteSpace(MdkProduct.Version));
        Assert.Matches(@"^\d+\.\d+\.\d+", MdkProduct.Version);
        Assert.Equal("1.1.0", MdkProduct.Version);
    }

    [Fact]
    public void ReleaseTag_matches_github_actions_v_prefix()
    {
        Assert.Equal("v" + MdkProduct.Version, MdkProduct.ReleaseTag);
        Assert.Matches(@"^v\d+\.\d+\.\d+", MdkProduct.ReleaseTag);
        Assert.StartsWith("https://github.com/mdkoss/mdkoss/releases", MdkProduct.GitHubReleasesUrl);
    }
}
