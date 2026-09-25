using System.Security.Claims;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using CGTOOL.Web.Data;
using CGTOOL.Web.Data.Governance;

namespace CGTOOL.Web.Components.Pages;

public partial class SubmitInsiderDeclaration
{
    // Screen1/Screen2 split per the Insider Trading Declaration Functional Spec: Screen 1 (NIN
    // questions, "Next") and Screen 2 (document uploads + shareholding question/grid, "Save as
    // Draft" / "Proceed to Submission").
    private enum Step { Loading, NoAccess, NotDue, EditWindowClosed, Capture1, Capture2, Review, Done }

    [Parameter] public int? Id { get; set; }

    private Step _step = Step.Loading;
    private Member? _effectiveMember;
    private DeclarationCycleRun? _run;
    private DateTime? _submittedAtUtc;
    private bool _submitting;
    private bool _isEditing;
    private int _editingDeclarationId;

    // Set once, at load, and never mutated afterwards -- distinct from _isEditing (which flips true
    // the moment this session's own Capture2->Review autosave writes a row). Audit/email wording and
    // the Review step's button/heading text need to reflect the session's ORIGINAL context (did the
    // declarant open an existing row, and was it already a real submission), not the fact that
    // GoToReviewAsync may since have written a row of its own.
    private bool _hadExistingRow;
    private bool _loadedAsSubmittedEdit;
    private bool _justAutoSaved;

    // Case 1 (Functional Spec §3.1)
    private bool _hasNin;
    private string _ninNumber = string.Empty;

    // Functional Spec §3.2 "Relatives' NIN" -- independent of whether those relatives hold shares.
    private bool _relativesHaveNin;
    private readonly List<NinHolderRow> _ninHolders = [];

    // Functional Spec §4: a single combined question replaces the old separate "do you hold shares" /
    // "do your relatives hold shares" pair. The declarant's own holding, if any, is captured as a
    // grid row with IsSelf=true rather than via separate fields.
    private bool _holdsSharesInDI;
    private readonly List<RelativeRow> _relatives = [];

    // Legacy self-shareholding fields (Case 2) -- no longer captured by this wizard (Functional Spec
    // §4 folds this into the combined grid above), but preserved so declarations submitted before this
    // change keep displaying correctly in the compliance report and print preview.
    private bool _holdsShares;
    private string _sharesNinNumber = string.Empty;
    private int? _numberOfSharesHeld;
    private bool _relativesHoldShares;

    // Supporting documents (Functional Spec §3.3): Emirates ID and Passport are mandatory, Trade
    // Licence and Other Documents optional. OCR auto-capture (Azure AI Document Intelligence) is
    // intentionally not wired up -- see the field comments on InsiderDeclaration for why -- so these
    // "Captured Information" fields are entered manually alongside the upload.
    private string? _emiratesIdPath;
    private string _emiratesIdNumber = string.Empty;
    private string _emiratesIdNameOnCard = string.Empty;
    private DateTime? _emiratesIdExpiryDate;
    private bool _uploadingEmiratesId;
    private int _emiratesIdUploadProgress;

    private string? _passportPath;
    private string _passportNumber = string.Empty;
    private DateTime? _passportExpiryDate;
    private string _passportIssuingCountry = string.Empty;
    private bool _uploadingPassport;
    private int _passportUploadProgress;

    private string? _tradeLicencePath;
    private string _tradeLicenceNumber = string.Empty;
    private string _tradeLicenceLegalName = string.Empty;
    private string _tradeLicenceIssuingAuthority = string.Empty;
    private DateTime? _tradeLicenceExpiryDate;
    private bool _uploadingTradeLicence;
    private int _tradeLicenceUploadProgress;

    private string? _otherDocumentPath;
    private bool _uploadingOtherDocument;
    private int _otherDocumentUploadProgress;

    // Set by PrefillDocumentsFromPriorDeclarationAsync when that document was carried forward from
    // the declarant's last submission but has since expired -- the Path is deliberately left blank in
    // that case (so GoToCapture2's mandatory-upload check still catches it), and this flag drives the
    // "please re-upload" notice next to that specific field.
    private bool _emiratesIdExpiredNeedsReupload;
    private bool _passportExpiredNeedsReupload;
    private bool _tradeLicenceExpiredNeedsReupload;

    private const long MaxIdUploadBytes = 1 * 1024 * 1024;

    private bool _pendingCapture1Confirm;
    private bool _savingDraft;
    private bool _agreeConfirmed;

    // Functional Spec §3.2/§4 "Relatives option set", used consistently by both grids.
    private static readonly (RelativeRelationship Value, string Label)[] RelativeOptions =
    [
        (RelativeRelationship.Father, "Father"),
        (RelativeRelationship.Mother, "Mother"),
        (RelativeRelationship.Brother, "Brother"),
        (RelativeRelationship.Sister, "Sister"),
        (RelativeRelationship.Children, "Children"),
        (RelativeRelationship.Spouse, "Spouse"),
        (RelativeRelationship.FatherInLaw, "Father-in-Law"),
        (RelativeRelationship.MotherInLaw, "Mother-in-Law"),
        (RelativeRelationship.Stepchildren, "Stepchildren"),
    ];

    private class RelativeRow
    {
        public string NinNumber { get; set; } = string.Empty;
        public string RelativeName { get; set; } = string.Empty;
        public RelativeRelationship Relationship { get; set; } = RelativeRelationship.Mother;
        public int NumberOfShares { get; set; }
        public string Additional { get; set; } = string.Empty;
        public bool IsSelf { get; set; }
    }

    private class NinHolderRow
    {
        public RelativeRelationship Relationship { get; set; } = RelativeRelationship.Mother;
        public string NameOfShareHolder { get; set; } = string.Empty;
        public string NinNumber { get; set; } = string.Empty;
        public string Additional { get; set; } = string.Empty;
    }

