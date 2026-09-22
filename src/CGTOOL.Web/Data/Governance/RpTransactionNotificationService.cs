namespace CGTOOL.Web.Data.Governance;

/// <summary>Sends the exact notification texts specified in Related Party FRD §3.3.2/§3.3.3 for each
/// workflow event. Every send is best-effort (silently skips a null/blank/empty recipient) --
/// IActivityEmailSender itself already degrades to logging-only when no mail sender is configured (see
/// Program.cs), matching the existing DeclarationReminderHostedService pattern. CCAO/CFO are now real
/// system roles (see GovernanceRoles/RpTransactionRoleResolver) that any number of logins can hold, so
/// every method takes a list of recipient emails for those two instead of a single designated person.</summary>
public static class RpTransactionNotificationService
{
    private static string Ref(RelatedPartyTransaction t) => RelatedPartyTransaction.DisplayReference(t.Id);

    private static string CommonBody(RelatedPartyTransaction t) =>
        $"Entity: {t.Company?.Name}\n" +
        $"Requestor: {t.Member?.FullName}\n" +
        $"Counter-party: {t.CounterPartyName}\n" +
        $"Transaction Value: {t.TransactionValue:N2}\n" +
        $"Description: {t.Description}\n" +
        $"Date of request: {t.DateOfRequest:dd/MM/yyyy}";

    private static Task SendAsync(IActivityEmailSender mail, string? toEmail, string subject, string text) =>
        string.IsNullOrWhiteSpace(toEmail) ? Task.CompletedTask : mail.SendAsync(toEmail, subject, text.Replace("\n", "<br/>"));

    private static async Task SendToAllAsync(IActivityEmailSender mail, IEnumerable<string>? toEmails, string subject, string text)
    {
        if (toEmails is null) return;
        foreach (var email in toEmails)
        {
            await SendAsync(mail, email, subject, text);
        }
    }

    /// <summary>Workflow ref 1b -- fired right after submission.</summary>
    public static async Task SubmittedAsync(IActivityEmailSender mail, RelatedPartyTransaction t, string? approverEmail)
    {
        var body = CommonBody(t);
        await SendAsync(mail, t.Member?.Email, $"{Ref(t)} — Awaiting Approval",
            $"Stated RP Transaction logged by you is currently Awaiting Approval. Transaction is not to be executed prior to obtaining approval.\n\n{body}");
        await SendAsync(mail, approverEmail, $"{Ref(t)} — Awaiting Your Approval",
            $"Stated RP transaction has been logged and is Awaiting Approval at your level.\n\n{body}");
    }

    /// <summary>Workflow ref 3b -- Approver rejected.</summary>
    public static Task RejectedByApproverAsync(IActivityEmailSender mail, RelatedPartyTransaction t) =>
        SendAsync(mail, t.Member?.Email, $"{Ref(t)} — Rejected",
            $"Stated RP transaction has been Rejected. User shall not execute.\n\n{CommonBody(t)}");

    /// <summary>Workflow ref 3d -- Approver approved directly (still pending CCAO/Board sign-off).</summary>
    public static async Task ApprovedByApproverAsync(IActivityEmailSender mail, RelatedPartyTransaction t, IEnumerable<string> ccaoEmails, IEnumerable<string> cfoEmails)
    {
        var body = CommonBody(t);
        await SendAsync(mail, t.Member?.Email, $"{Ref(t)} — Approved",
            $"Stated RP transaction has been Approved. User may go ahead with execution.\n\n{body}");
        await SendToAllAsync(mail, ccaoEmails, $"{Ref(t)} — Approved by Approver",
            $"Stated RP transaction has been Approved by Approver to be at arm's length, ensure necessary approvals for RP transactions are obtained in line with regulatory requirements.\n\n{body}");
        await SendToAllAsync(mail, cfoEmails, $"{Ref(t)} — Approved by Approver",
            $"Stated RP transaction has been Approved by Approver to be at arm's length, necessary approvals for RP transactions are pending to be obtained in line with regulatory requirements.\n\n{body}");
    }

