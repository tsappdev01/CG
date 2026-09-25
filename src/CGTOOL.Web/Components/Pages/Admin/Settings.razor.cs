using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using CGTOOL.Web.Components.Layout;
using CGTOOL.Web.Data;
using CGTOOL.Web.Data.Governance;

namespace CGTOOL.Web.Components.Pages.Admin;

public partial class Settings
{


    // Menu Order: one group per reorderable list in NavMenu -- the top-level sections, plus each
    // group's children. ParentKey null = the top-level sections themselves.
    private record OrderGroup(string? ParentKey, string Title);

    private static readonly OrderGroup[] OrderGroups =
    [
        new(null, "Main sections"),
        new("MyAccount", "My Account"),
        new("AdminPanel", "Admin Panel"),
        new("AdminPanel.AuditLogs", "Admin Panel — Audit Logs"),
        new("DeclarationSetup", "Declaration Setup"),
        new("Reports", "Reports"),
        new("InvestorRelations", "Investor Relations"),
        new("RpTransactions", "RP Transactions"),
    ];

    private Dictionary<string, int> _navOrder = [];
    private Dictionary<string, string> _navLabels = [];
    private Dictionary<string, string> _labelEdits = [];
    private Dictionary<string, NavMenuItemState> _navVisibility = [];
    private NavMenuCatalogItem? _pendingRemoval;

    // Only one section is open at a time, matching how the nav menu itself behaves. "Main sections"
    // starts open because it is the one people come here for most.
    private readonly HashSet<string> _openSections = ["Main sections"];

    private bool IsSectionOpen(string title) => _openSections.Contains(title);

    private void ToggleSection(string title)
    {
        if (!_openSections.Remove(title))
        {
            _openSections.Clear();
            _openSections.Add(title);
        }
    }

    /// <summary>This item's own setting, ignoring its ancestors -- the row edits the item itself.</summary>
    private NavMenuItemState StateOf(NavMenuCatalogItem item) =>
        _navVisibility.GetValueOrDefault(item.Key, NavMenuItemState.Visible);

    private int VisibleCount(List<NavMenuCatalogItem> items) => items.Count(i => StateOf(i) == NavMenuItemState.Visible);

    /// <summary>True when this row is a section that is off, and something sits under it -- so the
    /// row can say that its children went with it rather than leaving that to be discovered.</summary>
    private bool HasHiddenChildren(NavMenuCatalogItem item, NavMenuItemState state) =>
        state != NavMenuItemState.Visible && NavMenuCatalog.Items.Any(i => i.ParentKey == item.Key);

    private async Task SetStateAsync(NavMenuCatalogItem item, NavMenuItemState state)
    {
        await NavVisibilityWriter.SetStateAsync(item.Key, state);

        if (state == NavMenuItemState.Visible) _navVisibility.Remove(item.Key);
        else _navVisibility[item.Key] = state;

        await AuditLog.LogAsync(
            await CurrentActorAsync(),
            AuditAction.Update,
            "NavMenuItem", item.Key, EffectiveLabel(item),
            justification: $"Menu visibility set to {state}.");

        await NavState.NotifyChangedAsync();

        Toasts.ShowSuccess(state switch
        {
            NavMenuItemState.Visible => $"{EffectiveLabel(item)} is back on the menu.",
            NavMenuItemState.Hidden => $"{EffectiveLabel(item)} is hidden.",
            _ => $"{EffectiveLabel(item)} was removed from the menu.",
        });
    }

    private async Task ConfirmRemovalAsync()
    {
        if (_pendingRemoval is null) return;

        var item = _pendingRemoval;
        _pendingRemoval = null;
        await SetStateAsync(item, NavMenuItemState.Removed);
    }

    protected override async Task OnInitializedAsync() => await LoadAsync();

