using System.ComponentModel.DataAnnotations;

namespace CGTOOL.Web.Data.Governance;

/// <summary>A member's own persistent "My Workspace &gt; My Family" list -- reference data the member
/// maintains once, independent of any specific declaration cycle (unlike InsiderDeclarationRelative/
/// CoiRelative, which are answers captured against one particular submission).</summary>
/// <summary>Whether the interest in the organization is held directly or through someone else.</summary>
public enum RelatedPartyInterestType { None, Direct, Indirect }

public class FamilyMember
{
    public int Id { get; set; }

    public int MemberId { get; set; }
    public Member? Member { get; set; }

    [Required, MaxLength(120)]
    public string Name { get; set; } = string.Empty;

    public RelativeRelationship Relationship { get; set; } = RelativeRelationship.Spouse;

    /// <summary>Emirates ID or passport number as printed. Free text rather than a validated
    /// Emirates ID: a relative may be non-resident and carry only a passport.</summary>
    [MaxLength(60)]
    public string? IdentificationNumber { get; set; }

    public DateOnly? DateOfBirth { get; set; }

    [MaxLength(80)]
    public string? Nationality { get; set; }

    [MaxLength(160)]
    public string? Occupation { get; set; }

    /// <summary>The company or organization this relative is connected to, if any. Held here rather
    /// than as an OwnedCompany because it describes the relative's own interest, not a company the
    /// member holds documents for.</summary>
    [MaxLength(160)]
    public string? Organization { get; set; }

    public RelatedPartyInterestType InterestType { get; set; } = RelatedPartyInterestType.None;

    /// <summary>Ownership percentage in the organization above (0-100).</summary>
    public decimal? OwnershipPercentage { get; set; }

    [MaxLength(260)]
    public string? EmiratesIdPath { get; set; }

    [MaxLength(260)]
    public string? PassportPath { get; set; }
}
