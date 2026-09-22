using System.ComponentModel.DataAnnotations;

namespace CGTOOL.Web.Data.Governance;

/// <summary>An admin-customized position for one nav menu item (see NavMenuCatalog). Only items an
/// admin has actually reordered get a row here -- everything else ranks by its catalog declaration
/// order, so this table starts empty and stays sparse.</summary>
public class NavMenuItemOrder
{
    public int Id { get; set; }

    [Required, MaxLength(80)]
    public string ItemKey { get; set; } = string.Empty;

    public int SortOrder { get; set; }
}