    // Functional Spec §3.1/§8: alphanumeric, minimum 10 characters.
    private static bool IsValidNin(string nin) => !string.IsNullOrWhiteSpace(nin) && nin.Trim().Length >= 10 && nin.Trim().All(char.IsLetterOrDigit);

    protected override async Task OnParametersSetAsync() => await LoadAsync();

    private async Task LoadAsync()
    {
        // Its own short-lived context rather than the circuit-scoped ApplicationDbContext:
        // sharing that one lets this race, or outlive, whatever else in the circuit is using
        // it -- which kills the circuit and takes the page with it.
        await using var db = await DbFactory.CreateDbContextAsync();

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

        if (_effectiveMember is null || !_effectiveMember.InsiderTradingAccess)
        {
            _step = Step.NoAccess;
            return;
        }

        if (Id is { } editId)
        {
            await LoadForEditAsync(editId);
            return;
        }

        // A saved-but-not-submitted draft is still a row in InsiderDeclarations, so it must not count
        // as "already submitted" here -- otherwise the run it belongs to would look done and the
        // member could never get back to their own draft via the plain (no Id) URL.
        var submittedRunIds = await db.InsiderDeclarations
            .AsNoTracking()
            .Where(d => d.MemberId == _effectiveMember.Id && !d.IsDraft)
            .Select(d => d.DeclarationCycleRunId)
            .ToListAsync();

        // ThenByDescending(Id): SentAtUtc alone isn't a unique/stable sort key, so without a
        // deterministic tiebreaker this could disagree with the identical queries in
        // InsiderDeclarationBanner.razor and MyDeclarations.razor.cs and open a different run (with a
        // different due date) than the one those pages showed.
        _run = await db.DeclarationCycleRuns
            .AsNoTracking()
            .Where(r => r.Type == DeclarationCycleType.InsiderTrading
                && (r.CompanyId == null || r.CompanyId == _effectiveMember.CompanyId)
                && !r.Recalled
                && !submittedRunIds.Contains(r.Id))
            .OrderByDescending(r => r.SentAtUtc)
            .ThenByDescending(r => r.Id)
            .FirstOrDefaultAsync();

        if (_run is not null)
        {
            var existingDraftId = await db.InsiderDeclarations
                .AsNoTracking()
                .Where(d => d.MemberId == _effectiveMember.Id && d.DeclarationCycleRunId == _run.Id && d.IsDraft)
                .Select(d => (int?)d.Id)
                .FirstOrDefaultAsync();

            if (existingDraftId is { } draftId)
            {
                await LoadForEditAsync(draftId);
                return;
            }

            await PrefillDocumentsFromPriorDeclarationAsync();
        }

        _step = _run is null ? Step.NotDue : Step.Capture1;
    }

    // Carries Emirates ID / Passport / Trade Licence details forward from the declarant's most recent
    // prior submission (any earlier cycle, not just last quarter) so they don't have to re-enter and
    // re-upload the same documents every period. A still-valid document's Path is carried forward too
    // (nothing to do -- GoToCapture2's "already uploaded" check passes as-is); an expired one has its
    // Path left blank so that same check still forces a fresh upload, while its text fields are
    // pre-filled anyway as a head start. Only called for a genuinely new declaration (no draft already
    // exists for this run) -- LoadForEditAsync handles editing an existing row separately.
    private async Task PrefillDocumentsFromPriorDeclarationAsync()
    {
        // Its own short-lived context rather than the circuit-scoped ApplicationDbContext:
        // sharing that one lets this race, or outlive, whatever else in the circuit is using
        // it -- which kills the circuit and takes the page with it.
        await using var db = await DbFactory.CreateDbContextAsync();

        var prior = await db.InsiderDeclarations
            .AsNoTracking()
            .Where(d => d.MemberId == _effectiveMember!.Id && !d.IsDraft)
            .OrderByDescending(d => d.SubmittedAtUtc)
            .ThenByDescending(d => d.Id)
            .FirstOrDefaultAsync();

        if (prior is null) return;

        var today = DateTime.UtcNow.Date;

        _emiratesIdNumber = prior.EmiratesIdNumber ?? string.Empty;
        _emiratesIdNameOnCard = prior.EmiratesIdNameOnCard ?? string.Empty;
        _emiratesIdExpiryDate = prior.EmiratesIdExpiryDate;
        if (prior.EmiratesIdExpiryDate is { } eidExpiry && eidExpiry.Date >= today)
        {
            _emiratesIdPath = prior.EmiratesIdPath;
        }
        else
        {
            _emiratesIdExpiredNeedsReupload = !string.IsNullOrEmpty(prior.EmiratesIdPath);
        }

        _passportNumber = prior.PassportNumber ?? string.Empty;
        _passportIssuingCountry = prior.PassportIssuingCountry ?? string.Empty;
        _passportExpiryDate = prior.PassportExpiryDate;
        if (prior.PassportExpiryDate is { } passportExpiry && passportExpiry.Date >= today)
        {
            _passportPath = prior.PassportPath;
        }
        else
        {
            _passportExpiredNeedsReupload = !string.IsNullOrEmpty(prior.PassportPath);
        }

        _tradeLicenceNumber = prior.TradeLicenceNumber ?? string.Empty;
        _tradeLicenceLegalName = prior.TradeLicenceLegalName ?? string.Empty;
        _tradeLicenceIssuingAuthority = prior.TradeLicenceIssuingAuthority ?? string.Empty;
        _tradeLicenceExpiryDate = prior.TradeLicenceExpiryDate;
        if (prior.TradeLicenceExpiryDate is { } licenceExpiry && licenceExpiry.Date >= today)
        {
            _tradeLicencePath = prior.TradeLicencePath;
        }
        else
        {
            _tradeLicenceExpiredNeedsReupload = !string.IsNullOrEmpty(prior.TradeLicencePath);
        }

        if (_emiratesIdExpiredNeedsReupload || _passportExpiredNeedsReupload || _tradeLicenceExpiredNeedsReupload)
        {
            Toasts.ShowError("Details carried over from your last declaration — please upload a current copy of the document(s) marked as expired below.");
        }
        else
        {
            Toasts.ShowSuccess("Emirates ID, Passport, and Trade Licence details carried over from your last declaration — please verify before continuing.");
        }
    }

