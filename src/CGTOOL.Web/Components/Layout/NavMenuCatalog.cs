using CGTOOL.Web.Data.Governance;

namespace CGTOOL.Web.Components.Layout;

/// <summary>One reorderable nav menu entry. Key is stable and persisted (NavMenuItemOrder.ItemKey) --
/// never rename an existing Key without a migration, since it'd silently orphan any admin-saved
/// order for that item. ParentKey null = a top-level group (My Account, Admin Panel, Declaration
/// Setup, Reports); Dashboard, Register/Login, and the profile block aren't in this catalog -- they're
/// not meaningful to reorder (pinned utility links, not content the admin curates).</summary>
public record NavMenuCatalogItem(string Key, string Label, string? ParentKey);

public static class NavMenuCatalog
{
    public static readonly NavMenuCatalogItem[] Items =
    [
        new("MyAccount", "My Account", null),
        new("AdminPanel", "Admin Panel", null),
        new("DeclarationSetup", "Declaration Setup", null),
        new("Reports", "Reports", null),

        new("MyAccount.Profile", "Profile", "MyAccount"),
        new("MyAccount.Workspace", "My Workspace", "MyAccount"),
        new("MyAccount.Declarations", "Declarations", "MyAccount"),

        new("AdminPanel.UserManagement", "User Management", "AdminPanel"),
        new("AdminPanel.Impersonate", "Impersonate", "AdminPanel"),
        new("AdminPanel.ClaimAdmin", "Claim Administrator Role", "AdminPanel"),
        new("AdminPanel.Company", "Company", "AdminPanel"),
        new("AdminPanel.Departments", "Departments", "AdminPanel"),
        new("AdminPanel.JobTitle", "Job Title", "AdminPanel"),
        new("AdminPanel.PolicyDocuments", "Policy Documents", "AdminPanel"),
        new("AdminPanel.Settings", "Settings", "AdminPanel"),
        new("AdminPanel.AuditLogs", "Audit Logs", "AdminPanel"),

        new("AdminPanel.AuditLogs.Daily", "Daily Logs", "AdminPanel.AuditLogs"),
        new("AdminPanel.AuditLogs.Detailed", "Detailed Log", "AdminPanel.AuditLogs"),
        new("AdminPanel.AuditLogs.Periodic", "Periodic Review by Date filter", "AdminPanel.AuditLogs"),

        // One entry for the four declarations, which now share a screen and pick between
        // themselves on it. Scheduled Maintenance keeps its own entry: it uses the same
        // machinery but is not a declaration.
        new("DeclarationSetup.Notifications", "Notifications", "DeclarationSetup"),
        new("DeclarationSetup.ScheduledMaintenance", "Scheduled Maintenance", "DeclarationSetup"),

        new("Reports.InsiderSubmission", "Insider Submission", "Reports"),
        new("Reports.RpCoiSubmission", "RP & COI Submission", "Reports"),
        new("Reports.RelatedPartyRegister", "Related Party Register", "Reports"),

        new("InvestorRelations", "Investor Relations", null),
        new("InvestorRelations.ShareRegister", "Share Register", "InvestorRelations"),
        new("InvestorRelations.ShareTrading", "Shares Trading", "InvestorRelations"),

        new("RpTransactions", "RP Transactions", null),
        new("RpTransactions.AddNew", "Add New Transaction", "RpTransactions"),
        new("RpTransactions.MyTransactions", "My Transactions", "RpTransactions"),
        new("RpTransactions.ApproverQueue", "Pending My Approval", "RpTransactions"),
        new("RpTransactions.CcaoReview", "CCAO Review", "RpTransactions"),
        new("RpTransactions.Register", "RP Transaction Register", "RpTransactions"),
    ];

    public static IReadOnlyList<NavMenuCatalogItem> ChildrenOf(string? parentKey) =>
        Items.Where(i => i.ParentKey == parentKey).ToList();

    /// <summary>Shared by NavMenu (rendering, via CSS order) and the Settings reorder UI so both
    /// compute the exact same position for any item, whether or not an admin has explicitly reordered
    /// it yet -- items with no saved row rank by catalog declaration order (spaced by 10, so an
    /// explicit reorder can never collide with an unset sibling's implicit position).</summary>
    public static int EffectiveOrder(NavMenuCatalogItem item, IReadOnlyDictionary<string, int> stored)
    {
        if (stored.TryGetValue(item.Key, out var v)) return v;
        var siblings = ChildrenOf(item.ParentKey);
        var index = siblings.ToList().FindIndex(i => i.Key == item.Key);
        return index * 10;
    }

    public static List<NavMenuCatalogItem> Sorted(string? parentKey, IReadOnlyDictionary<string, int> stored) =>
        ChildrenOf(parentKey)
            .Select(item => (item, order: EffectiveOrder(item, stored)))
            .OrderBy(x => x.order)
            .Select(x => x.item)
            .ToList();

    /// <summary>Shared by NavMenu (rendering) and the Settings rename UI so both show the exact same
    /// text for any item, whether or not an admin has renamed it yet.</summary>
    public static string EffectiveLabel(string key, IReadOnlyDictionary<string, string> labelOverrides)
    {
        if (labelOverrides.TryGetValue(key, out var custom)) return custom;
        var item = Items.FirstOrDefault(i => i.Key == key);
        return item?.Label ?? key;
    }

    /// <summary>
    /// Whether an item appears in the nav, and if not, why. An item is only shown when it and every
    /// ancestor are Visible -- a child of a hidden section has nowhere to appear, and its own
    /// setting is left untouched so unhiding the parent restores whatever each child was set to.
    /// Shared by NavMenu (rendering) and the Settings screen (editing) so both agree.
    /// </summary>
    public static NavMenuItemState EffectiveState(string key, IReadOnlyDictionary<string, NavMenuItemState> overrides)
    {
        var current = key;

        while (current is not null)
        {
            if (overrides.TryGetValue(current, out var state) && state != NavMenuItemState.Visible) return state;
            current = Items.FirstOrDefault(i => i.Key == current)?.ParentKey;
        }

        return NavMenuItemState.Visible;
    }
}
