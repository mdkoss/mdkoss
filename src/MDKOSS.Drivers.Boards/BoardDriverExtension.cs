using MDKOSS.Core.Drivers;
using MDKOSS.Extensions;

namespace MDKOSS.Drivers.Boards;

/// <summary>Registers catalog motion-card <c>type</c> keys (zmc / adt / mpc / …).</summary>
public sealed class BoardDriverExtension : IMdkExtension
{
    public string Id => "driver-boards";

    public string DisplayName => "Catalog motion cards (simulate-first)";

    public void Register(IExtensionRegistration registration)
    {
        ArgumentNullException.ThrowIfNull(registration);
        foreach (var kind in BoardCatalog.All)
        {
            var captured = kind;
            registration.Driver(captured.Type, () => new BoardCardDriver(captured));
        }
    }
}

/// <summary>Call once before creating <see cref="Core.MdkRuntime"/> when not using plugin scan.</summary>
public static class BoardDriverBootstrap
{
    public static void Register() => MdkExtensionHost.Register(new BoardDriverExtension());
}
