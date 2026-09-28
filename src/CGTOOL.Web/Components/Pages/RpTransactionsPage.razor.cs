using System.Security.Claims;
using Microsoft.AspNetCore.Components;
using Microsoft.EntityFrameworkCore;
using CGTOOL.Web.Data;
using CGTOOL.Web.Data.Governance;

namespace CGTOOL.Web.Components.Pages;

public partial class RpTransactionsPage
{
    private enum Step { Loading, NoAccess, Ready }

    private Step _step = Step.Loading;
    private Member? _effectiveMember;
    private string _activeTab = "new";

    private List<string> _counterPartyNames = [];
    private List<RelatedPartyTransaction> _myTransactions = [];

    private string _counterPartyName = string.Empty;
    private decimal? _transactionValue;
    private string _description = string.Empty;
    private bool _submitting;

    private const long MaxDocumentFileBytes = 10 * 1024 * 1024;
    private readonly List<RelatedPartyTransactionDocument> _pendingDocuments = [];
    private bool _uploadingDocument;
    private int _uploadProgress;

    private RelatedPartyTransaction? _amendingRow;
    private string _amendCounterPartyName = string.Empty;
    private decimal? _amendTransactionValue;
    private string _amendDescription = string.Empty;

    protected override async Task OnInitializedAsync()
    {
        // Its own short-lived context rather than the circuit-scoped ApplicationDbContext:
        // sharing that one lets this race, or outlive, whatever else in the circuit is using
        // it -- which kills the circuit and takes the page with it.
        await using var db = await DbFactory.CreateDbContextAsync();

        _activeTab = Nav.ToBaseRelativePath(Nav.Uri).Contains("mine", StringComparison.OrdinalIgnoreCase) ? "mine" : "new";

        var state = await AuthState.GetAuthenticationStateAsync();

        if (Impersonation.ActingMemberId is { } actingId)
        {
            _effectiveMember = await db.Members.Include(m => m.Company).FirstOrDefaultAsync(m => m.Id == actingId);
        }
        else
        {
            var userId = state.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (userId is not null)
            {
                _effectiveMember = await db.Members.Include(m => m.Company).FirstOrDefaultAsync(m => m.ApplicationUserId == userId);
            }
        }

        var isAdmin = state.User.IsInRole(GovernanceRoles.Administrator);
        if (_effectiveMember is null || (!isAdmin && !_effectiveMember.RelatedPartyTransactionAccess))
        {
            _step = Step.NoAccess;
            return;
        }

        _counterPartyNames = await RelatedPartyMasterSource.GetNamesAsync(db);
        await LoadMyTransactionsAsync();

        _step = Step.Ready;
    }

    private async Task LoadMyTransactionsAsync()
    {
        // Its own short-lived context rather than the circuit-scoped ApplicationDbContext:
        // sharing that one lets this race, or outlive, whatever else in the circuit is using
        // it -- which kills the circuit and takes the page with it.
        await using var db = await DbFactory.CreateDbContextAsync();

        _myTransactions = await db.RelatedPartyTransactions
            .AsNoTracking()
            .Include(t => t.ApproverMember)
            .Where(t => t.MemberId == _effectiveMember!.Id)
            .OrderByDescending(t => t.CreatedAtUtc)
            .ToListAsync();
    }

    private void SwitchTab(string tab) => _activeTab = tab;

    // ---------- stage-1 pre-checks ----------

    private PreCheckResult? _preChecks;

    /// <summary>A stand-in for the rail above an unsaved form: a transaction at stage 1. The rail
    /// reads a transaction, and there isn't one yet.</summary>
    private RelatedPartyTransaction DraftForRail { get; } = new() { Status = RpTransactionStatus.AwaitingApproval };

    private async Task OnCounterPartyChangedAsync(string? value)
    {
        _counterPartyName = value ?? string.Empty;
        await RunPreChecksAsync();
    }

    private async Task OnValueChangedAsync(decimal? value)
    {
        _transactionValue = value;
        await RunPreChecksAsync();
    }

    /// <summary>Runs as the form is filled, so the requestor sees where this goes and what will be
    /// flagged before submitting rather than after. Nothing here blocks Submit except the two the
    /// form already refused: no counter-party, and a value of zero.</summary>
    private async Task RunPreChecksAsync()
    {
        if (_effectiveMember is null) return;
        _preChecks = await PreChecks.RunAsync(_effectiveMember.Id, _counterPartyName, _transactionValue);
    }

