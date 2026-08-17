using MDKOSS.Core;

namespace MDKOSS.Tests.Core;

public sealed class RuntimeTaskFactoryTests
{
    [Fact]
    public void IsSupported_recognizes_builtin_types()
    {
        Assert.True(RuntimeTaskFactory.IsSupported("pollDriver"));
        Assert.True(RuntimeTaskFactory.IsSupported("operation"));
        Assert.True(RuntimeTaskFactory.IsSupported("machine"));
        Assert.True(RuntimeTaskFactory.IsSupported("motion"));
        Assert.True(RuntimeTaskFactory.IsSupported("flow"));
        Assert.True(RuntimeTaskFactory.IsSupported("script"));
        Assert.False(RuntimeTaskFactory.IsSupported("unknown-task"));
    }
}
