using System.ComponentModel.DataAnnotations;

namespace CGTOOL.Web.Data.Governance;

public class Company
{
    public int Id { get; set; }

    [Required, MaxLength(160)]
    public string Name { get; set; } = string.Empty;

    [Required, MaxLength(20)]
    public string ShortCode { get; set; } = string.Empty;

    [MaxLength(240)]
    public string? Address { get; set; }

    [MaxLength(80)]
    public string? City { get; set; }

    [MaxLength(80)]
    public string? Country { get; set; }

    /// <summary>One of the values configured under CompanyLookups:Sectors in appsettings.json.</summary>
    [MaxLength(80)]
    public string? Sector { get; set; }

    /// <summary>One of the values configured under CompanyLookups:GroupNames in appsettings.json.</summary>
    [MaxLength(80)]
    public string? GroupName { get; set; }

    /// <summary>The single User who holds authorized/approving authority for this entity. Required:
    /// an entity without one cannot take an RP Transaction through approval at all. Nullable rather
    /// than a NOT NULL column so entities created before the rule still load and can be corrected;
    /// the form is what refuses to save one empty.</summary>
    [Required(ErrorMessage = "Choose the user who holds approving authority for this entity.")]
    public int? ApprovingAuthorityMemberId { get; set; }
    public Member? ApprovingAuthorityMember { get; set; }

    /// <summary>The single User delegated authority for this entity -- who acts when the approving
    /// authority cannot. Required for the same reason, and on the same terms.</summary>
    [Required(ErrorMessage = "Choose the user who holds delegate authority for this entity.")]
    public int? DelegateAuthorityMemberId { get; set; }
    public Member? DelegateAuthorityMember { get; set; }

    /// <summary>Path (under wwwroot/uploads) to this company's logo, shown in place of its name in
    /// Entity columns/pickers once uploaded.</summary>
    [MaxLength(260)]
    public string? LogoPath { get; set; }

    public bool Active { get; set; } = true;

    public List<Member> Members { get; set; } = [];
}