    // AsNoTracking: this DbContext instance is scoped to the whole circuit (reused across page
    // navigations), and writes here go through raw stored procedures rather than EF's change
    // tracker/SaveChanges -- without it, EF's identity map would keep returning the pre-edit entity
    // on a later re-read within the same circuit (e.g. My Declarations' print preview after editing).
    private async Task LoadForEditAsync(int declarationId)
    {
        // Its own short-lived context rather than the circuit-scoped ApplicationDbContext:
        // sharing that one lets this race, or outlive, whatever else in the circuit is using
        // it -- which kills the circuit and takes the page with it.
        await using var db = await DbFactory.CreateDbContextAsync();

        var declaration = await db.InsiderDeclarations
            .AsNoTracking()
            .Include(d => d.DeclarationCycleRun)
            .Include(d => d.Relatives)
            .Include(d => d.NinHolders)
            .FirstOrDefaultAsync(d => d.Id == declarationId && d.MemberId == _effectiveMember!.Id);

        if (declaration is null || declaration.DeclarationCycleRun is null)
        {
            _step = Step.NotDue;
            return;
        }

        // A draft can still point at a run an admin has since recalled (recall is only offered while
        // a run has zero submissions, which a draft doesn't count as) -- treat it the same as "not due".
        if (declaration.DeclarationCycleRun.Recalled)
        {
            _step = Step.NotDue;
            return;
        }

        // Editing is allowed through the entire due date (comparing dates, not times), per FRD §7.4.
        if (declaration.DeclarationCycleRun.DueDateUtc.Date < DateTime.UtcNow.Date)
        {
            _step = Step.EditWindowClosed;
            return;
        }

        _isEditing = true;
        _hadExistingRow = true;
        _loadedAsSubmittedEdit = !declaration.IsDraft;
        _editingDeclarationId = declaration.Id;
        _run = declaration.DeclarationCycleRun;

        _hasNin = declaration.HasNin;
        _ninNumber = declaration.NinNumber ?? string.Empty;
        _relativesHaveNin = declaration.RelativesHaveNin;
        _holdsShares = declaration.HoldsShares;
        _sharesNinNumber = declaration.SharesNinNumber ?? string.Empty;
        _numberOfSharesHeld = declaration.NumberOfSharesHeld;
        _relativesHoldShares = declaration.RelativesHoldShares;
        _holdsSharesInDI = declaration.HoldsShares || declaration.RelativesHoldShares;

        _emiratesIdPath = declaration.EmiratesIdPath;
        _emiratesIdNumber = declaration.EmiratesIdNumber ?? string.Empty;
        _emiratesIdNameOnCard = declaration.EmiratesIdNameOnCard ?? string.Empty;
        _emiratesIdExpiryDate = declaration.EmiratesIdExpiryDate;

        _passportPath = declaration.PassportPath;
        _passportNumber = declaration.PassportNumber ?? string.Empty;
        _passportExpiryDate = declaration.PassportExpiryDate;
        _passportIssuingCountry = declaration.PassportIssuingCountry ?? string.Empty;

        _tradeLicencePath = declaration.TradeLicencePath;
        _tradeLicenceNumber = declaration.TradeLicenceNumber ?? string.Empty;
        _tradeLicenceLegalName = declaration.TradeLicenceLegalName ?? string.Empty;
        _tradeLicenceIssuingAuthority = declaration.TradeLicenceIssuingAuthority ?? string.Empty;
        _tradeLicenceExpiryDate = declaration.TradeLicenceExpiryDate;

        _otherDocumentPath = declaration.OtherDocumentPath;

        _relatives.Clear();
        _relatives.AddRange(declaration.Relatives.Select(r => new RelativeRow
        {
            NinNumber = r.NinNumber ?? string.Empty,
            RelativeName = r.RelativeName,
            Relationship = r.Relationship,
            NumberOfShares = r.NumberOfShares,
            Additional = r.Additional ?? string.Empty,
            IsSelf = r.IsSelf,
        }));

        _ninHolders.Clear();
        _ninHolders.AddRange(declaration.NinHolders.Select(h => new NinHolderRow
        {
            Relationship = h.Relationship,
            NameOfShareHolder = h.NameOfShareHolder,
            NinNumber = h.NinNumber,
            Additional = h.Additional ?? string.Empty,
        }));

        _step = Step.Capture1;
    }

    // Functional Spec §4 compliance requirement: every addition or removal of a grid row must be
    // captured in the audit log, not just the current snapshot, so reports can show the full history
    // of alterations rather than only the latest state. Logged immediately (not deferred to final
    // submit) so the trail survives even if the declarant never gets past this screen.
    private async Task AddRelativeAsync()
    {
        _relatives.Add(new RelativeRow());
        await LogGridChangeAsync("Added a blank row to the shareholding grid.");
    }

    private async Task RemoveRelativeAsync(RelativeRow row)
    {
        _relatives.Remove(row);
        var who = row.IsSelf ? "Self" : row.RelativeName;
        await LogGridChangeAsync($"Removed shareholding grid row: {who} ({RelationshipLabel(row.Relationship)}), NIN {row.NinNumber}.");
    }

