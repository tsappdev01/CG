using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CGTOOL.Web.Data.Governance;

/// <summary>The five canonical statuses per Related Party FRD §1.2 -- nothing else is ever persisted
/// here. "Cleared for Board/AC/GA Review" in the workflow diagram is not a distinct status: per §1.2
/// it's just the workflow label for the moment the CCAO's Form 3 decision sets status back to
/// Approved (see RpCcaoAction), whether the transaction arrived there via direct Approver approval or
/// via escalation.</summary>
public enum RpTransactionStatus
{
    AwaitingApproval,
    Approved,
    Rejected,
    Escalated,
    ReleasedToRegister,
}

/// <summary>Approver's decision on Form 2 (FRD §3.2.2). Escalate remains a manual option alongside the
/// two automatic escalation triggers (30-day timeout, Approver's own conflict of interest) -- see
/// RpEscalationReason.</summary>
public enum RpApproverAction
{
    Approve,
    Reject,
    Escalate,
}

/// <summary>CCAO's decision on Form 3 (FRD §3.2.3) -- a single consolidated approval representing that
/// all required offline governance review (MD&amp;CEO/Board/AC/GA, per §3.5) concluded satisfactorily,
/// regardless of whether the transaction reached CCAO via direct Approver approval or escalation.</summary>
public enum RpCcaoAction
{
    Approve,
    Reject,
}

public enum RpEscalationReason
{
    None,
    ManualByApprover,
    AutoTimeout30Days,
    ApproverConflictOfInterest,
}

/// <summary>One Related Party Transaction (FRD §3) -- User submits, Approver (Company.
/// ApprovingAuthorityMemberId) decides, CCAO confirms and releases to the Register. Display reference
/// is computed from Id ("RPT-00001") rather than stored, to avoid a two-phase insert.</summary>
public class RelatedPartyTransaction
{
    public int Id { get; set; }

    public static string DisplayReference(int id) => $"RPT-{id:D5}";

    /// <summary>"Entity" in the FRD -- auto-filled from the submitting Member's own Company.</summary>
    public int CompanyId { get; set; }
    public Company? Company { get; set; }

    /// <summary>"User" in the FRD -- the Member the transaction was submitted for (the declarant, not
    /// necessarily the logged-in account, when submitted by an approved delegate/impersonator).</summary>
    public int MemberId { get; set; }
    public Member? Member { get; set; }

    [Required, MaxLength(200)]
    public string CounterPartyName { get; set; } = string.Empty;

    [Column(TypeName = "decimal(18,2)")]
    public decimal TransactionValue { get; set; }

    [Required, MaxLength(4000)]
    public string Description { get; set; } = string.Empty;

    public DateTime DateOfRequest { get; set; } = DateTime.UtcNow;

    public RpTransactionStatus Status { get; set; } = RpTransactionStatus.AwaitingApproval;

    /// <summary>Snapshot of Company.ApprovingAuthorityMemberId at submission time -- kept stable even
    /// if the Company's designated Approver is reassigned later, so this transaction's history and
    /// pending queue stay consistent.</summary>
    public int? ApproverMemberId { get; set; }
    public Member? ApproverMember { get; set; }

    public RpApproverAction? ApproverAction { get; set; }
    [MaxLength(2000)] public string? ApproverRemarks { get; set; }
    public DateTime? ApproverActionAtUtc { get; set; }

    public RpEscalationReason EscalationReason { get; set; } = RpEscalationReason.None;

    /// <summary>Set the moment the transaction becomes Escalated (manually, by 30-day timeout, or by
    /// conflict) -- drives the separate "7 days since escalation, weekly thereafter" reminder to CCAO/
    /// Group CFO (FRD §3.3.3 item 4), independent of DateOfRequest.</summary>
    public DateTime? EscalatedAtUtc { get; set; }

    public RpCcaoAction? CcaoAction { get; set; }
    [MaxLength(2000)] public string? CcaoRemarks { get; set; }
    public DateTime? CcaoActionAtUtc { get; set; }

    /// <summary>Form 4 (FRD §3.2.4) -- ticked only once documentation for AC/Board/GM approvals is
    /// confirmed in place; gates the "Release to RP Register" button.</summary>
    public bool DocumentationConfirmed { get; set; }
    public DateTime? ReleasedAtUtc { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime ModifiedAtUtc { get; set; } = DateTime.UtcNow;

    /// <summary>Incremented on every amendment while still AwaitingApproval/Escalated -- each amendment
    /// re-notifies whoever currently holds the transaction (FRD §3.3, Note 2).</summary>
    public int AmendmentCount { get; set; }

    /// <summary>Last time a periodic (7-day/weekly) reminder was actually sent for whichever stage the
    /// transaction currently sits at -- lets the hourly background job avoid resending same-day.</summary>
    public DateTime? LastReminderSentUtc { get; set; }

    [Required, MaxLength(160)]
    public string SubmittedByName { get; set; } = string.Empty;

    /// <summary>Set only when submitted by an approved delegate/impersonator acting on the declarant's
    /// behalf -- same pattern as RelatedPartyCoiDeclaration.SubmittedOnBehalfOf (FRD Addendum 2 §5.1.3,
    /// reusing the existing MemberImpersonationApproval/ImpersonationContext mechanism).</summary>
    [MaxLength(160)]
    public string? SubmittedOnBehalfOf { get; set; }

    public List<RelatedPartyTransactionDocument> Documents { get; set; } = [];
}

/// <summary>Supporting document uploaded against an RP Transaction (e.g. draft agreement, board paper) --
/// same upload-then-link pattern as CoiTradeLicenseDocument: the file is written to disk immediately
/// when picked, and this row is only inserted once the parent transaction itself has been saved.</summary>
public class RelatedPartyTransactionDocument
{
    public int Id { get; set; }

    public int RelatedPartyTransactionId { get; set; }
    public RelatedPartyTransaction? RelatedPartyTransaction { get; set; }

    /// <summary>Path (under wwwroot/uploads) to the uploaded file.</summary>
    [Required, MaxLength(260)]
    public string FilePath { get; set; } = string.Empty;

    [Required, MaxLength(160)]
    public string FileName { get; set; } = string.Empty;

    public DateTime UploadedAtUtc { get; set; } = DateTime.UtcNow;
}
