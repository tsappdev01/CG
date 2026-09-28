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

    /// <summary>Sent back to the requestor to amend and resubmit -- the "returned to user" outcome
    /// in the workflow (scripts/rp-transaction-workflow.md), which previously had to be done as a
    /// rejection and so lost the thread. Appended, not inserted: the values are stored as ints.</summary>
    Returned,
}

/// <summary>Approver's decision on Form 2 (FRD §3.2.2). Escalate remains a manual option alongside the
/// two automatic escalation triggers (30-day timeout, Approver's own conflict of interest) -- see
/// RpEscalationReason.</summary>
public enum RpApproverAction
{
    Approve,
    Reject,
    Escalate,

    /// <summary>Sent back for the requestor to fix and resubmit. Appended for the same reason as
    /// RpTransactionStatus.Returned.</summary>
    Return,
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

    /// <summary>The value put it in a band the approver cannot clear alone -- the workflow's
    /// "escalate: over limit", which had no way of happening before the DoA matrix existed.</summary>
    OverApproverLimit,

    /// <summary>A pre-check failed and the entity's matrix says that alone escalates.</summary>
    FailedPreCheck,
}

/// <summary>Which body took the offline decision the CCAO is recording.</summary>
public enum RpGoverningBody
{
    MdAndCeo,
    AuditCommittee,
    BoardOfDirectors,
    GeneralAssembly,
}

public static class RpGoverningBodies
{
    public static readonly (RpGoverningBody Value, string Label)[] All =
    [
        (RpGoverningBody.MdAndCeo, "MD & CEO"),
        (RpGoverningBody.AuditCommittee, "Audit Committee"),
        (RpGoverningBody.BoardOfDirectors, "Board of Directors"),
        (RpGoverningBody.GeneralAssembly, "General Assembly"),
    ];

    public static string Label(RpGoverningBody? value) =>
        value is null ? "—" : All.First(b => b.Value == value).Label;
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
    // ---------- the offline governance decision the CCAO records ----------
    // §3.2 section 3 is the CCAO writing down a decision taken elsewhere. Recording only that a
    // decision happened left out what an auditor asks for first: which body took it, when, and who
    // stood out of it.

    public RpGoverningBody? GoverningBody { get; set; }

    public DateTime? GoverningBodyDecisionDate { get; set; }

    /// <summary>Names of the members who abstained as conflicted, as the CCAO recorded them.
    /// Names rather than member ids because a board member need not be a user of this system, and a
    /// minute naming someone who was later deleted still named them.</summary>
    [MaxLength(1000)]
    public string? AbstainedMembers { get; set; }

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
/// <summary>What an attached file is. The minutes behind a Board decision are evidence of the
/// decision, not one of the requestor's supporting papers, and the register has to be able to say
/// which is which.</summary>
public enum RpTransactionDocumentKind
{
    Supporting,
    GovernanceMinutes,
}

public class RelatedPartyTransactionDocument
{
    public int Id { get; set; }

    public RpTransactionDocumentKind Kind { get; set; } = RpTransactionDocumentKind.Supporting;

    public int RelatedPartyTransactionId { get; set; }
    public RelatedPartyTransaction? RelatedPartyTransaction { get; set; }

    /// <summary>Path (under wwwroot/uploads) to the uploaded file.</summary>
    [Required, MaxLength(260)]
    public string FilePath { get; set; } = string.Empty;

    [Required, MaxLength(160)]
    public string FileName { get; set; } = string.Empty;

    public DateTime UploadedAtUtc { get; set; } = DateTime.UtcNow;
}