    private async Task AddNinHolderAsync()
    {
        _ninHolders.Add(new NinHolderRow());
        await LogGridChangeAsync("Added a blank row to the relatives' NIN grid.");
    }

    private async Task RemoveNinHolderAsync(NinHolderRow row)
    {
        _ninHolders.Remove(row);
        await LogGridChangeAsync($"Removed relatives' NIN grid row: {row.NameOfShareHolder} ({RelationshipLabel(row.Relationship)}), NIN {row.NinNumber}.");
    }

    private async Task LogGridChangeAsync(string detail)
    {
        if (_effectiveMember is null) return;
        var state = await AuthState.GetAuthenticationStateAsync();
        var actorName = state.User.Identity?.Name ?? "unknown";
        var isImpersonating = Impersonation.ActingMemberId is not null;
        await AuditLog.LogAsync(actorName, AuditAction.Update, nameof(InsiderDeclaration), _effectiveMember.Id.ToString(), detail,
            actingOnBehalfOf: isImpersonating ? _effectiveMember.FullName : null);
    }

    private Task OnEmiratesIdSelectedAsync(InputFileChangeEventArgs e) => UploadIdDocumentAsync(e, "emirates-id",
        path => _emiratesIdPath = path, () => _uploadingEmiratesId = false, () => _uploadingEmiratesId = true,
        p => _emiratesIdUploadProgress = p, AnalyzeAndFillEmiratesIdAsync);

    private Task OnPassportSelectedAsync(InputFileChangeEventArgs e) => UploadIdDocumentAsync(e, "passport",
        path => _passportPath = path, () => _uploadingPassport = false, () => _uploadingPassport = true,
        p => _passportUploadProgress = p, AnalyzeAndFillPassportAsync);

    private Task OnTradeLicenceSelectedAsync(InputFileChangeEventArgs e) => UploadIdDocumentAsync(e, "trade-licence",
        path => _tradeLicencePath = path, () => _uploadingTradeLicence = false, () => _uploadingTradeLicence = true,
        p => _tradeLicenceUploadProgress = p, AnalyzeAndFillTradeLicenceAsync);

    private Task OnOtherDocumentSelectedAsync(InputFileChangeEventArgs e) => UploadIdDocumentAsync(e, "other-document",
        path => _otherDocumentPath = path, () => _uploadingOtherDocument = false, () => _uploadingOtherDocument = true,
        p => _otherDocumentUploadProgress = p, onSavedAsync: null);

    private const int UploadChunkBytes = 64 * 1024;

    // Functional Spec §3.3: JPG, PNG, or PDF only, 1 MB max. Copies in chunks (rather than a single
    // CopyToAsync) purely so onProgress/StateHasChanged can report percent-complete back to the
    // progress bar in the UI as the upload streams in over SignalR.
    private async Task UploadIdDocumentAsync(InputFileChangeEventArgs e, string kind, Action<string> onSaved, Action onDone, Action onStart, Action<int> onProgress, Func<string, Task>? onSavedAsync)
    {
        if (_effectiveMember is null) return;

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
        if (file.Size > MaxIdUploadBytes)
        {
            Toasts.ShowError("File must be 1 MB or smaller.");
            return;
        }

        onStart();
        onProgress(0);
        StateHasChanged();
        try
        {
            var uploadsDir = Path.Combine(Env.WebRootPath, "uploads", "insider-declarations");
            Directory.CreateDirectory(uploadsDir);

            foreach (var existing in Directory.GetFiles(uploadsDir, $"{_effectiveMember.Id}-{kind}.*"))
            {
                File.Delete(existing);
            }

            var fileName = $"{_effectiveMember.Id}-{kind}{extension}";
            var filePath = Path.Combine(uploadsDir, fileName);

            await using (var stream = file.OpenReadStream(MaxIdUploadBytes))
            await using (var target = File.Create(filePath))
            {
                var buffer = new byte[UploadChunkBytes];
                long totalRead = 0;
                int read;
                while ((read = await stream.ReadAsync(buffer)) > 0)
                {
                    await target.WriteAsync(buffer.AsMemory(0, read));
                    totalRead += read;
                    onProgress((int)(totalRead * 100 / file.Size));
                    StateHasChanged();
                }
            }

            onProgress(100);
            onSaved($"/uploads/insider-declarations/{fileName}");
            Toasts.ShowSuccess("File uploaded.");

            if (onSavedAsync is not null)
            {
                await onSavedAsync(filePath);
            }
        }
        finally
        {
            onDone();
        }
    }

    // Functional Spec §3.3 "Automated Document Capture": read the just-uploaded file with Azure AI
    // Document Intelligence and pre-fill the corresponding fields. Silently does nothing when
    // DocumentIntelligence isn't configured (manual entry, as before) or when the model can't read the
    // file -- OCR on a photographed ID is never guaranteed, and every field it does fill stays a plain
    // editable input, so the declarant always has the final say before submitting.
    private async Task AnalyzeAndFillEmiratesIdAsync(string filePath) => await AnalyzeAndFillAsync(filePath, extraction =>
    {
        if (!string.IsNullOrWhiteSpace(extraction.DocumentNumber)) _emiratesIdNumber = extraction.DocumentNumber;
        var name = string.Join(" ", new[] { extraction.FirstName, extraction.LastName }.Where(s => !string.IsNullOrWhiteSpace(s)));
        if (!string.IsNullOrWhiteSpace(name)) _emiratesIdNameOnCard = name;
        if (extraction.DateOfExpiration is not null) _emiratesIdExpiryDate = extraction.DateOfExpiration;
    });

