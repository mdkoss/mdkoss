using MDKOSS.Extensions;

namespace MDKOSS.Tests;

/// <summary>Discovers plugin DLLs once for the test host.</summary>
public static class TestPluginBootstrap
{
    private static readonly object Sync = new();
    private static bool _registered;

    public static void EnsureRegistered()
    {
        lock (Sync)
        {
            if (_registered)
            {
                return;
            }

            MdkExtensionHost.DiscoverAndRegister();
            _registered = true;
        }
    }
}
