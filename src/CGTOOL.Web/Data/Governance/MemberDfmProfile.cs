using System.ComponentModel.DataAnnotations;

namespace CGTOOL.Web.Data.Governance;

/// <summary>How the member is registered with the DFM: Individual, UAE National, UAE Resident or
/// an international investor. The categories the exchange's own registration asks for.</summary>
public enum DfmInvestorType
{
    Individual,
    UaeNational,
    UaeResident,
    InternationalInvestor,
}

/// <summary>The member's investor record as the DFM holds it: their investor number, who the
/// exchange thinks they are, and how to reach them. One row per member.
///
/// Kept apart from Member, which is who they are to this company -- their department, their job
/// title, what they may see. This is who they are to the exchange, maintained by them, and the
/// two disagree perfectly legitimately: a name spelt as it appears on a trading account, an
/// address that is not the office.
///
/// It deliberately holds no Emirates ID or passport number, and no bank or tax details. The
/// identity documents are in My Documents, where they are read off the document itself rather than
/// typed twice; bank and tax details are not this application's business. Nor the address, the
/// account status or the dates the exchange keeps -- asked for once and taken back out, because
/// nothing here reads them.</summary>
public class MemberDfmProfile
{
    public int Id { get; set; }

    public int MemberId { get; set; }
    public Member? Member { get; set; }

    // ---- investor identification ----

    /// <summary>The DFM investor number, which is the NIN the declarations ask for -- the same
    /// ten characters, validated by the same rule, so a number that is good here is good there.</summary>
    [MaxLength(40)]
    public string? InvestorNumber { get; set; }

    public DfmInvestorType? InvestorType { get; set; }

    // ---- personal details ----

    /// <summary>As it appears on the trading account, which is not always how the directory spells
    /// it. Left to be typed rather than copied from Member for that reason.</summary>
    [MaxLength(200)]
    public string? FullName { get; set; }

    [MaxLength(80)]
    public string? Nationality { get; set; }

    // ---- contact details ----

    [MaxLength(40)]
    public string? MobileNumber { get; set; }

    [MaxLength(200)]
    public string? EmailAddress { get; set; }

    /// <summary>Nothing filled in yet. Used to decide whether the section opens with a prompt or a
    /// form, and to show "none on file" in the accordion count.</summary>
    public bool IsEmpty =>
        string.IsNullOrWhiteSpace(InvestorNumber)
        && InvestorType is null
        && string.IsNullOrWhiteSpace(FullName)
        && string.IsNullOrWhiteSpace(Nationality)
        && string.IsNullOrWhiteSpace(MobileNumber)
        && string.IsNullOrWhiteSpace(EmailAddress);
}