    /// <summary>Returned is the state amending exists for, so it is amendable by definition -- and
    /// unlike the other two, resubmitting it puts it back in front of the approver.</summary>
    private static bool CanAmend(RelatedPartyTransaction t) =>
        t.Status is RpTransactionStatus.AwaitingApproval or RpTransactionStatus.Escalated or RpTransactionStatus.Returned;

    private async Task UploadDocumentAsync(Microsoft.AspNetCore.Components.Forms.InputFileChangeEventArgs e)
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
            Toasts.ShowError("Only JPEG, PNG, or PDF files are supported.");
            return;
        }
        if (file.Size > MaxDocumentFileBytes)
        {
            Toasts.ShowError("File must be 10 MB or smaller.");
            return;
        }

        _uploadingDocument = true;
        _uploadProgress = 0;
        StateHasChanged();
        try
        {
            var uploadsDir = Path.Combine(Env.WebRootPath, "uploads", "rp-transactions");
            Directory.CreateDirectory(uploadsDir);

            var fileName = $"{_effectiveMember!.Id}-{Guid.NewGuid():N}{extension}";
            var filePath = Path.Combine(uploadsDir, fileName);

            const int chunkBytes = 64 * 1024;
            await using (var stream = file.OpenReadStream(MaxDocumentFileBytes))
            await using (var target = File.Create(filePath))
            {
                var buffer = new byte[chunkBytes];
                long totalRead = 0;
                int read;
                while ((read = await stream.ReadAsync(buffer)) > 0)
                {
                    await target.WriteAsync(buffer.AsMemory(0, read));
                    totalRead += read;
                    _uploadProgress = (int)(totalRead * 100 / file.Size);
                    StateHasChanged();
                }
            }

            _pendingDocuments.Add(new RelatedPartyTransactionDocument { FilePath = $"/uploads/rp-transactions/{fileName}", FileName = file.Name });
            Toasts.ShowSuccess("File uploaded.");
        }
        finally
        {
            _uploadingDocument = false;
        }
    }

    private void RemovePendingDocument(RelatedPartyTransactionDocument doc)
    {
        _pendingDocuments.Remove(doc);
        try
        {
            var fullPath = Path.Combine(Env.WebRootPath, doc.FilePath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(fullPath)) File.Delete(fullPath);
        }
        catch
        {
            // Best-effort cleanup -- an orphaned file on disk is not worth blocking the user's edit over.
        }
    }

    private async Task SubmitAsync()
    {
        // Its own short-lived context rather than the circuit-scoped ApplicationDbContext:
        // sharing that one lets this race, or outlive, whatever else in the circuit is using
        // it -- which kills the circuit and takes the page with it.
        await using var db = await DbFactory.CreateDbContextAsync();

        if (string.IsNullOrWhiteSpace(_counterPartyName))
        {
            Toasts.ShowError("Select the name of the counter-party.");
            return;
        }
        if (_transactionValue is not (> 0))
        {
            Toasts.ShowError("Transaction value must be greater than 0.");
            return;
        }
        if (string.IsNullOrWhiteSpace(_description))
        {
            Toasts.ShowError("Enter a description of the transaction, including material terms and conditions.");
            return;
        }

        _submitting = true;
        try
        {
            var company = await db.Companies
                .Include(c => c.ApprovingAuthorityMember)
                .FirstOrDefaultAsync(c => c.Id == _effectiveMember!.CompanyId);

            if (company?.ApprovingAuthorityMember is null)
            {
                Toasts.ShowError("No Approver is configured for your entity. Ask an Administrator to set one on the Company record (Approving Authority) before submitting.");
                return;
            }

            var state = await AuthState.GetAuthenticationStateAsync();
            var actorName = state.User.Identity?.Name ?? "unknown";
            var isImpersonating = Impersonation.ActingMemberId is not null;

            var isConflicted = await ApproverIsConflictedAsync(company.ApprovingAuthorityMember.Id, _counterPartyName);

            // The other half of "route by authority": over the approver's limit the transaction does
            // not go to them at all. An entity with no matrix, or no limit on it, keeps the
            // behaviour it had -- every transaction to the approver, whatever it is worth.
            var matrix = await db.DelegationsOfAuthority
                .AsNoTracking()
                .FirstOrDefaultAsync(d => d.CompanyId == company.Id);
            var overLimit = matrix?.ApproverLimit is { } limit && _transactionValue!.Value > limit;

            var transaction = new RelatedPartyTransaction
            {
                CompanyId = company.Id,
                MemberId = _effectiveMember!.Id,
                CounterPartyName = _counterPartyName.Trim(),
                TransactionValue = _transactionValue!.Value,
                Description = _description.Trim(),
                DateOfRequest = DateTime.UtcNow,
                ApproverMemberId = company.ApprovingAuthorityMember.Id,
                SubmittedByName = actorName,
                SubmittedOnBehalfOf = isImpersonating ? _effectiveMember.FullName : null,
            };

            // A conflicted approver is the stronger reason of the two: it says the approver must not
            // see it, where the limit only says they cannot clear it.
            if (isConflicted || overLimit)
            {
                transaction.Status = RpTransactionStatus.Escalated;
                transaction.EscalationReason = isConflicted
                    ? RpEscalationReason.ApproverConflictOfInterest
                    : RpEscalationReason.OverApproverLimit;
                transaction.EscalatedAtUtc = DateTime.UtcNow;
            }
            else
            {
                transaction.Status = RpTransactionStatus.AwaitingApproval;
                transaction.EscalationReason = RpEscalationReason.None;
            }

            var newId = await Writer.InsertAsync(transaction);
            transaction.Id = newId;
            transaction.Member = _effectiveMember;
            transaction.Company = company;

            foreach (var doc in _pendingDocuments)
            {
                await Writer.InsertDocumentAsync(newId, doc);
            }
            transaction.Documents = [.. _pendingDocuments];

            if (isConflicted || overLimit)
            {
                var ccaoEmails = await RpTransactionRoleResolver.GetRoleEmailsAsync(db, RpTransactionRole.Ccao);
                var cfoEmails = await RpTransactionRoleResolver.GetRoleEmailsAsync(db, RpTransactionRole.Cfo);
                await RpTransactionNotificationService.EscalatedAsync(EmailSender, transaction, company.ApprovingAuthorityMember.Email, ccaoEmails, cfoEmails, transaction.EscalationReason);
            }
            else
            {
                await RpTransactionNotificationService.SubmittedAsync(EmailSender, transaction, company.ApprovingAuthorityMember.Email);
            }

            await AuditLog.LogAsync(actorName, AuditAction.Create, nameof(RelatedPartyTransaction), newId.ToString(),
                $"Submitted RP Transaction {RelatedPartyTransaction.DisplayReference(newId)} (counter-party: {transaction.CounterPartyName}, value: {transaction.TransactionValue:N2})" +
                (isConflicted ? " -- auto-escalated to CCAO (Approver is conflicted per their own RP&COI declaration)." : string.Empty));

            Toasts.ShowSuccess(isConflicted
                ? $"Transaction {RelatedPartyTransaction.DisplayReference(newId)} submitted and auto-escalated to the CCAO's office (your Approver is conflicted on this counter-party)."
                : $"Transaction {RelatedPartyTransaction.DisplayReference(newId)} submitted and is awaiting Approver decision.");

            _counterPartyName = string.Empty;
            _transactionValue = null;
            _description = string.Empty;
            _pendingDocuments.Clear();

            await LoadMyTransactionsAsync();
            _counterPartyNames = await RelatedPartyMasterSource.GetNamesAsync(db);
            _activeTab = "mine";
        }
        finally
        {
            _submitting = false;
        }
    }

    /// <summary>FRD §3.3.3 item (ii)/Addendum 1 §4.2.1 -- an Approver conflicted on this counter-party
    /// (per their own most recently submitted RP&amp;COI declaration's relatives/company interests)
    /// never gets a chance to act; the transaction routes straight to CCAO.</summary>
    private async Task<bool> ApproverIsConflictedAsync(int approverMemberId, string counterPartyName)
    {
        // Its own short-lived context rather than the circuit-scoped ApplicationDbContext:
        // sharing that one lets this race, or outlive, whatever else in the circuit is using
        // it -- which kills the circuit and takes the page with it.
        await using var db = await DbFactory.CreateDbContextAsync();

        var latestDeclaration = await db.RelatedPartyCoiDeclarations
            .AsNoTracking()
            .Include(d => d.Relatives)
            .Include(d => d.Companies)
            .Where(d => d.MemberId == approverMemberId && !d.IsDraft)
            .OrderByDescending(d => d.ModifiedAtUtc ?? d.SubmittedAtUtc)
            .FirstOrDefaultAsync();

        if (latestDeclaration is null) return false;

        return latestDeclaration.Relatives.Any(r => string.Equals(r.Name, counterPartyName, StringComparison.OrdinalIgnoreCase))
            || latestDeclaration.Companies.Any(c => string.Equals(c.LegalCompanyName, counterPartyName, StringComparison.OrdinalIgnoreCase));
    }

    private void StartAmend(RelatedPartyTransaction t)
    {
        _amendingRow = t;
        _amendCounterPartyName = t.CounterPartyName;
        _amendTransactionValue = t.TransactionValue;
        _amendDescription = t.Description;
    }

    private void CancelAmend() => _amendingRow = null;

    private async Task SaveAmendAsync()
    {
        // Its own short-lived context rather than the circuit-scoped ApplicationDbContext:
        // sharing that one lets this race, or outlive, whatever else in the circuit is using
        // it -- which kills the circuit and takes the page with it.
        await using var db = await DbFactory.CreateDbContextAsync();

        if (_amendingRow is null) return;

        if (string.IsNullOrWhiteSpace(_amendCounterPartyName) || _amendTransactionValue is not (> 0) || string.IsNullOrWhiteSpace(_amendDescription))
        {
            Toasts.ShowError("All fields are required and the value must be greater than 0.");
            return;
        }

        var t = await db.RelatedPartyTransactions.Include(x => x.Member).Include(x => x.Company).Include(x => x.ApproverMember)
            .FirstOrDefaultAsync(x => x.Id == _amendingRow.Id);
        if (t is null || !CanAmend(t)) { _amendingRow = null; return; }

        await Writer.AmendAsync(t.Id, _amendCounterPartyName.Trim(), _amendTransactionValue!.Value, _amendDescription.Trim());

        // Amending a returned transaction is how it goes back: the requestor was asked to fix
        // something, and saving the fix is them handing it back rather than a separate step they
        // could forget.
        var resubmitted = t.Status == RpTransactionStatus.Returned;
        if (resubmitted)
        {
            await Writer.ResubmitAsync(t.Id);
            t.Status = RpTransactionStatus.AwaitingApproval;
        }

        t.CounterPartyName = _amendCounterPartyName.Trim();
        t.TransactionValue = _amendTransactionValue!.Value;
        t.Description = _amendDescription.Trim();

        // FRD §3.3 Note 2 -- re-notify whoever currently holds the transaction that it's been amended.
        var amendedNote = $"({(resubmitted ? "Amended and resubmitted" : "Amended")} -- please re-review) {t.Description}";
        var amendedSubject = $"{RelatedPartyTransaction.DisplayReference(t.Id)} — {(resubmitted ? "Amended and resubmitted" : "Amended")}";
        if (t.Status == RpTransactionStatus.Escalated)
        {
            var ccaoEmails = await RpTransactionRoleResolver.GetRoleEmailsAsync(db, RpTransactionRole.Ccao);
            var cfoEmails = await RpTransactionRoleResolver.GetRoleEmailsAsync(db, RpTransactionRole.Cfo);
            foreach (var email in ccaoEmails.Concat(cfoEmails))
            {
                await EmailSender.SendAsync(email, amendedSubject, amendedNote);
            }
        }
        else if (!string.IsNullOrWhiteSpace(t.ApproverMember?.Email))
        {
            await EmailSender.SendAsync(t.ApproverMember.Email, amendedSubject, amendedNote);
        }

        var state = await AuthState.GetAuthenticationStateAsync();
        await AuditLog.LogAsync(state.User.Identity?.Name ?? "unknown", AuditAction.Update, nameof(RelatedPartyTransaction), t.Id.ToString(),
            resubmitted
                ? $"Amended and resubmitted RP Transaction {RelatedPartyTransaction.DisplayReference(t.Id)} after it was returned."
                : $"Amended RP Transaction {RelatedPartyTransaction.DisplayReference(t.Id)} while pending.");

        _amendingRow = null;
        Toasts.ShowSuccess(resubmitted ? "Amended and sent back to your approver." : "Transaction amended.");
        await LoadMyTransactionsAsync();
    }
}
