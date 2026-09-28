namespace CGTOOL.Web.Data.Governance;

/// <summary>How a transaction's status, action and stage read on screen. Its own class because four
/// screens plus two shared components show the same pills and the same words, and they had started
/// to drift -- each page carried a private copy.</summary>
public static class RpTransactionDisplay
{
    public static string StatusLabel(RpTransactionStatus status) => status switch
    {
        RpTransactionStatus.AwaitingApproval => "Awaiting Approval",
        RpTransactionStatus.Approved => "Approved",
        RpTransactionStatus.Rejected => "Rejected",
        RpTransactionStatus.Escalated => "Escalated",
        RpTransactionStatus.ReleasedToRegister => "Released to Register",
        RpTransactionStatus.Returned => "Returned to requestor",
        _ => status.ToString(),
    };

    public static string StatusPillClass(RpTransactionStatus status) => status switch
    {
        RpTransactionStatus.Approved or RpTransactionStatus.ReleasedToRegister => "cleared",
        RpTransactionStatus.Rejected => "flagged",
        RpTransactionStatus.Returned => "escalated",
        _ => "pending",
    };

    public static string ApproverActionLabel(RpApproverAction action) => action switch
    {
        RpApproverAction.Approve => "Approved",
        RpApproverAction.Reject => "Rejected",
        RpApproverAction.Escalate => "Escalated to CCAO",
        RpApproverAction.Return => "Returned to requestor",
        _ => action.ToString(),
    };

    public static string EscalationReasonLabel(RpEscalationReason reason) => reason switch
    {
        RpEscalationReason.ManualByApprover => "Escalated by the approver",
        RpEscalationReason.AutoTimeout30Days => "Escalated automatically — 30 days without a decision",
        RpEscalationReason.ApproverConflictOfInterest => "Escalated — the approver is conflicted",
        RpEscalationReason.OverApproverLimit => "Escalated — above the approver's limit",
        RpEscalationReason.FailedPreCheck => "Escalated — a pre-check failed",
        _ => string.Empty,
    };

    /// <summary>Who currently needs to act. Terminal statuses have nobody left.</summary>
    public static string PendingWith(RelatedPartyTransaction t) => t.Status switch
    {
        RpTransactionStatus.AwaitingApproval => t.ApproverMember?.FullName ?? "Approver",
        RpTransactionStatus.Returned => t.Member?.FullName ?? "Requestor",
        RpTransactionStatus.Approved or RpTransactionStatus.Escalated => "CCAO",
        _ => "—",
    };

    /// <summary>The reference as people quote it.</summary>
    public static string Reference(RelatedPartyTransaction t) => RelatedPartyTransaction.DisplayReference(t.Id);

    /// <summary>Days since the approver became responsible, and how many remain before the
    /// auto-escalation fires. Null once the transaction has left the approver.
    ///
    /// Counted from DateOfRequest because that is what RelatedPartyTransactionReminderHostedService
    /// counts from: a countdown on screen that disagreed with the job actually doing the escalating
    /// would be worse than none. It follows that a returned transaction keeps its original clock.</summary>
    public static (int Elapsed, int Remaining)? ApproverAge(RelatedPartyTransaction t, DateTime utcNow)
    {
        if (t.Status != RpTransactionStatus.AwaitingApproval) return null;

        var elapsed = (int)(utcNow.Date - t.DateOfRequest.Date).TotalDays;
        return (elapsed, Math.Max(0, RelatedPartyTransactionReminderHostedService.AutoEscalateAfterDays - elapsed));
    }

    public static string AgeTone(int remaining) => remaining switch
    {
        <= 0 => "critical",
        <= 7 => "critical",
        <= 14 => "warning",
        _ => "good",
    };
}
