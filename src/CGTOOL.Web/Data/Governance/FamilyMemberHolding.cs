using System.ComponentModel.DataAnnotations;

namespace CGTOOL.Web.Data.Governance;

/// <summary>One company a related party holds an interest in. A relative can hold several -- a
/// share of one company, all of another -- which is why this is a list rather than three columns on
/// FamilyMember, as it was.
///
/// It is also what the Related Party & COI declaration's "companies in which a relative owns 30% or
/// more" section is built from: with one holding per relative, a member with two qualifying
/// companies could only ever declare one of them.</summary>
public class FamilyMemberHolding
{
    public int Id { get; set; }

    public int FamilyMemberId { get; set; }
    public FamilyMember? FamilyMember { get; set; }

    [Required, MaxLength(160)]
    public string CompanyName { get; set; } = string.Empty;

    public RelatedPartyHoldingNature NatureOfHolding { get; set; } = RelatedPartyHoldingNature.None;

    /// <summary>Percentage of that company the relative holds (0-100).</summary>
    public decimal? OwnershipPercentage { get; set; }
}
