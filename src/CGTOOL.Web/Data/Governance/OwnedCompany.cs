using System.ComponentModel.DataAnnotations;

namespace CGTOOL.Web.Data.Governance;

/// <summary>A member's own persistent "My Register &gt; My Companies" list -- reference data the member
/// maintains once (companies they or their family own/hold shares in), independent of any specific
/// declaration cycle.</summary>
public class OwnedCompany
{
    public int Id { get; set; }

    public int MemberId { get; set; }
    public Member? Member { get; set; }

    [Required, MaxLength(160)]
    public string CompanyName { get; set; } = string.Empty;

    [MaxLength(400)]
    public string? TradeLicenseDetails { get; set; }

    /// <summary>How the company is held -- the same question, and the same answers, as a related
    /// party's: a register that says "Subsidiary" for a relative's company and nothing for the
    /// member's own is answering half the question.</summary>
    public RelatedPartyHoldingNature NatureOfHolding { get; set; } = RelatedPartyHoldingNature.None;

    /// <summary>What the company actually does. Same field, same length, as the one the Related
    /// Party & COI declaration asks for against each company, so what is recorded here answers it.</summary>
    [MaxLength(400)]
    public string? PrincipalBusinessActivity { get; set; }

    /// <summary>What the interest actually is -- supplier, customer, directorship, and so on. Free
    /// text: it describes a relationship, where NatureOfHolding says how the shares are held.</summary>
    [MaxLength(400)]
    public string? NatureOfInterest { get; set; }

    /// <summary>Ownership percentage (0-100).</summary>
    public decimal? OwnershipPercentage { get; set; }

    /// <summary>Whether the member sits on this company's board or serves as a senior executive of
    /// it. Held on the register, like the DI shareholding flag on a relative: it is a standing fact
    /// about the member's relationship to the company, and it is what the Related Party & COI
    /// declaration's "board member or senior executive" section is asking for.</summary>
    public bool ServesAsBoardMemberOrExecutive { get; set; }

    [MaxLength(260)]
    public string? TradeLicensePath { get; set; }

    /// <summary>Read off the uploaded trade licence by Document Intelligence where it is configured,
    /// and editable either way -- see the same fields on FamilyMember.</summary>
    [MaxLength(100)]
    public string? TradeLicenceNumber { get; set; }

    [MaxLength(200)]
    public string? TradeLicenceLegalName { get; set; }

    public DateTime? TradeLicenceExpiryDate { get; set; }

    [MaxLength(260)]
    public string? MoaPath { get; set; }

    /// <summary>Power of Attorney -- optional, unlike the trade license/MOA.</summary>
    [MaxLength(260)]
    public string? PoaPath { get; set; }
}
