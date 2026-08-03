using System.Runtime.CompilerServices;

namespace MDKOSS.Tests;

internal static class ModuleInit
{
    [ModuleInitializer]
    internal static void Init() => TestPluginBootstrap.EnsureRegistered();
}
