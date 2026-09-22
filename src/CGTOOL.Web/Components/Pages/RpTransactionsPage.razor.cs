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
        _activeTab = Nav.ToBaseRelativePath(Nav.Uri).Contains("mine", StringComparison.OrdinalIgnoreCase) ? "mine" : "new";

        var state = await AuthState.GetAuthenticationStateAsync();

        if (Impersonation.ActingMemberId is { } actingId)
        {
            _effectiveMember = await Db.Members.Include(m => m.Company).FirstOrDefaultAsync(m => m.Id == actingId);
        }
        else
        {
            var userId = state.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (userId is not null)
            {
                _effectiveMember = await Db.Members.Include(m => m.Company).FirstOrDefaultAsync(m => m.ApplicationUserId == userId);
            }
        }

        var isAdmin = state.User.IsInRole(GovernanceRoles.Administrator);
        if (_effectiveMember is null || (!isAdmin && !_effectiveMember.RelatedPartyTransactionAccess))
        {
            _step = Step.NoAccess;
            return;
        }

        _counterPartyNames = await RelatedPartyMasterSource.GetNamesAsync(Db);
        await LoadMyTransactionsAsync();

        _step = Step.Ready;
    }

    private async Task LoadMyTransactionsAsync()
    {
        _myTransactions = await Db.RelatedPartyTransactions
            .AsNoTracking()
            .Include(t => t.ApproverMember)
            .Where(t => t.MemberId == _effectiveMember!.Id)
            .OrderByDescending(t => t.CreatedAtUtc)
            .ToListAsync();
    }

    private void SwitchTab(string tab) => _activeTab = tab;

    private static bool CanAmend(RelatedPartyTransaction t) =>
        t.Status is RpTransactionStatus.AwaitingApproval or RpTransactionStatus.Escalated;

    // Who currently needs to act -- the named Approver while AwaitingApproval, otherwise CCAO (a
    // role any number of Members can hold, so named generically rather than resolving specific
    // people). Terminal statuses (Rejected/ReleasedToRegister) have nobody left to act.
    private static string PendingWithName(RelatedPartyTransaction t) => t.Status switch
    {
        RpTransactionStatus.AwaitingApproval => t.ApproverMember?.FullName ?? "Approver",
        RpTransactionStatus.Escalated or RpTransactionStatus.Approved => "CCAO",
        _ => "—",
    };

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
            var company = await Db.Companies
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

            if (isConflicted)
            {
                transaction.Status = RpTransactionStatus.Escalated;
                transaction.EscalationReason = RpEscalationReason.ApproverConflictOfInterest;
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

            if (isConflicted)
            {
                var ccaoEmails = await RpTransactionRoleResolver.GetRoleEmailsAsync(Db, RpTransactionRole.Ccao);
                var cfoEmails = await RpTransactionRoleResolver.GetRoleEmailsAsync(Db, RpTransactionRole.Cfo);
                await RpTransactionNotificationService.EscalatedAsync(EmailSender, transaction, company.ApprovingAuthorityMember.Email, ccaoEmails, cfoEmails, RpEscalationReason.ApproverConflictOfInterest);
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
            _counterPartyNames = await RelatedPartyMasterSource.GetNamesAsync(Db);
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
        var latestDeclaration = await Db.RelatedPartyCoiDeclarations
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
        if (_amendingRow is null) return;

        if (string.IsNullOrWhiteSpace(_amendCounterPartyName) || _amendTransactionValue is not (> 0) || string.IsNullOrWhiteSpace(_amendDescription))
        {
            Toasts.ShowError("All fields are required and the value must be greater than 0.");
            return;
        }

        var t = await Db.RelatedPartyTransactions.Include(x => x.Member).Include(x => x.Company).Include(x => x.ApproverMember)
            .FirstOrDefaultAsync(x => x.Id == _amendingRow.Id);
        if (t is null || !CanAmend(t)) { _amendingRow = null; return; }

        await Writer.AmendAsync(t.Id, _amendCounterPartyName.Trim(), _amendTransactionValue!.Value, _amendDescription.Trim());

        t.CounterPartyName = _amendCounterPartyName.Trim();
        t.TransactionValue = _amendTransactionValue!.Value;
        t.Description = _amendDescription.Trim();

        // FRD §3.3 Note 2 -- re-notify whoever currently holds the transaction that it's been amended.
        var amendedNote = $"(Amended -- please re-review) {t.Description}";
        var amendedSubject = $"{RelatedPartyTransaction.DisplayReference(t.Id)} — Amended";
        if (t.Status == RpTransactionStatus.Escalated)
        {
            var ccaoEmails = await RpTransactionRoleResolver.GetRoleEmailsAsync(Db, RpTransactionRole.Ccao);
            var cfoEmails = await RpTransactionRoleResolver.GetRoleEmailsAsync(Db, RpTransactionRole.Cfo);
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
            $"Amended RP Transaction {RelatedPartyTransaction.DisplayReference(t.Id)} while pending.");

        _amendingRow = null;
        Toasts.ShowSuccess("Transaction amended.");
        await LoadMyTransactionsAsync();
    }

    private static string StatusLabel(RpTransactionStatus status) => status switch
    {
        RpTransactionStatus.AwaitingApproval => "Awaiting Approval",
        RpTransactionStatus.Approved => "Approved",
        RpTransactionStatus.Rejected => "Rejected",
        RpTransactionStatus.Escalated => "Escalated",
        RpTransactionStatus.ReleasedToRegister => "Released to Register",
        _ => status.ToString(),
    };

    private static string StatusPillClass(RpTransactionStatus status) => status switch
    {
        RpTransactionStatus.Approved or RpTransactionStatus.ReleasedToRegister => "cleared",
        RpTransactionStatus.Rejected => "flagged",
        _ => "pending",
    };
}
