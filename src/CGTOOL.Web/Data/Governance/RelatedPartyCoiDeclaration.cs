using System.ComponentModel.DataAnnotations;

namespace CGTOOL.Web.Data.Governance;

/// <summary>Which of the three company tables (Related Party & COI Declaration Functional Spec §4)
/// a CoiCompanyEntry belongs to. All three now require the same trade-license fields/documents.</summary>
public enum CoiCompanyOwnerType
{
    /// <summary>I.B — companies the declarant personally owns ≥30% of.</summary>
    Self,
    /// <summary>I.C — companies a declared relative owns ≥30% of.</summary>
    Relative,
    /// <summary>I.D — companies where the declarant serves as board member/senior executive.</summary>
    BoardOrExecutiveRole,
}

/// <summary>One member's Related Party &amp; Conflict of Interest declaration answering a specific
/// DeclarationCycleRun (Type=ConflictOfInterest) -- same linkage/versioning pattern as InsiderDeclaration,
/// so this reuses the same modern Declarations Setup (Quarter/Year, Send Now/Schedule Send, recipients,
/// reminders) that already existed for Conflict of Interest but had nothing writing real submissions
/// against it -- the member-facing side (MyDeclarations.razor) was still pointed at the older,
/// no-longer-administered DeclarationSetup/DeclarationSubmission pair.</summary>
public class RelatedPartyCoiDeclaration
{
    public int Id { get; set; }

    public int MemberId { get; set; }
    public Member? Member { get; set; }

    public int DeclarationCycleRunId { get; set; }
    public DeclarationCycleRun? DeclarationCycleRun { get; set; }

    // "Nothing to declare" toggles, one per sub-section (Functional Spec §8) -- resolves the ambiguity
    // between "left blank because nothing to declare" and "left blank because incomplete."
    public bool NothingToDeclareRelatives { get; set; }
    public bool NothingToDeclareSelfOwned { get; set; }
    public bool NothingToDeclareRelativeOwned { get; set; }
    public bool NothingToDeclareBoardRoles { get; set; }
    public bool NothingToDeclareConflicts { get; set; }

    /// <summary>True while only saved without final submission -- editable indefinitely, not gated by
    /// the cycle's due date, and doesn't count as a completed submission on the compliance report.</summary>
    public bool IsDraft { get; set; }

    [Required, MaxLength(160)]
    public string AttestationName { get; set; } = string.Empty;

    public bool AttestationConfirmed { get; set; }

    public DateTime SubmittedAtUtc { get; set; } = DateTime.UtcNow;

    /// <summary>Set each time the declarant edits their answers after the initial submission.</summary>
    public DateTime? ModifiedAtUtc { get; set; }

    [Required, MaxLength(160)]
    public string SubmittedByName { get; set; } = string.Empty;

    /// <summary>Set only when submitted by an approved impersonator acting on the declarant's behalf.</summary>
    [MaxLength(160)]
    public string? SubmittedOnBehalfOf { get; set; }

    /// <summary>Set once the declarant completes UAE PASS login (see IUaePassAuthService) -- final
    /// submission is blocked until this is populated, whenever UAE PASS is configured. Null for
    /// declarations submitted before this integration existed, or while UAE PASS isn't configured.</summary>
    public DateTime? UaePassVerifiedAtUtc { get; set; }

    [MaxLength(160)]
    public string? UaePassVerifiedName { get; set; }

    /// <summary>UAE PASS's stable per-person identifier ("uuid" claim from its UserInfo endpoint).</summary>
    [MaxLength(120)]
    public string? UaePassUuid { get; set; }

    public List<CoiRelative> Relatives { get; set; } = [];
    public List<CoiCompanyEntry> Companies { get; set; } = [];
    public List<CoiConflictEntry> Conflicts { get; set; } = [];
}

/// <summary>I.A "List of Relatives" grid row. Relationship reuses InsiderDeclaration's
/// RelativeRelationship enum -- both forms' Note 2 definitions of "Relative" are identical
/// (Father/Mother/Brother/Sister/Children/Spouse/Father-in-law/Mother-in-law/Children of spouse).</summary>
public class CoiRelative
{
    public int Id { get; set; }

    public int RelatedPartyCoiDeclarationId { get; set; }
    public RelatedPartyCoiDeclaration? RelatedPartyCoiDeclaration { get; set; }

    [Required, MaxLength(120)]
    public string Name { get; set; } = string.Empty;

    public RelativeRelationship Relationship { get; set; }
}

/// <summary>One row across I.B/I.C/I.D -- distinguished by OwnerType. All three now require a trade
/// license number, expiry date, license activities, and at least one uploaded document.</summary>
public class CoiCompanyEntry
{
    public int Id { get; set; }

    public int RelatedPartyCoiDeclarationId { get; set; }
    public RelatedPartyCoiDeclaration? RelatedPartyCoiDeclaration { get; set; }

    public CoiCompanyOwnerType OwnerType { get; set; }

    /// <summary>I.C only -- links the shareholding to a specific relative declared in I.A.</summary>
    public int? CoiRelativeId { get; set; }
    public CoiRelative? CoiRelative { get; set; }

    [Required, MaxLength(160)]
    public string LegalCompanyName { get; set; } = string.Empty;

    [MaxLength(400)]
    public string? PrincipalBusinessActivity { get; set; }

    [MaxLength(40)]
    public string? TradeLicenseNumber { get; set; }

    public DateTime? TradeLicenseExpiryDate { get; set; }

    [MaxLength(400)]
    public string? LicenseActivities { get; set; }

    public List<CoiTradeLicenseDocument> Documents { get; set; } = [];
}

/// <summary>One uploaded file for a CoiCompanyEntry -- multiple per row are supported (a license plus
/// amendments, or a license and a share certificate).</summary>
public class CoiTradeLicenseDocument
{
    public int Id { get; set; }

    public int CoiCompanyEntryId { get; set; }
    public CoiCompanyEntry? CoiCompanyEntry { get; set; }

    /// <summary>Path (under wwwroot/uploads) to the uploaded file.</summary>
    [Required, MaxLength(260)]
    public string FilePath { get; set; } = string.Empty;

    [Required, MaxLength(160)]
    public string FileName { get; set; } = string.Empty;

    public DateTime UploadedAtUtc { get; set; } = DateTime.UtcNow;
}

/// <summary>Part II "Conflict of Interest Declaration" grid row.</summary>
public class CoiConflictEntry
{
    public int Id { get; set; }

    public int RelatedPartyCoiDeclarationId { get; set; }
    public RelatedPartyCoiDeclaration? RelatedPartyCoiDeclaration { get; set; }

    [Required, MaxLength(160)]
    public string CompanyOrCounterpartyName { get; set; } = string.Empty;

    [MaxLength(400)]
    public string? PrincipalBusinessActivity { get; set; }

    /// <summary>One of the fixed values offered in the form's dropdown: Owned, Affiliate, Subsidiary.</summary>
    [MaxLength(1000)]
    public string? NatureOfHolding { get; set; }
}