    private async Task AnalyzeAndFillPassportAsync(string filePath) => await AnalyzeAndFillAsync(filePath, extraction =>
    {
        if (!string.IsNullOrWhiteSpace(extraction.DocumentNumber)) _passportNumber = extraction.DocumentNumber;
        if (extraction.DateOfExpiration is not null) _passportExpiryDate = extraction.DateOfExpiration;
        if (!string.IsNullOrWhiteSpace(extraction.CountryRegion)) _passportIssuingCountry = extraction.CountryRegion;
    });

    private async Task AnalyzeAndFillAsync(string filePath, Action<IdDocumentExtraction> apply)
    {
        if (!DocIntel.IsConfigured) return;

        try
        {
            await using var stream = File.OpenRead(filePath);
            var extraction = await DocIntel.AnalyzeIdDocumentAsync(stream);
            if (extraction is null) return;

            apply(extraction);
            Toasts.ShowSuccess("Details auto-filled from the document — please verify before continuing.");
        }
        catch (Exception ex)
        {
            // OCR is a convenience, not a hard requirement -- the declarant can still fill every field
            // by hand, so a failed call here should never block the upload that already succeeded.
            Toasts.ShowError($"Could not auto-read the document ({ex.Message}). Enter the details manually.");
        }
    }

    // Trade licences run through the general-purpose document model rather than a purpose-built one
    // (see the comment on TradeLicenceExtraction) -- extraction is a rougher best-effort here, so the
    // success message says so rather than implying the same confidence as the ID document fields.
    private async Task AnalyzeAndFillTradeLicenceAsync(string filePath)
    {
        if (!DocIntel.IsConfigured) return;

        try
        {
            await using var stream = File.OpenRead(filePath);
            var extraction = await DocIntel.AnalyzeTradeLicenceAsync(stream);
            if (extraction is null) return;

            if (!string.IsNullOrWhiteSpace(extraction.LicenceNumber)) _tradeLicenceNumber = extraction.LicenceNumber;
            if (!string.IsNullOrWhiteSpace(extraction.BusinessName)) _tradeLicenceLegalName = extraction.BusinessName;
            if (extraction.ExpiryDate is not null) _tradeLicenceExpiryDate = extraction.ExpiryDate;

            if (extraction.LicenceNumber is not null || extraction.BusinessName is not null || extraction.ExpiryDate is not null)
            {
                Toasts.ShowSuccess("Best-effort details auto-filled from the trade licence — please check these carefully before continuing.");
            }
        }
        catch (Exception ex)
        {
            Toasts.ShowError($"Could not auto-read the trade licence ({ex.Message}). Enter the details manually.");
        }
    }

    // Screen 1 "Next" -- Functional Spec §3.1/§3.2/§8 validation.
    private void GoToCapture2()
    {
        if (_hasNin && !IsValidNin(_ninNumber))
        {
            Toasts.ShowError("Enter a valid NIN number: letters and digits only (no special characters), minimum 10 characters.");
            return;
        }

        if (_relativesHaveNin)
        {
            if (_ninHolders.Count == 0)
            {
                Toasts.ShowError("Add at least one relative, or answer No to the relatives' NIN question.");
                return;
            }
            foreach (var h in _ninHolders)
            {
                if (string.IsNullOrWhiteSpace(h.NameOfShareHolder))
                {
                    Toasts.ShowError("Enter the name of share holder for every relatives' NIN row.");
                    return;
                }
                if (!IsValidNin(h.NinNumber))
                {
                    Toasts.ShowError("Enter a valid NIN: letters and digits only (no special characters), minimum 10 characters, for every relatives' NIN row.");
                    return;
                }
            }
        }

        // Give the declarant a last chance to go back and correct anything before moving on --
        // ConfirmCapture2 is what actually advances to Screen 2.
        _pendingCapture1Confirm = true;
    }

    private void ConfirmCapture1()
    {
        _pendingCapture1Confirm = false;
        _step = Step.Capture2;
    }

    private void CancelCapture1Confirm() => _pendingCapture1Confirm = false;

    // Screen 2 "Proceed to Submission" -- validates, then autosaves (Functional Spec §5 "Declaration
    // saved") before landing on the confirmation step. The autosave preserves whatever submitted/draft
    // status the row already had when this session opened it (_loadedAsSubmittedEdit) rather than
    // always writing IsDraft=true -- otherwise reviewing an edit to an already-submitted declaration
    // would wrongly flip it back to "not submitted" on the compliance report before Confirm is even
    // clicked.
    private async Task GoToReviewAsync()
    {
        if (string.IsNullOrWhiteSpace(_emiratesIdPath))
        {
            Toasts.ShowError("Upload your Emirates ID.");
            return;
        }
        if (string.IsNullOrWhiteSpace(_emiratesIdNumber) || string.IsNullOrWhiteSpace(_emiratesIdNameOnCard) || _emiratesIdExpiryDate is null)
        {
            Toasts.ShowError("Enter the Emirates ID number, name on card, and expiry date.");
            return;
        }

        if (string.IsNullOrWhiteSpace(_passportPath))
        {
            Toasts.ShowError("Upload your passport.");
            return;
        }
        if (string.IsNullOrWhiteSpace(_passportNumber) || _passportExpiryDate is null || string.IsNullOrWhiteSpace(_passportIssuingCountry))
        {
            Toasts.ShowError("Enter the passport number, expiry date, and issuing country.");
            return;
        }

        if (_holdsSharesInDI)
        {
            if (_relatives.Count == 0)
            {
                Toasts.ShowError("Add at least one shareholder row, or answer No to the shareholding question.");
                return;
            }
            foreach (var r in _relatives)
            {
                var who = r.IsSelf ? "Self" : r.RelativeName;
                if (!r.IsSelf && string.IsNullOrWhiteSpace(r.RelativeName))
                {
                    Toasts.ShowError("Enter the name of share holder for every row (or mark it as yourself).");
                    return;
                }
                if (!IsValidNin(r.NinNumber))
                {
                    Toasts.ShowError($"Enter a valid NIN: letters and digits only (no special characters), minimum 10 characters, for {who}.");
                    return;
                }
            }
        }

        if (_effectiveMember is null || _run is null) return;

        try
        {
            var state = await AuthState.GetAuthenticationStateAsync();
            var actorName = state.User.Identity?.Name ?? "unknown";
            var isImpersonating = Impersonation.ActingMemberId is not null;
            await PersistAsync(isDraft: !_loadedAsSubmittedEdit, actorName, isImpersonating);
        }
        catch (SqlException ex) when (ex.Number == 50011)
        {
            Toasts.ShowError("This Insider Trading declaration was already submitted.");
            await LoadAsync();
            return;
        }

        _justAutoSaved = true;
        _agreeConfirmed = false;
        _step = Step.Review;
    }

