using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using CGTOOL.Web.Data;
using CGTOOL.Web.Data.Governance;

namespace CGTOOL.Web.Components.Pages;

public partial class RpTransactionApprovalsPage
{
    private enum Step { Loading, NoAccess, Ready }

    private Step _step = Step.Loading;
    private Member? _currentMember;
    private List<RelatedPartyTransaction> _pending = [];
    private Dictionary<int, string> _remarks = [];

    protected override async Task OnInitializedAsync()
    {
        var state = await AuthState.GetAuthenticationStateAsync();
        var userId = state.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        _currentMember = userId is null ? null : await Db.Members.FirstOrDefaultAsync(m => m.ApplicationUserId == userId);

        if (_currentMember is null)
        {
            _step = Step.NoAccess;
            return;
        }

        await LoadAsync();
        _step = Step.Ready;
    }

    private async Task LoadAsync()
    {
        _pending = await Db.RelatedPartyTransactions
            .Include(t => t.Member).ThenInclude(m => m!.Company)
            .Include(t => t.Company)
            .Where(t => t.Status == RpTransactionStatus.AwaitingApproval && t.ApproverMemberId == _currentMember!.Id)
            .OrderBy(t => t.DateOfRequest)
            .ToListAsync();

        _remarks = _pending.ToDictionary(t => t.Id, _ => string.Empty);
    }

    private string RemarksFor(int id) => _remarks.TryGetValue(id, out var v) ? v : string.Empty;

    private async Task ApproveAsync(RelatedPartyTransaction t)
    {
        var remarks = RemarksFor(t.Id).Trim();
        if (string.IsNullOrWhiteSpace(remarks))
        {
            Toasts.ShowError("Remarks are required before Approve/Escalate/Reject.");
            return;
        }

        t.ApproverAction = RpApproverAction.Approve;
        t.ApproverRemarks = remarks;
        t.ApproverActionAtUtc = DateTime.UtcNow;
        t.Status = RpTransactionStatus.Approved;

        await Writer.RecordApproverActionAsync(t);

        var ccaoEmails = await RpTransactionRoleResolver.GetRoleEmailsAsync(Db, RpTransactionRole.Ccao);
        var cfoEmails = await RpTransactionRoleResolver.GetRoleEmailsAsync(Db, RpTransactionRole.Cfo);
        await RpTransactionNotificationService.ApprovedByApproverAsync(EmailSender, t, ccaoEmails, cfoEmails);

        await LogAndReloadAsync(t, "approved");
    }

    private async Task RejectAsync(RelatedPartyTransaction t)
    {
        var remarks = RemarksFor(t.Id).Trim();
        if (string.IsNullOrWhiteSpace(remarks))
        {
            Toasts.ShowError("Remarks are required before Approve/Escalate/Reject.");
            return;
        }

        t.ApproverAction = RpApproverAction.Reject;
        t.ApproverRemarks = remarks;
        t.ApproverActionAtUtc = DateTime.UtcNow;
        t.Status = RpTransactionStatus.Rejected;

        await Writer.RecordApproverActionAsync(t);
        await RpTransactionNotificationService.RejectedByApproverAsync(EmailSender, t);

        await LogAndReloadAsync(t, "rejected");
    }

    private async Task EscalateAsync(RelatedPartyTransaction t)
    {
        var remarks = RemarksFor(t.Id).Trim();
        if (string.IsNullOrWhiteSpace(remarks))
        {
            Toasts.ShowError("Remarks are required before Approve/Escalate/Reject.");
            return;
        }

        t.ApproverAction = RpApproverAction.Escalate;
        t.ApproverRemarks = remarks;
        t.ApproverActionAtUtc = DateTime.UtcNow;
        t.Status = RpTransactionStatus.Escalated;
        t.EscalationReason = RpEscalationReason.ManualByApprover;
        t.EscalatedAtUtc = DateTime.UtcNow;

        await Writer.RecordApproverActionAsync(t);

        var ccaoEmails = await RpTransactionRoleResolver.GetRoleEmailsAsync(Db, RpTransactionRole.Ccao);
        var cfoEmails = await RpTransactionRoleResolver.GetRoleEmailsAsync(Db, RpTransactionRole.Cfo);
        await RpTransactionNotificationService.EscalatedAsync(EmailSender, t, _currentMember!.Email, ccaoEmails, cfoEmails, RpEscalationReason.ManualByApprover);

        await LogAndReloadAsync(t, "escalated");
    }

    private async Task LogAndReloadAsync(RelatedPartyTransaction t, string verb)
    {
        var state = await AuthState.GetAuthenticationStateAsync();
        await AuditLog.LogAsync(state.User.Identity?.Name ?? "unknown", AuditAction.Update, nameof(RelatedPartyTransaction), t.Id.ToString(),
            $"Approver {verb} RP Transaction {RelatedPartyTransaction.DisplayReference(t.Id)}.");

        Toasts.ShowSuccess($"Transaction {RelatedPartyTransaction.DisplayReference(t.Id)} {verb}.");
        await LoadAsync();
    }
}