    /// <summary>Workflow ref 3g / §3.3.3 -- Escalated, whether manually by the Approver, by 30-day
    /// timeout, or by the Approver's own conflict of interest. Wording varies slightly by reason.</summary>
    public static async Task EscalatedAsync(IActivityEmailSender mail, RelatedPartyTransaction t, string? approverEmail, IEnumerable<string> ccaoEmails, IEnumerable<string> cfoEmails, RpEscalationReason reason)
    {
        var body = CommonBody(t);

        await SendAsync(mail, t.Member?.Email, $"{Ref(t)} — Escalated",
            $"Stated RP transaction is currently awaiting approval and has been Escalated for further review. Transaction is not to be executed prior to obtaining approval.\n\n{body}");

        (string approverText, string ccaoText, string cfoText) = reason switch
        {
            RpEscalationReason.AutoTimeout30Days =>
                ("Auto-escalation: Stated RP transaction has been auto-escalated for review and further action by the CCAO and CFO on the expiry of 30 days since date of submission of RP Transaction by user.",
                 "Auto-escalation: Stated RP transaction has been auto-escalated for review and further action at your level on the expiry of 30 days since date of submission of RP Transaction by user.",
                 "Auto-escalation: Stated RP transaction has been auto-escalated for review and further action by the CCAO's office on the expiry of 30 days since date of submission of RP Transaction by user."),
            RpEscalationReason.ApproverConflictOfInterest =>
                ("Auto-escalation: Stated RP transaction has been auto-escalated for review and further action by the CCAO and CFO as a result of a conflict of interest at your level.",
                 "Auto-escalation: Stated RP transaction has been auto-escalated for review and further action at your level as a result of a conflict of interest at Approver's level.",
                 "Auto-escalation: Stated RP transaction has been auto-escalated for review and further action by the CCAO's office as a result of a conflict of interest at Approver's level."),
            _ =>
                ("Stated RP Transaction has been Escalated by you for review and approval by the CCAO's office.",
                 "Stated RP Transaction has been Escalated by Approver for review and approval by your office.",
                 "Stated RP Transaction has been Escalated by Approver for review and approval by the CCAO's office."),
        };

        await SendAsync(mail, approverEmail, $"{Ref(t)} — Escalated", $"{approverText}\n\n{body}");
        await SendToAllAsync(mail, ccaoEmails, $"{Ref(t)} — Escalated", $"{ccaoText}\n\n{body}");
        await SendToAllAsync(mail, cfoEmails, $"{Ref(t)} — Escalated", $"{cfoText}\n\n{body}");
    }

    /// <summary>Workflow ref 9b -- CCAO's Form 3 Approve (whether the transaction arrived via direct
    /// Approver approval or escalation).</summary>
    public static async Task CcaoApprovedAsync(IActivityEmailSender mail, RelatedPartyTransaction t, IEnumerable<string> cfoEmails, string? approverEmail)
    {
        var body = CommonBody(t);
        await SendAsync(mail, t.Member?.Email, $"{Ref(t)} — Approved",
            $"Stated RP transaction has been Approved. User may go ahead with execution.\n\n{body}");
        await SendToAllAsync(mail, cfoEmails, $"{Ref(t)} — Approved",
            $"Stated RP transaction has been Approved and notified to the stated user.\n\n{body}");
        await SendAsync(mail, approverEmail, $"{Ref(t)} — Approved",
            $"Stated RP transaction has been Approved and notified to the stated user.\n\n{body}");
    }

    /// <summary>Workflow ref 9d -- CCAO's Form 3 Reject.</summary>
    public static async Task CcaoRejectedAsync(IActivityEmailSender mail, RelatedPartyTransaction t, IEnumerable<string> cfoEmails, string? approverEmail)
    {
        var body = CommonBody(t);
        await SendAsync(mail, t.Member?.Email, $"{Ref(t)} — Rejected",
            $"Stated RP transaction has been Rejected. User shall not execute.\n\n{body}");
        await SendToAllAsync(mail, cfoEmails, $"{Ref(t)} — Rejected",
            $"Stated RP transaction has been Rejected and notified to the stated user.\n\n{body}");
        await SendAsync(mail, approverEmail, $"{Ref(t)} — Rejected",
            $"Stated RP transaction has been Rejected and notified to the stated user.\n\n{body}");
    }

    /// <summary>Workflow ref 13 -- released to the RP Register.</summary>
    public static async Task ReleasedAsync(IActivityEmailSender mail, RelatedPartyTransaction t, IEnumerable<string> cfoEmails, string? approverEmail)
    {
        const string text = "Approvals in line with regulatory requirements have been obtained for the above RP transaction and all documentation made available. Accordingly, the stated RP transaction has been entered into the RP Register.";
        var body = CommonBody(t);
        await SendAsync(mail, t.Member?.Email, $"{Ref(t)} — Released to RP Register", $"{text}\n\n{body}");
        await SendToAllAsync(mail, cfoEmails, $"{Ref(t)} — Released to RP Register", $"{text}\n\n{body}");
        await SendAsync(mail, approverEmail, $"{Ref(t)} — Released to RP Register", $"{text}\n\n{body}");
    }

    /// <summary>§3.3.3 item 2(i) -- 7-day/weekly reminder to the Approver while AwaitingApproval.</summary>
    public static Task ReminderApproverAsync(IActivityEmailSender mail, RelatedPartyTransaction t, string? approverEmail) =>
        SendAsync(mail, approverEmail, $"{Ref(t)} — Reminder",
            $"Reminder: Stated RP transaction has been logged and is awaiting review and further action at your level.\n\n{CommonBody(t)}");

    /// <summary>§3.3.3 item 4 -- 7-day/weekly reminder to CCAO/CFO once Escalated.</summary>
    public static async Task ReminderCcaoAsync(IActivityEmailSender mail, RelatedPartyTransaction t, IEnumerable<string> ccaoEmails, IEnumerable<string> cfoEmails)
    {
        var body = CommonBody(t);
        await SendToAllAsync(mail, ccaoEmails, $"{Ref(t)} — Reminder",
            $"Reminder: Stated RP transaction has been logged and is awaiting review and further action at your level.\n\n{body}");
        await SendToAllAsync(mail, cfoEmails, $"{Ref(t)} — Reminder",
            $"Reminder: Stated RP transaction has been logged and is awaiting review and further action by CCAO's office.\n\n{body}");
    }
}
