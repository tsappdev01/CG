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
    private string _search = string.Empty;
    private string _sort = "oldest";
    private string _filter = string.Empty;
    private int? _selectedId;
    private bool _acting;

    /// <summary>The counts across the top, which are also the filters.</summary>
    public static readonly (string Key, string Label)[] Tiles =
    [
        ("", "Awaiting me"),
        ("escalating", "Auto-escalates ≤ 7 days"),
        ("amended", "Amended since raised"),
        ("large", "AED 1m and over"),
    ];

    private static string? ToneOf(string key) => key switch
    {
        "escalating" => "critical",
        "amended" => "warning",
        _ => null,
    };

    private int CountOf(string key) => _pending.Count(t => Matches(t, key));

    private bool Matches(RelatedPartyTransaction t, string key) => key switch
    {
        "escalating" => RpTransactionDisplay.ApproverAge(t, DateTime.UtcNow) is { Remaining: <= 7 },
        "amended" => t.AmendmentCount > 0,
        "large" => t.TransactionValue >= 1_000_000m,
        _ => true,
    };

    /// <summary>Clicking the tile already filtered on clears it, so the tiles toggle rather than
    /// being a one-way trip into a filter with no visible way out.</summary>
    private void ToggleFilter(string key)
    {
        _filter = _filter == key ? string.Empty : key;
        KeepSelectionInQueue();
    }

    private void Select(int id) => _selectedId = id;

    private RelatedPartyTransaction? Selected =>
        Queue().FirstOrDefault(t => t.Id == _selectedId) ?? Queue().FirstOrDefault();

    private List<RelatedPartyTransaction> Queue()
    {
        IEnumerable<RelatedPartyTransaction> query = _pending.Where(t => Matches(t, _filter));

        if (!string.IsNullOrWhiteSpace(_search))
        {
            var term = _search.Trim();
            query = query.Where(t =>
                RpTransactionDisplay.Reference(t).Contains(term, StringComparison.OrdinalIgnoreCase)
                || t.CounterPartyName.Contains(term, StringComparison.OrdinalIgnoreCase)
                || (t.Member?.FullName ?? string.Empty).Contains(term, StringComparison.OrdinalIgnoreCase));
        }

        query = _sort == "value"
            ? query.OrderByDescending(t => t.TransactionValue)
            : query.OrderBy(t => t.DateOfRequest);

        return query.ToList();
    }

    /// <summary>A filter that hides the selected transaction leaves the detail pane showing something
    /// the queue no longer lists, which reads as a bug. Fall back to the top of the new queue.</summary>
    private void KeepSelectionInQueue()
    {
        if (_selectedId is { } id && Queue().Any(t => t.Id == id)) return;
        _selectedId = Queue().FirstOrDefault()?.Id;
    }

    protected override async Task OnInitializedAsync()
    {
        // Its own short-lived context rather than the circuit-scoped ApplicationDbContext:
        // sharing that one lets this race, or outlive, whatever else in the circuit is using
        // it -- which kills the circuit and takes the page with it.
        await using var db = await DbFactory.CreateDbContextAsync();

        var state = await AuthState.GetAuthenticationStateAsync();
        var userId = state.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        _currentMember = userId is null ? null : await db.Members.FirstOrDefaultAsync(m => m.ApplicationUserId == userId);

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
        // Its own short-lived context rather than the circuit-scoped ApplicationDbContext:
        // sharing that one lets this race, or outlive, whatever else in the circuit is using
        // it -- which kills the circuit and takes the page with it.
        await using var db = await DbFactory.CreateDbContextAsync();

        _pending = await db.RelatedPartyTransactions
            .Include(t => t.Member).ThenInclude(m => m!.Company)
            .Include(t => t.Company)
            .Include(t => t.Documents)
            .Where(t => t.Status == RpTransactionStatus.AwaitingApproval && t.ApproverMemberId == _currentMember!.Id)
            .OrderBy(t => t.DateOfRequest)
            .ToListAsync();

        _remarks = _pending.ToDictionary(t => t.Id, _ => string.Empty);
        KeepSelectionInQueue();
    }

    private string RemarksFor(int id) => _remarks.TryGetValue(id, out var v) ? v : string.Empty;

    private async Task ApproveAsync(RelatedPartyTransaction t)
    {
        // Its own short-lived context rather than the circuit-scoped ApplicationDbContext:
        // sharing that one lets this race, or outlive, whatever else in the circuit is using
        // it -- which kills the circuit and takes the page with it.
        await using var db = await DbFactory.CreateDbContextAsync();

        var remarks = RemarksFor(t.Id).Trim();
        if (string.IsNullOrWhiteSpace(remarks))
        {
            Toasts.ShowError("Remarks are required before any decision.");
            return;
        }

        _acting = true;
        t.ApproverAction = RpApproverAction.Approve;
        t.ApproverRemarks = remarks;
        t.ApproverActionAtUtc = DateTime.UtcNow;
        t.Status = RpTransactionStatus.Approved;

        await Writer.RecordApproverActionAsync(t);

        var ccaoEmails = await RpTransactionRoleResolver.GetRoleEmailsAsync(db, RpTransactionRole.Ccao);
        var cfoEmails = await RpTransactionRoleResolver.GetRoleEmailsAsync(db, RpTransactionRole.Cfo);
        await RpTransactionNotificationService.ApprovedByApproverAsync(EmailSender, t, ccaoEmails, cfoEmails);

        await LogAndReloadAsync(t, "approved");
    }

    private async Task RejectAsync(RelatedPartyTransaction t)
    {
        var remarks = RemarksFor(t.Id).Trim();
        if (string.IsNullOrWhiteSpace(remarks))
        {
            Toasts.ShowError("Remarks are required before any decision.");
            return;
        }

        _acting = true;
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
        // Its own short-lived context rather than the circuit-scoped ApplicationDbContext:
        // sharing that one lets this race, or outlive, whatever else in the circuit is using
        // it -- which kills the circuit and takes the page with it.
        await using var db = await DbFactory.CreateDbContextAsync();

        var remarks = RemarksFor(t.Id).Trim();
        if (string.IsNullOrWhiteSpace(remarks))
        {
            Toasts.ShowError("Remarks are required before any decision.");
            return;
        }

        _acting = true;
        t.ApproverAction = RpApproverAction.Escalate;
        t.ApproverRemarks = remarks;
        t.ApproverActionAtUtc = DateTime.UtcNow;
        t.Status = RpTransactionStatus.Escalated;
        t.EscalationReason = RpEscalationReason.ManualByApprover;
        t.EscalatedAtUtc = DateTime.UtcNow;

        await Writer.RecordApproverActionAsync(t);

        var ccaoEmails = await RpTransactionRoleResolver.GetRoleEmailsAsync(db, RpTransactionRole.Ccao);
        var cfoEmails = await RpTransactionRoleResolver.GetRoleEmailsAsync(db, RpTransactionRole.Cfo);
        await RpTransactionNotificationService.EscalatedAsync(EmailSender, t, _currentMember!.Email, ccaoEmails, cfoEmails, RpEscalationReason.ManualByApprover);

        await LogAndReloadAsync(t, "escalated");
    }

    /// <summary>Sends it back for the requestor to fix, which the workflow has always had as an
    /// outcome ("returned to user -- amend and resubmit") and the screen has never offered. Doing it
    /// as a rejection instead ended the transaction and lost the thread.</summary>
    private async Task ReturnAsync(RelatedPartyTransaction t)
    {
        var remarks = RemarksFor(t.Id).Trim();
        if (string.IsNullOrWhiteSpace(remarks))
        {
            Toasts.ShowError("Say what needs changing before returning it — that is the whole message the requestor gets.");
            return;
        }

        _acting = true;
        t.ApproverAction = RpApproverAction.Return;
        t.ApproverRemarks = remarks;
        t.ApproverActionAtUtc = DateTime.UtcNow;
        t.Status = RpTransactionStatus.Returned;

        await Writer.RecordApproverActionAsync(t);
        await RpTransactionNotificationService.ReturnedByApproverAsync(EmailSender, t);

        await LogAndReloadAsync(t, "returned to the requestor");
    }

    private async Task LogAndReloadAsync(RelatedPartyTransaction t, string verb)
    {
        var state = await AuthState.GetAuthenticationStateAsync();
        await AuditLog.LogAsync(state.User.Identity?.Name ?? "unknown", AuditAction.Update, nameof(RelatedPartyTransaction), t.Id.ToString(),
            $"Approver {verb} RP Transaction {RelatedPartyTransaction.DisplayReference(t.Id)}.");

        Toasts.ShowSuccess($"Transaction {RelatedPartyTransaction.DisplayReference(t.Id)} {verb}.");
        _acting = false;
        await LoadAsync();
    }
}
