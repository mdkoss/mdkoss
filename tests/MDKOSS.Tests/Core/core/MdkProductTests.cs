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
}