    // Shared by SubmitAsync, SaveDraftAsync, and GoToReviewAsync's autosave: writes the header + grid
    // rows in one transaction and, on success, flips this page into "editing" mode against whatever
    // row it just wrote so a later save updates that same row instead of trying to insert a duplicate
    // -- the insert proc throws once a row already exists for this member+cycle.
    private async Task<int> PersistAsync(bool isDraft, string actorName, bool isImpersonating)
    {
        // Its own short-lived context rather than the circuit-scoped ApplicationDbContext:
        // sharing that one lets this race, or outlive, whatever else in the circuit is using
        // it -- which kills the circuit and takes the page with it.
        await using var db = await DbFactory.CreateDbContextAsync();

        var declaration = new InsiderDeclaration
        {
            Id = _editingDeclarationId,
            MemberId = _effectiveMember!.Id,
            DeclarationCycleRunId = _run!.Id,
            HasNin = _hasNin,
            NinNumber = _hasNin ? _ninNumber.Trim() : null,
            HoldsShares = _holdsSharesInDI,
            SharesNinNumber = _holdsShares ? _sharesNinNumber.Trim() : null,
            NumberOfSharesHeld = _holdsShares ? _numberOfSharesHeld : null,
            RelativesHoldShares = _holdsSharesInDI,
            RelativesHaveNin = _relativesHaveNin,
            IsDraft = isDraft,
            EmiratesIdPath = _emiratesIdPath,
            EmiratesIdNumber = string.IsNullOrWhiteSpace(_emiratesIdNumber) ? null : _emiratesIdNumber.Trim(),
            EmiratesIdNameOnCard = string.IsNullOrWhiteSpace(_emiratesIdNameOnCard) ? null : _emiratesIdNameOnCard.Trim(),
            EmiratesIdExpiryDate = _emiratesIdExpiryDate,
            PassportPath = _passportPath,
            PassportNumber = string.IsNullOrWhiteSpace(_passportNumber) ? null : _passportNumber.Trim(),
            PassportExpiryDate = _passportExpiryDate,
            PassportIssuingCountry = string.IsNullOrWhiteSpace(_passportIssuingCountry) ? null : _passportIssuingCountry.Trim(),
            TradeLicencePath = _tradeLicencePath,
            TradeLicenceNumber = string.IsNullOrWhiteSpace(_tradeLicenceNumber) ? null : _tradeLicenceNumber.Trim(),
            TradeLicenceLegalName = string.IsNullOrWhiteSpace(_tradeLicenceLegalName) ? null : _tradeLicenceLegalName.Trim(),
            TradeLicenceIssuingAuthority = string.IsNullOrWhiteSpace(_tradeLicenceIssuingAuthority) ? null : _tradeLicenceIssuingAuthority.Trim(),
            TradeLicenceExpiryDate = _tradeLicenceExpiryDate,
            OtherDocumentPath = _otherDocumentPath,
            SubmittedByName = actorName,
            SubmittedOnBehalfOf = isImpersonating ? _effectiveMember.FullName : null,
        };

        int declarationId;
        await using (var tx = await db.Database.BeginTransactionAsync())
        {
            if (_isEditing)
            {
                await Writer.UpdateAsync(declaration);
                await Writer.DeleteRelativesAsync(_editingDeclarationId);
                await Writer.DeleteNinHoldersAsync(_editingDeclarationId);
                declarationId = _editingDeclarationId;
            }
            else
            {
                declarationId = await Writer.InsertAsync(declaration);
            }

            if (_holdsSharesInDI)
            {
                foreach (var r in _relatives)
                {
                    await Writer.InsertRelativeAsync(declarationId, new InsiderDeclarationRelative
                    {
                        NinNumber = r.NinNumber.Trim(),
                        RelativeName = (r.IsSelf ? _effectiveMember.FullName : r.RelativeName).Trim(),
                        Relationship = r.Relationship,
                        NumberOfShares = r.NumberOfShares,
                        Additional = string.IsNullOrWhiteSpace(r.Additional) ? null : r.Additional.Trim(),
                        IsSelf = r.IsSelf,
                    });
                }
            }

            if (_relativesHaveNin)
            {
                foreach (var h in _ninHolders)
                {
                    await Writer.InsertNinHolderAsync(declarationId, new InsiderDeclarationNinHolder
                    {
                        Relationship = h.Relationship,
                        NameOfShareHolder = h.NameOfShareHolder.Trim(),
                        NinNumber = h.NinNumber.Trim(),
                        Additional = string.IsNullOrWhiteSpace(h.Additional) ? null : h.Additional.Trim(),
                    });
                }
            }

            await tx.CommitAsync();
        }

        _isEditing = true;
        _editingDeclarationId = declarationId;
        return declarationId;
    }

