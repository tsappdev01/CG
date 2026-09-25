using System.ComponentModel.DataAnnotations;

namespace CGTOOL.Web.Data.Governance;

/// <summary>
/// What an admin has done to a nav menu entry on the Settings screen. Visible is the absence of a
/// row, so an untouched menu carries no rows at all.
/// </summary>
public enum NavMenuItemState
{
    Visible = 0,

    /// <summary>Off for now. A quick toggle, expected to be flipped back.</summary>
    Hidden = 1,

    /// <summary>Taken out deliberately -- "we do not use this module". Still restorable, from the
    /// Removed items list, because the entry itself is defined in code and cannot truly be deleted.</summary>
    Removed = 2,
}

/// <summary>Admin override for whether a nav menu entry appears. Keyed by
/// <see cref="Components.Layout.NavMenuCatalogItem.Key"/>, like the label and order overrides.</summary>
public class NavMenuItemVisibility
{
    public int Id { get; set; }

    [Required, MaxLength(80)]
    public string ItemKey { get; set; } = string.Empty;

    public NavMenuItemState State { get; set; } = NavMenuItemState.Visible;
}
