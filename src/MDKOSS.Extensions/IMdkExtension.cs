namespace MDKOSS.Extensions;

/// <summary>
/// Unified contract for optional driver / device / API extension packages.
/// Implement this in a separate assembly, then call
/// <see cref="MdkExtensionHost.Register"/> before creating <see cref="Core.MdkRuntime"/>.
/// </summary>
public interface IMdkExtension
{
    /// <summary>Stable extension id used for idempotent registration (e.g. <c>driver-sim</c>, <c>serial</c>, <c>camera</c>).</summary>
    string Id { get; }

    /// <summary>Human-readable display name for diagnostics.</summary>
    string DisplayName { get; }

    /// <summary>
    /// Registers devices, actions, monitoring modules, tasks, drivers, and static pages
    /// through the unified <paramref name="registration"/> facade.
    /// </summary>
    void Register(IExtensionRegistration registration);
}