    // Lets the declarant save their in-progress answers without submitting -- a draft is not gated by
    // the Capture-step validation (GoToReviewAsync) since it's expected to be incomplete, and it never
    // triggers the confirmation email or counts as "Submitted" on the compliance report.
    private async Task SaveDraftAsync()
    {
        if (_savingDraft || _submitting || _effectiveMember is null || _run is null) return;
        _savingDraft = true;

        try
        {
            var state = await AuthState.GetAuthenticationStateAsync();
            var actorName = state.User.Identity?.Name ?? "unknown";
            var isImpersonating = Impersonation.ActingMemberId is not null;

            try
            {
                await PersistAsync(isDraft: true, actorName, isImpersonating);
            }
            catch (SqlException ex) when (ex.Number == 50011)
            {
                Toasts.ShowError("This Insider Trading declaration was already submitted.");
                await LoadAsync();
                return;
            }

            await AuditLog.LogAsync(actorName, _hadExistingRow ? AuditAction.Update : AuditAction.Create, nameof(InsiderDeclaration), _effectiveMember.Id.ToString(),
                "Draft saved: " + BuildSummary(), actingOnBehalfOf: isImpersonating ? _effectiveMember.FullName : null);

            Toasts.ShowSuccess("Declaration saved as draft. You can come back and finish it any time before the due date.");
        }
        finally
        {
            _savingDraft = false;
        }
    }

    private async Task SubmitAsync()
    {
        // Its own short-lived context rather than the circuit-scoped ApplicationDbContext:
        // sharing that one lets this race, or outlive, whatever else in the circuit is using
        // it -- which kills the circuit and takes the page with it.
        await using var db = await DbFactory.CreateDbContextAsync();

        // Guards against a double-click (or a slow round-trip) firing this handler a second time
        // while the first call is still in flight -- two concurrent calls sharing the circuit's
        // single ApplicationDbContext instance would otherwise crash with "a second operation was
        // started on this context instance before a previous operation completed."
        if (_submitting || _savingDraft || _effectiveMember is null || _run is null) return;
        if (!_agreeConfirmed)
        {
            Toasts.ShowError("Please confirm that the information given in this form is true, complete and accurate.");
            return;
        }
        _submitting = true;

        try
        {
            var state = await AuthState.GetAuthenticationStateAsync();
            var actorName = state.User.Identity?.Name ?? "unknown";
            var isImpersonating = Impersonation.ActingMemberId is not null;

            int declarationId;
            try
            {
                declarationId = await PersistAsync(isDraft: false, actorName, isImpersonating);
            }
            catch (SqlException ex) when (ex.Number == 50011)
            {
                Toasts.ShowError("This Insider Trading declaration was already submitted.");
                await LoadAsync();
                return;
            }

            var summary = BuildSummary();
            await AuditLog.LogAsync(actorName, _hadExistingRow ? AuditAction.Update : AuditAction.Create, nameof(InsiderDeclaration), _effectiveMember.Id.ToString(), summary,
                actingOnBehalfOf: isImpersonating ? _effectiveMember.FullName : null);

            if (!string.IsNullOrWhiteSpace(_effectiveMember.Email))
            {
                var attachPdf = SqlMail.IsConfigured && SqlMail.AttachmentFolderConfigured;
                var confirmSubject = $"Insider Trading Declaration {(_hadExistingRow ? "update" : "confirmation")} — Q{_run.PeriodQuarter} {_run.PeriodYear}";
                var confirmBody = BuildConfirmationEmail(summary, attached: attachPdf);
                try
                {
                    if (attachPdf)
                    {
                        var declaration = await db.InsiderDeclarations
                            .AsNoTracking()
                            .Include(x => x.Relatives)
                            .Include(x => x.NinHolders)
                            .FirstAsync(x => x.Id == declarationId);
                        var logoPath = Path.Combine(Env.WebRootPath, "images", "di-logo.jpg");
                        var pdfBytes = InsiderDeclarationPdfBuilder.Build(declaration, _effectiveMember, _run, File.Exists(logoPath) ? logoPath : null);
                        var attachmentFilename = $"InsiderTradingDeclaration_Q{_run.PeriodQuarter}_{_run.PeriodYear}.pdf";
                        await SqlMail.SendWithFileAttachmentAsync(_effectiveMember.Email, confirmSubject, confirmBody, pdfBytes, attachmentFilename);
                    }
                    else if (SqlMail.IsConfigured)
                    {
                        await SqlMail.SendAsync(_effectiveMember.Email, confirmSubject, confirmBody);
                    }
                    else
                    {
                        await EmailSender.SendAsync(_effectiveMember.Email, confirmSubject, confirmBody);
                    }

                    await AuditLog.LogAsync(actorName, AuditAction.Notify, nameof(Member), _effectiveMember.Id.ToString(),
                        $"Insider Trading declaration {(_hadExistingRow ? "update" : "confirmation")} sent to {_effectiveMember.FullName} <{_effectiveMember.Email}>{(attachPdf ? " with PDF attached" : "")}");
                }
                catch (Exception ex)
                {
                    await AuditLog.LogAsync(actorName, AuditAction.Notify, nameof(Member), _effectiveMember.Id.ToString(),
                        $"Insider Trading declaration {(_hadExistingRow ? "update" : "confirmation")} to {_effectiveMember.FullName} <{_effectiveMember.Email}> FAILED: {ex.Message}");
                }
            }

            _submittedAtUtc = DateTime.UtcNow;
            _step = Step.Done;
            Toasts.ShowSuccess($"Insider Trading declaration {(_hadExistingRow ? "updated" : "submitted")}.");
        }
        finally
        {
            _submitting = false;
        }
    }

