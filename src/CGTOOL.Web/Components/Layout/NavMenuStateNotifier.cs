namespace CGTOOL.Web.Components.Layout;

/// <summary>
/// Lets the Settings screen tell the nav menu that it has changed, so an edit shows up in the
/// sidebar as it is made rather than after a full page load.
///
/// Scoped, so it is per-circuit: it covers the admin doing the editing, which is who is looking at
/// the sidebar expecting it to change. Everyone else picks the change up on their next navigation,
/// when NavMenu re-reads the overrides.
/// </summary>
public sealed class NavMenuStateNotifier
{
    public event Func<Task>? Changed;

    public Task NotifyChangedAsync() => Changed?.Invoke() ?? Task.CompletedTask;
}
