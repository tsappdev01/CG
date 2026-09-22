using System.ComponentModel.DataAnnotations;

namespace CGTOOL.Web.Data.Governance;

// InLaws (4) is kept only so historical declarations that recorded a relative under the old,
// less specific "In-Laws" bucket still deserialize correctly -- new submissions use the more
// specific FatherInLaw/MotherInLaw/Stepchildren values added below per the Functional Spec's
// Relatives option set (Father, Mother, Brother, Sister, Children, Spouse, Father-in-Law,
// Mother-in-Law, Stepchildren). New members are appended rather than renumbering existing ones so
// stored integer values never shift under already-submitted declarations.
public enum RelativeRelationship { Mother, Father, Sister, Brother, InLaws, Children, Spouse, FatherInLaw, MotherInLaw, Stepchildren }

/// <summary>One member's Insider Trading declaration submitted in response to a specific
/// DeclarationCycleRun (FRD §7 "Business Process — Insider Trading Declaration"). Conflict of
/// Interest / Related Party Register submissions follow the same overall flow but capture different
/// fields, so this entity is intentionally Insider-Trading-specific rather than a generic shape.</summary>
public class InsiderDeclaration
{
    public int Id { get; set; }

    public int MemberId { get; set; }
    public Member? Member { get; set; }

    /// <summary>The notification cycle this declaration answers -- one submission per member per run.</summary>
    public int DeclarationCycleRunId { get; set; }
    public DeclarationCycleRun? DeclarationCycleRun { get; set; }

    /// <summary>Case 1: whether the declarant holds a National Investor Number.</summary>
    public bool HasNin { get; set; }
    [MaxLength(40)]
    public string? NinNumber { get; set; }

    /// <summary>Case 2: whether the declarant holds shares in DI.</summary>
    public bool HoldsShares { get; set; }
    [MaxLength(40)]
    public string? SharesNinNumber { get; set; }
    public int? NumberOfSharesHeld { get; set; }

    /// <summary>Case 3: whether any relatives hold shares in DI (detail in Relatives).</summary>
    public bool RelativesHoldShares { get; set; }

    /// <summary>Functional Spec §3.2: whether any relatives hold a National Investor Number
    /// (independent of Case 3 -- a relative can have a NIN without holding DI shares). Detail in
    /// NinHolders.</summary>
    public bool RelativesHaveNin { get; set; }

    /// <summary>True while the declarant has only saved their answers without submitting -- a draft
    /// is editable indefinitely (not gated by the cycle's due date) and does not count as a completed
    /// submission on the Insider Declaration Submission report.</summary>
    public bool IsDraft { get; set; }

    // Functional Spec §3.3 "Document Upload": Emirates ID and Passport are mandatory; Trade Licence
    // and Other Documents are optional. The spec calls for Azure AI Document Intelligence (OCR) to
    // auto-populate the "Captured Information" fields below from the uploaded file -- that isn't
    // wired up here (the source spec's OCR endpoint/key were a security finding: a live key embedded
    // in plain text, which must never be hard-coded or committed; doing so from this pass without a
    // properly Key-Vault-sourced credential would just reproduce the same finding). The declarant
    // enters these fields manually instead until OCR is wired up against a securely stored key.

    /// <summary>Path (under wwwroot/uploads) to the declarant's uploaded Emirates ID. Mandatory.</summary>
    [MaxLength(260)]
    public string? EmiratesIdPath { get; set; }
    [MaxLength(40)]
    public string? EmiratesIdNumber { get; set; }
    [MaxLength(120)]
    public string? EmiratesIdNameOnCard { get; set; }
    public DateTime? EmiratesIdExpiryDate { get; set; }

    /// <summary>Path (under wwwroot/uploads) to the declarant's uploaded passport. Mandatory.</summary>
    [MaxLength(260)]
    public string? PassportPath { get; set; }
    [MaxLength(40)]
    public string? PassportNumber { get; set; }
    public DateTime? PassportExpiryDate { get; set; }
    [MaxLength(80)]
    public string? PassportIssuingCountry { get; set; }

