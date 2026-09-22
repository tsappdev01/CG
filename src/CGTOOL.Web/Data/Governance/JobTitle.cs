using System.ComponentModel.DataAnnotations;

namespace CGTOOL.Web.Data.Governance;

/// <summary>Master data: a standard job title/designation option, offered as a picklist wherever a
/// Member's title is captured. Kept separate from Member.JobTitle (still free text) so existing data
/// isn't force-migrated; this is the "Job Title" list itself (Setup &gt; Job Title).</summary>
public class JobTitle
{
    public int Id { get; set; }

    [Required, MaxLength(120)]
    public string Name { get; set; } = string.Empty;

    public bool Active { get; set; } = true;
}
