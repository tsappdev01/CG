using System.ComponentModel.DataAnnotations;

namespace CGTOOL.Web.Data.Governance;

public class Company : IValidatableObject
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

    /// <summary>Null until someone chooses: an entity on the register before this field existed has
    /// no type, and picking one for it would assert something nobody decided.</summary>
    public CompanyEntityType? EntityType { get; set; }

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
    public int? ApprovingAuthorityMemberId { get; set; }
    public Member? ApprovingAuthorityMember { get; set; }

    /// <summary>Set when the entity is marked as having no approving authority at all, rather than
    /// one nobody has chosen yet. Its own value because a null id alone cannot tell the two apart,
    /// and the form has to refuse the second while allowing the first.</summary>
    public bool ApprovingAuthorityNotApplicable { get; set; }

    /// <summary>The single User delegated authority for this entity -- who acts when the approving
    /// authority cannot. Required for the same reason, and on the same terms.</summary>
    public int? DelegateAuthorityMemberId { get; set; }
    public Member? DelegateAuthorityMember { get; set; }

    public bool DelegateAuthorityNotApplicable { get; set; }

    /// <summary>Path (under wwwroot/uploads) to this company's logo, shown in place of its name in
    /// Entity columns/pickers once uploaded.</summary>
    [MaxLength(260)]
    public string? LogoPath { get; set; }

    public bool Active { get; set; } = true;

    public List<Member> Members { get; set; } = [];

    /// <summary>Each authority needs an answer -- a person, or "not applicable" said out loud. A
    /// [Required] on the id alone could not express that, since the answer "nobody holds this" is
    /// also a null id.</summary>
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (ApprovingAuthorityMemberId is null && !ApprovingAuthorityNotApplicable)
        {
            yield return new ValidationResult(
                "Choose the user who holds approving authority for this entity, or select Not applicable.",
                [nameof(ApprovingAuthorityMemberId)]);
        }

        if (DelegateAuthorityMemberId is null && !DelegateAuthorityNotApplicable)
        {
            yield return new ValidationResult(
                "Choose the user who holds delegate authority for this entity, or select Not applicable.",
                [nameof(DelegateAuthorityMemberId)]);
        }
    }
}