    /// <summary>Path (under wwwroot/uploads) to the declarant's uploaded trade licence, if provided. Optional.</summary>
    [MaxLength(260)]
    public string? TradeLicencePath { get; set; }
    [MaxLength(40)]
    public string? TradeLicenceNumber { get; set; }
    /// <summary>The licensed business/legal name on the trade licence.</summary>
    [MaxLength(160)]
    public string? TradeLicenceLegalName { get; set; }
    [MaxLength(160)]
    public string? TradeLicenceIssuingAuthority { get; set; }
    public DateTime? TradeLicenceExpiryDate { get; set; }

    /// <summary>Path (under wwwroot/uploads) to any other supporting document, if provided. Optional.</summary>
    [MaxLength(260)]
    public string? OtherDocumentPath { get; set; }

    public DateTime SubmittedAtUtc { get; set; } = DateTime.UtcNow;

    /// <summary>Set each time the declarant edits their answers after the initial submission --
    /// editing is allowed up to the cycle's due date, per FRD §7.4.</summary>
    public DateTime? ModifiedAtUtc { get; set; }

    /// <summary>The person who actually clicked submit -- the impersonator when submitted on
    /// behalf of another member, otherwise the declarant themselves. Updated on every edit to
    /// reflect the most recent editor.</summary>
    [Required, MaxLength(160)]
    public string SubmittedByName { get; set; } = string.Empty;

    /// <summary>Set only when submitted by an approved impersonator acting on the declarant's behalf.</summary>
    [MaxLength(160)]
    public string? SubmittedOnBehalfOf { get; set; }

    public List<InsiderDeclarationRelative> Relatives { get; set; } = [];

    /// <summary>Functional Spec §3.2 grid rows: relatives who hold a National Investor Number,
    /// independent of whether they hold DI shares.</summary>
    public List<InsiderDeclarationNinHolder> NinHolders { get; set; } = [];
}

/// <summary>One relative-holds-shares row under Case 3 of an InsiderDeclaration (Functional Spec §4
/// "Share Held By / Name of Share Holder / NIN / Additional" grid).</summary>
public class InsiderDeclarationRelative
{
    public int Id { get; set; }

    public int InsiderDeclarationId { get; set; }
    public InsiderDeclaration? InsiderDeclaration { get; set; }

    [MaxLength(40)]
    public string? NinNumber { get; set; }

    [Required, MaxLength(120)]
    public string RelativeName { get; set; } = string.Empty;

    public RelativeRelationship Relationship { get; set; }

    [Range(0, int.MaxValue)]
    public int NumberOfShares { get; set; }

    /// <summary>Functional Spec §3.2/§4 grid's optional free-text "Additional" column.</summary>
    [MaxLength(400)]
    public string? Additional { get; set; }

    /// <summary>True when this row is the declarant themselves rather than a relative -- Functional
    /// Spec §4 folds "Do you or your relatives hold shares" into one grid, so the declarant's own
    /// holding is captured as a row here (Relationship is meaningless when this is set) instead of
    /// via separate self-only fields.</summary>
    public bool IsSelf { get; set; }
}

/// <summary>One row of the Functional Spec §3.2 "Relatives' NIN" grid (Share Held By / Name of
/// Share Holder / NIN / Additional) -- distinct from InsiderDeclarationRelative, which records
/// relatives holding DI shares rather than relatives simply holding a NIN.</summary>
public class InsiderDeclarationNinHolder
{
    public int Id { get; set; }

    public int InsiderDeclarationId { get; set; }
    public InsiderDeclaration? InsiderDeclaration { get; set; }

    public RelativeRelationship Relationship { get; set; }

    [Required, MaxLength(120)]
    public string NameOfShareHolder { get; set; } = string.Empty;

    [Required, MaxLength(40)]
    public string NinNumber { get; set; } = string.Empty;

    [MaxLength(400)]
    public string? Additional { get; set; }
}