    private string BuildSummary()
    {
        var parts = new List<string>
        {
            $"NIN: {(_hasNin ? $"Yes ({_ninNumber})" : "No")}",
            $"Relatives have NIN: {(_relativesHaveNin ? "Yes" : "No")}",
            $"Holds shares in DI (self or relatives): {(_holdsSharesInDI ? "Yes" : "No")}",
        };

        if (_relativesHaveNin)
        {
            parts.AddRange(_ninHolders.Select(h => $"Relative NIN holder: {h.NameOfShareHolder} ({RelationshipLabel(h.Relationship)}), NIN {h.NinNumber}"));
        }

        if (_holdsSharesInDI)
        {
            parts.AddRange(_relatives.Select(r => r.IsSelf
                ? $"Shareholder: Self, NIN {r.NinNumber}"
                : $"Shareholder: {r.RelativeName} ({RelationshipLabel(r.Relationship)}), NIN {r.NinNumber}"));
        }

        return string.Join("; ", parts);
    }

    // Corporate-branded to match WelcomeEmailTemplate's DI navy/gold styling. summary's semicolon-
    // joined fragments (from BuildSummary) become table rows rather than a raw <br/>-joined dump, and
    // the note at the bottom reflects whichever attachment path actually ran (a real PDF only when
    // SqlMail.AttachmentFolderConfigured -- see SqlDbMailSender for why that's conditional).
    private string BuildConfirmationEmail(string summary, bool attached)
    {
        const string logoTag = "<img src=\"https://cg.dubaiinvestments.com/images/di-logo.jpg\" alt=\"Dubai Investments\" height=\"44\" style=\"display:block;\" />";
        var rows = string.Join("", summary.Split("; ").Select(row =>
        {
            var parts = row.Split(':', 2);
            var label = parts[0].Trim();
            var value = parts.Length > 1 ? parts[1].Trim() : "";
            return $"""
                <tr>
                  <td style="padding:8px 0;border-bottom:1px solid #EEF0F3;color:#8993A3;font-size:12px;width:55%;">{label}</td>
                  <td style="padding:8px 0;border-bottom:1px solid #EEF0F3;color:#1B2430;font-size:13px;font-weight:600;">{value}</td>
                </tr>
                """;
        }));

        var attachmentNote = attached
            ? "<p>A PDF copy of this declaration is attached to this email for your records.</p>"
            : "<p>You can view and print this declaration at any time from <b>My Declarations</b> in the Corporate Governance Tool.</p>";

        return $$"""
            <!DOCTYPE html>
            <html>
            <body style="margin:0;padding:0;background-color:#EEF0F3;font-family:Segoe UI,Arial,sans-serif;">
              <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="background-color:#EEF0F3;padding:24px 0;">
                <tr>
                  <td align="center">
                    <table role="presentation" width="600" cellpadding="0" cellspacing="0" style="background-color:#FFFFFF;border-radius:6px;overflow:hidden;">
                      <tr>
                        <td style="background-color:#0E2A47;padding:24px 32px;">
                          {{logoTag}}
                          <div style="color:#D9B65A;font-size:16px;font-weight:600;margin-top:8px;">Corporate Governance Tool</div>
                          <div style="color:#FFFFFF;font-size:12px;margin-top:2px;">Insider Trading Declaration — {{(_hadExistingRow ? "Update Confirmation" : "Submission Confirmation")}}</div>
                        </td>
                      </tr>
                      <tr>
                        <td style="padding:32px;color:#1B2430;font-size:14px;line-height:1.6;">
                          <p>Dear {{_effectiveMember!.FullName}},</p>
                          <p>
                            Your Insider Trading declaration for <b>Q{{_run!.PeriodQuarter}} {{_run.PeriodYear}}</b> has been
                            <b>{{(_hadExistingRow ? "updated" : "recorded")}}</b> on {{DateTime.Now:dd MMM yyyy HH:mm}}.
                          </p>
                          <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="margin:16px 0;">
                            {{rows}}
                          </table>
                          {{attachmentNote}}
                          <p>Should you have any queries on this matter, please contact the Corporate Affairs Office.</p>
                          <p>Thank you.</p>
                        </td>
                      </tr>
                      <tr>
                        <td style="background-color:#F5F6F8;padding:16px 32px;color:#8993A3;font-size:11px;line-height:1.5;">
                          <strong>Disclaimer:</strong> Please do not reply to this email. The information contained in
                          this message may be CONFIDENTIAL and is for the intended addressee only. Any unauthorized use,
                          dissemination of the information, or copying of this message is prohibited. If you are not the
                          intended addressee, please notify the sender immediately and delete this message.
                        </td>
                      </tr>
                    </table>
                  </td>
                </tr>
              </table>
            </body>
            </html>
            """;
    }

    private static string RelationshipLabel(RelativeRelationship relationship) => relationship switch
    {
        RelativeRelationship.InLaws => "In-Laws",
        RelativeRelationship.FatherInLaw => "Father-in-Law",
        RelativeRelationship.MotherInLaw => "Mother-in-Law",
        _ => relationship.ToString(),
    };

    // Functional Spec §2 merge fields {From}/{To}: the calendar quarter the admin explicitly picked
    // for this run in Declarations Setup (DeclarationCycleRun.PeriodYear/PeriodQuarter), not derived
    // from when the notification happened to be sent.
    private (DateTime From, DateTime To) PeriodRange()
    {
        var from = new DateTime(_run!.PeriodYear, ((_run.PeriodQuarter - 1) * 3) + 1, 1);
        return (from, from.AddMonths(3).AddDays(-1));
    }
}
