using System.ComponentModel.DataAnnotations;

namespace CGTOOL.Web.Data.Governance;

/// <summary>An admin-renamed label for one nav menu item (see NavMenuCatalog). Only items an admin
/// has actually renamed get a row here -- everything else displays its catalog default label, so
/// this table starts empty and stays sparse. Kept separate from NavMenuItemOrder so renaming an
/// item can never accidentally touch its sort position (and vice versa).</summary>
public class NavMenuItemLabel
{
    public int Id { get; set; }

    [Required, MaxLength(80)]
    public string ItemKey { get; set; } = string.Empty;

    [Required, MaxLength(120)]
    public string CustomLabel { get; set; } = string.Empty;
}
