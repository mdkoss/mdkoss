using MDKOSS.Core;
using MDKOSS.Core.Drivers;
using MDKOSS.Pnp;

namespace MDKOSS.Tests.Extensions;

/// <summary>
/// Sample DieBonder casts tray devices to <see cref="TrayDevice"/>. That fails when
/// MDKOSS.Pnp is loaded once via ProjectReference and again via PluginLoadContext.
/// </summary>
public sealed class PnpTrayTypeIdentityTests
{
    public PnpTrayTypeIdentityTests()
    {
        _ = typeof(TrayDevice);
        TestPluginBootstrap.EnsureRegistered();
    }

    [Fact]
    public void Tray_factory_returns_TrayDevice_matching_host_reference()
    {
        Assert.Contains("pnp", MDKOSS.Extensions.MdkExtensionHost.RegisteredIds);

        var cfg = new MdkSetting.DeviceConfig
        {
            Id = "tray-test",
            Name = "Tray Test",
            Type = "tray",
            Enabled = true,
            Parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["rows"] = "2",
                ["cols"] = "2",
                ["originX"] = "0",
                ["originY"] = "0",
                ["pitchX"] = "1",
                ["pitchY"] = "1",
                ["pickZ"] = "-1",
                ["safeZ"] = "0",
                ["startIndex"] = "0",
            },
        };

        var vars = new MVarStore();
        var drivers = new Dictionary<string, IDriver>(StringComparer.OrdinalIgnoreCase);
        Assert.True(
            DeviceExtensionRegistry.TryCreate("tray", cfg, cfg.Name, vars, drivers, out var device),
            "tray device factory should be registered");
        Assert.NotNull(device);
        Assert.IsType<TrayDevice>(device);
        var tray = (TrayDevice)device!;
        Assert.False(tray.IsExhausted);
        Assert.True(tray.TryGetCurrentNest(out _));
    }
}
