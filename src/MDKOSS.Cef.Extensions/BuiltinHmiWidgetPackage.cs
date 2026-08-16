namespace MDKOSS.Cef.Extensions;

/// <summary>Loads the shipped widgets from <c>views/widgets/{type}/</c> as a normal package.</summary>
public sealed class BuiltinHmiWidgetPackage : IHmiWidgetPackage
{
    public string Id => "hmi-builtin";

    public void Register(IHmiWidgetRegistration registration)
    {
        ArgumentNullException.ThrowIfNull(registration);
        foreach (var root in HmiWidgetRegistry.EnumerateBuiltinWidgetRoots())
        {
            registration.Folder(root);
        }
    }
}
