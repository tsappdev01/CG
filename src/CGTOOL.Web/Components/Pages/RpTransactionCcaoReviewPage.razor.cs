using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using CGTOOL.Web.Data;
using CGTOOL.Web.Data.Governance;

namespace CGTOOL.Web.Components.Pages;

public partial class RpTransactionCcaoReviewPage
{
    private enum Step { Loading, NoAccess, Ready }

    private Step _step = Step.Loading;
    private string _activeTab = "review";

    private List<RelatedPartyTransaction> _awaitingDecision = [];
    private List<RelatedPartyTransaction> _readyToRelease = [];
    private Dictionary<int, string> _remarks = [];
    private Dictionary<int, bool> _documentationConfirmed = [];

    private List<string> _cfoEmails = [];

    protected override async Task OnInitializedAsync()
    {
        // Its own short-lived context rather than the circuit-scoped ApplicationDbContext:
        // sharing that one lets this race, or outlive, whatever else in the circuit is using
        // it -- which kills the circuit and takes the page with it.
        await using var db = await DbFactory.CreateDbContextAsync();

        var state = await AuthState.GetAuthenticationStateAsync();
        var userId = state.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var currentMember = userId is null ? null : await db.Members.FirstOrDefaultAsync(m => m.ApplicationUserId == userId);

        var isCcao = currentMember?.RpTransactionRole == RpTransactionRole.Ccao;
        var isAdmin = state.User.IsInRole(GovernanceRoles.Administrator);

        if (!isCcao && !isAdmin)
        {
            _step = Step.NoAccess;
            return;
        }

        _cfoEmails = await RpTransactionRoleResolver.GetRoleEmailsAsync(db, RpTransactionRole.Cfo);

        await LoadAsync();
        _step = Step.Ready;
    }

    private async Task LoadAsync()
    {
        // Its own short-lived context rather than the circuit-scoped ApplicationDbContext:
        // sharing that one lets this race, or outlive, whatever else in the circuit is using
        // it -- which kills the circuit and takes the page with it.
        await using var db = await DbFactory.CreateDbContextAsync();

        var query = db.RelatedPartyTransactions
            .Include(t => t.Member).ThenInclude(m => m!.Company)
            .Include(t => t.Company)
            .Include(t => t.ApproverMember)
            .AsQueryable();

        _awaitingDecision = await query
            .Where(t => (t.Status == RpTransactionStatus.Approved || t.Status == RpTransactionStatus.Escalated) && t.CcaoAction == null)
            .OrderBy(t => t.DateOfRequest)
            .ToListAsync();

        _readyToRelease = await query
            .Where(t => t.Status == RpTransactionStatus.Approved && t.CcaoAction == RpCcaoAction.Approve && t.ReleasedAtUtc == null)
            .OrderBy(t => t.CcaoActionAtUtc)
            .ToListAsync();

        _remarks = _awaitingDecision.ToDictionary(t => t.Id, _ => string.Empty);
        _documentationConfirmed = _readyToRelease.ToDictionary(t => t.Id, _ => false);
    }

    private void SwitchTab(string tab) => _activeTab = tab;

    private string RemarksFor(int id) => _remarks.TryGetValue(id, out var v) ? v : string.Empty;

    private async Task CcaoApproveAsync(RelatedPartyTransaction t)
    {
        var remarks = RemarksFor(t.Id).Trim();
        if (string.IsNullOrWhiteSpace(remarks))
        {
            Toasts.ShowError("Remarks are required before Approve/Reject.");
            return;
        }

        t.CcaoAction = RpCcaoAction.Approve;
        t.CcaoRemarks = remarks;
        t.CcaoActionAtUtc = DateTime.UtcNow;
        t.Status = RpTransactionStatus.Approved;

        await Writer.RecordCcaoActionAsync(t);
        await RpTransactionNotificationService.CcaoApprovedAsync(EmailSender, t, _cfoEmails, t.ApproverMember?.Email);

        await LogAndReloadAsync(t, "approved (Form 3)");
    }

    private async Task CcaoRejectAsync(RelatedPartyTransaction t)
    {
        var remarks = RemarksFor(t.Id).Trim();
        if (string.IsNullOrWhiteSpace(remarks))
        {
            Toasts.ShowError("Remarks are required before Approve/Reject.");
            return;
        }

        t.CcaoAction = RpCcaoAction.Reject;
        t.CcaoRemarks = remarks;
        t.CcaoActionAtUtc = DateTime.UtcNow;
        t.Status = RpTransactionStatus.Rejected;

        await Writer.RecordCcaoActionAsync(t);
        await RpTransactionNotificationService.CcaoRejectedAsync(EmailSender, t, _cfoEmails, t.ApproverMember?.Email);

        await LogAndReloadAsync(t, "rejected (Form 3)");
    }

    private async Task ReleaseAsync(RelatedPartyTransaction t)
    {
        if (!_documentationConfirmed.TryGetValue(t.Id, out var confirmed) || !confirmed)
        {
            Toasts.ShowError("Confirm that all documentation for AC/Board/GM approvals is in place before releasing.");
            return;
        }

        var releasedAt = DateTime.UtcNow;
        await Writer.ReleaseAsync(t.Id, releasedAt);
        t.DocumentationConfirmed = true;
        t.ReleasedAtUtc = releasedAt;
        t.Status = RpTransactionStatus.ReleasedToRegister;

        await RpTransactionNotificationService.ReleasedAsync(EmailSender, t, _cfoEmails, t.ApproverMember?.Email);

        await LogAndReloadAsync(t, "released to the RP Register");
    }

    private async Task LogAndReloadAsync(RelatedPartyTransaction t, string verb)
    {
        var state = await AuthState.GetAuthenticationStateAsync();
        await AuditLog.LogAsync(state.User.Identity?.Name ?? "unknown", AuditAction.Update, nameof(RelatedPartyTransaction), t.Id.ToString(),
            $"CCAO {verb} RP Transaction {RelatedPartyTransaction.DisplayReference(t.Id)}.");

        Toasts.ShowSuccess($"Transaction {RelatedPartyTransaction.DisplayReference(t.Id)} {verb}.");
        await LoadAsync();
    }
}
