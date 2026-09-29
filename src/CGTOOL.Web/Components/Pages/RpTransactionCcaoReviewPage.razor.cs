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
    private string _search = string.Empty;
    private string _sort = "value";
    private int? _selectedId;
    private bool _acting;
    private bool _uploadingMinutes;
    private double? _minutesProgress;

    // The offline decision, held per transaction the way remarks already are, so switching between
    // queue items does not lose what has been typed into another one.
    private readonly Dictionary<int, RpGoverningBody?> _bodies = [];
    private readonly Dictionary<int, DateTime?> _decisionDates = [];
    private readonly Dictionary<int, string> _abstained = [];

    private RpGoverningBody? BodyFor(int id) => _bodies.TryGetValue(id, out var v) ? v : null;
    private DateTime? DecisionDateFor(int id) => _decisionDates.TryGetValue(id, out var v) ? v : null;
    private string AbstainedFor(int id) => _abstained.TryGetValue(id, out var v) ? v : string.Empty;

    private const long MaxMinutesBytes = 10 * 1024 * 1024;

    /// <summary>The minutes behind the decision, filed against the transaction as evidence rather
    /// than as one of the requestor's supporting papers -- which is what Kind distinguishes.</summary>
    private async Task UploadMinutesAsync(Microsoft.AspNetCore.Components.Forms.InputFileChangeEventArgs e, RelatedPartyTransaction t)
    {
        var file = e.File;
        var extension = file.ContentType switch
        {
            "image/png" => ".png",
            "image/jpeg" => ".jpg",
            "application/pdf" => ".pdf",
            _ => null,
        };
        if (extension is null)
        {
            Toasts.ShowError("Only JPEG, PNG or PDF files are supported.");
            return;
        }
        if (file.Size > MaxMinutesBytes)
        {
            Toasts.ShowError("File must be 10 MB or smaller.");
            return;
        }

        _uploadingMinutes = true;
        try
        {
            var directory = Path.Combine(Env.WebRootPath, "uploads", "rp-transactions");
            Directory.CreateDirectory(directory);

            var fileName = $"{Guid.NewGuid():N}{extension}";
            // Copied in chunks rather than one CopyToAsync so the bar can report a real percentage
            // as the file streams in over SignalR, which is what makes it a progress bar rather than
            // a decoration.
            await using (var source = file.OpenReadStream(MaxMinutesBytes))
            await using (var target = File.Create(Path.Combine(directory, fileName)))
            {
                var buffer = new byte[64 * 1024];
                long copied = 0;
                int read;
                while ((read = await source.ReadAsync(buffer)) > 0)
                {
                    await target.WriteAsync(buffer.AsMemory(0, read));
                    copied += read;
                    _minutesProgress = file.Size > 0 ? copied * 100d / file.Size : null;
                    StateHasChanged();
                }
            }

            var document = new RelatedPartyTransactionDocument
            {
                FilePath = $"/uploads/rp-transactions/{fileName}",
                FileName = file.Name,
                Kind = RpTransactionDocumentKind.GovernanceMinutes,
            };
            document.Id = await Writer.InsertDocumentAsync(t.Id, document);
            t.Documents.Add(document);

            Toasts.ShowSuccess("Minutes attached.");
        }
        finally
        {
            _uploadingMinutes = false;
            _minutesProgress = null;
        }
    }

    private List<RelatedPartyTransaction> Current => _activeTab == "review" ? _awaitingDecision : _readyToRelease;

    /// <summary>Why this transaction is in front of the CCAO at all -- approved by its approver, or
    /// escalated, and if escalated, by what. It changes what the CCAO is being asked to do, and the
    /// queue said nothing about it.</summary>
    private static string ArrivedBy(RelatedPartyTransaction t) =>
        RpTransactionDisplay.EscalationReasonLabel(t.EscalationReason) is { Length: > 0 } escalated
            ? escalated
            : t.ApproverMember?.FullName is { Length: > 0 } approver
                ? $"Approved by {approver}"
                : "Approved";

    private void Select(int id) => _selectedId = id;

    private RelatedPartyTransaction? Selected =>
        Queue().FirstOrDefault(t => t.Id == _selectedId) ?? Queue().FirstOrDefault();

    private List<RelatedPartyTransaction> Queue()
    {
        IEnumerable<RelatedPartyTransaction> query = Current;

        if (!string.IsNullOrWhiteSpace(_search))
        {
            var term = _search.Trim();
            query = query.Where(t =>
                RpTransactionDisplay.Reference(t).Contains(term, StringComparison.OrdinalIgnoreCase)
                || t.CounterPartyName.Contains(term, StringComparison.OrdinalIgnoreCase)
                || (t.Member?.FullName ?? string.Empty).Contains(term, StringComparison.OrdinalIgnoreCase));
        }

        query = _sort == "oldest"
            ? query.OrderBy(t => t.DateOfRequest)
            : query.OrderByDescending(t => t.TransactionValue);

        return query.ToList();
    }

    /// <summary>Switching tab must move the selection too: the detail pane would otherwise keep
    /// showing a transaction from the tab that is no longer open.</summary>
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
            .Include(t => t.Documents)
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

        // Anything already recorded is shown back rather than asked for again.
        foreach (var t in _awaitingDecision)
        {
            _bodies[t.Id] = t.GoverningBody;
            _decisionDates[t.Id] = t.GoverningBodyDecisionDate;
            _abstained[t.Id] = t.AbstainedMembers ?? string.Empty;
        }
        KeepSelectionInQueue();
    }

    private void SwitchTab(string tab)
    {
        _activeTab = tab;
        KeepSelectionInQueue();
    }

    private string RemarksFor(int id) => _remarks.TryGetValue(id, out var v) ? v : string.Empty;

    private async Task CcaoApproveAsync(RelatedPartyTransaction t)
    {
        if (_acting) return;
        var remarks = RemarksFor(t.Id).Trim();
        if (string.IsNullOrWhiteSpace(remarks))
        {
            Toasts.ShowError("Remarks are required before a decision.");
            return;
        }

        if (BodyFor(t.Id) is not { } body)
        {
            Toasts.ShowError("Say which body took the decision — an approval with no decision behind it is what this screen exists to stop.");
            return;
        }
        if (DecisionDateFor(t.Id) is not { } decidedOn)
        {
            Toasts.ShowError("Give the date of the meeting that decided it.");
            return;
        }

        _acting = true;
        t.CcaoAction = RpCcaoAction.Approve;
        t.CcaoRemarks = remarks;
        t.CcaoActionAtUtc = DateTime.UtcNow;
        t.Status = RpTransactionStatus.Approved;
        t.GoverningBody = body;
        t.GoverningBodyDecisionDate = decidedOn;
        t.AbstainedMembers = AbstainedFor(t.Id).Trim() is { Length: > 0 } abstained ? abstained : null;

        await Writer.RecordCcaoActionAsync(t);
        await Writer.RecordGovernanceDecisionAsync(t);
        await RpTransactionNotificationService.CcaoApprovedAsync(EmailSender, t, _cfoEmails, t.ApproverMember?.Email);

        await LogAndReloadAsync(t, "approved (Form 3)");
    }

    private async Task CcaoRejectAsync(RelatedPartyTransaction t)
    {
        if (_acting) return;
        var remarks = RemarksFor(t.Id).Trim();
        if (string.IsNullOrWhiteSpace(remarks))
        {
            Toasts.ShowError("Remarks are required before a decision.");
            return;
        }

        _acting = true;
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
        if (_acting) return;
        if (!_documentationConfirmed.TryGetValue(t.Id, out var confirmed) || !confirmed)
        {
            Toasts.ShowError("Confirm that all documentation for AC/Board/GM approvals is in place before releasing.");
            return;
        }

        _acting = true;
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
        _acting = false;
        await LoadAsync();
    }
}
