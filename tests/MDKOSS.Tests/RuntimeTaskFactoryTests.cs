using MDKOSS.Core;

namespace MDKOSS.Tests;

public sealed class RuntimeTaskFactoryTests
{
    [Fact]
    public void IsSupported_recognizes_builtin_types()
    {
        Assert.True(RuntimeTaskFactory.IsSupported("pollDriver"));
        Assert.True(RuntimeTaskFactory.IsSupported("operation"));
        Assert.True(RuntimeTaskFactory.IsSupported("motion"));
        Assert.False(RuntimeTaskFactory.IsSupported("unknown-task"));
    }
}