    private async Task LoadAsync()
    {
        // Its own short-lived context rather than the circuit-scoped ApplicationDbContext:
        // sharing that one lets this race, or outlive, whatever else in the circuit is using
        // it -- surfacing as "A second operation was started on this context instance" or
        // "Cannot access a disposed context instance". Same reason NavMenu and TopBar do it.
        await using var db = await DbFactory.CreateDbContextAsync();


        _navOrder = await db.NavMenuItemOrders.AsNoTracking().ToDictionaryAsync(o => o.ItemKey, o => o.SortOrder);
        _navLabels = await db.NavMenuItemLabels.AsNoTracking().ToDictionaryAsync(l => l.ItemKey, l => l.CustomLabel);

        // Text boxes start seeded with whatever's currently displayed (renamed or catalog default)
        // so the admin edits from what they actually see, not a blank field.
        _labelEdits = NavMenuCatalog.Items.ToDictionary(i => i.Key, i => EffectiveLabel(i));
        _navVisibility = await db.NavMenuItemVisibilities.AsNoTracking().ToDictionaryAsync(v => v.ItemKey, v => v.State);
    }

    private List<NavMenuCatalogItem> SortedGroupItems(OrderGroup group) => NavMenuCatalog.Sorted(group.ParentKey, _navOrder);

    private string EffectiveLabel(NavMenuCatalogItem item) => NavMenuCatalog.EffectiveLabel(item.Key, _navLabels);

    private bool IsRenamed(NavMenuCatalogItem item) => _navLabels.ContainsKey(item.Key);

    private async Task SaveLabelAsync(NavMenuCatalogItem item)
    {
        var newLabel = (_labelEdits.TryGetValue(item.Key, out var v) ? v : string.Empty).Trim();

        if (string.IsNullOrEmpty(newLabel) || newLabel == item.Label)
        {
            if (!IsRenamed(item)) return;
            await NavLabelWriter.ResetAsync(item.Key);
        }
        else
        {
            if (newLabel == EffectiveLabel(item)) return;
            await NavLabelWriter.UpsertAsync(item.Key, newLabel);
        }

        await AuditLog.LogAsync(await CurrentActorAsync(), AuditAction.Update, nameof(NavMenuItemLabel), null,
            $"Renamed nav menu item '{item.Label}' to '{newLabel}'");

        await LoadAsync();
        await NavState.NotifyChangedAsync();
        Toasts.ShowSuccess("Menu item renamed.");
    }

    private async Task ResetLabelAsync(NavMenuCatalogItem item)
    {
        if (!IsRenamed(item)) return;

        await NavLabelWriter.ResetAsync(item.Key);
        await AuditLog.LogAsync(await CurrentActorAsync(), AuditAction.Update, nameof(NavMenuItemLabel), null,
            $"Reset nav menu item '{item.Key}' back to its default label ('{item.Label}')");

        await LoadAsync();
        await NavState.NotifyChangedAsync();
        Toasts.ShowSuccess("Menu item reset to its default name.");
    }

    private async Task MoveAsync(OrderGroup group, NavMenuCatalogItem item, int direction)
    {
        var sorted = SortedGroupItems(group);
        var index = sorted.FindIndex(i => i.Key == item.Key);
        var swapIndex = index + direction;
        if (index < 0 || swapIndex < 0 || swapIndex >= sorted.Count) return;

        var a = sorted[index];
        var b = sorted[swapIndex];
        var aOrder = NavMenuCatalog.EffectiveOrder(a, _navOrder);
        var bOrder = NavMenuCatalog.EffectiveOrder(b, _navOrder);

        await NavOrderWriter.UpsertAsync(a.Key, bOrder);
        await NavOrderWriter.UpsertAsync(b.Key, aOrder);

        await AuditLog.LogAsync(await CurrentActorAsync(), AuditAction.Update, nameof(NavMenuItemOrder), null,
            $"Swapped nav menu order of '{a.Label}' and '{b.Label}' within {group.Title}");

        await LoadAsync();
        await NavState.NotifyChangedAsync();
        Toasts.ShowSuccess("Menu order updated.");
    }

    private async Task<string> CurrentActorAsync()
    {
        var state = await AuthState.GetAuthenticationStateAsync();
        return state.User.Identity?.Name ?? "unknown";
    }

    // "Replaced by" in the version history is meant for a compliance audience -- the login name
    // (often an email or AD account) isn't as readable as the person's actual name, so this resolves
    // it via the Member record linked to the current login, falling back to the login name for
    // logins with no linked Member (e.g. a bootstrap admin account).
}
