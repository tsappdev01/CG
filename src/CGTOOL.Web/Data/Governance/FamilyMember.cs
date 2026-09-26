using System.ComponentModel.DataAnnotations;

namespace CGTOOL.Web.Data.Governance;

/// <summary>A member's own persistent "My Workspace &gt; My Family" list -- reference data the member
/// maintains once, independent of any specific declaration cycle (unlike InsiderDeclarationRelative/
/// CoiRelative, which are answers captured against one particular submission).</summary>
/// <summary>How the organization is held. Replaced Direct/Indirect, which described the route to an
/// interest rather than what the interest is, and could not distinguish a subsidiary from a
/// minority affiliate -- the thing a reader of the register actually needs to know.</summary>
public enum RelatedPartyHoldingNature { None, Affiliate, Owned, Subsidiary }

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

    /// <summary>National Investor Number. Held per related party because the insider-trading
    /// declaration asks for the NIN of every relative who holds one, not only the declarant's.</summary>
    [MaxLength(60)]
    public string? NinNumber { get; set; }

    [MaxLength(80)]
    public string? Nationality { get; set; }

    [MaxLength(160)]
    public string? Occupation { get; set; }

    /// <summary>The company or organization this relative is connected to, if any. Held here rather
    /// than as an OwnedCompany because it describes the relative's own interest, not a company the
    /// member holds documents for.</summary>
    [MaxLength(160)]
    public string? Organization { get; set; }

    public RelatedPartyHoldingNature NatureOfHolding { get; set; } = RelatedPartyHoldingNature.None;

    /// <summary>Ownership percentage in the organization above (0-100).</summary>
    public decimal? OwnershipPercentage { get; set; }

    [MaxLength(260)]
    public string? EmiratesIdPath { get; set; }

    [MaxLength(260)]
    public string? PassportPath { get; set; }

    [MaxLength(260)]
    public string? TradeLicencePath { get; set; }

    /// <summary>Read off the uploaded trade licence by Document Intelligence where it is configured,
    /// and editable either way: the extraction is a best-effort pattern match over the OCR text, not
    /// an authoritative read, so the member has to be able to correct it.</summary>
    [MaxLength(100)]
    public string? TradeLicenceNumber { get; set; }

    [MaxLength(200)]
    public string? TradeLicenceLegalName { get; set; }

    public DateTime? TradeLicenceExpiryDate { get; set; }
}
