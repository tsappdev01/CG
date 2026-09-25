using System.Security.Claims;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using CGTOOL.Web.Data;
using CGTOOL.Web.Data.Governance;

namespace CGTOOL.Web.Components.Pages;

public partial class SubmitRelatedPartyCoiDeclaration
{
    private enum Step { Loading, NoAccess, NotDue, EditWindowClosed, Form, Review, Done }

    [Parameter] public int? Id { get; set; }

    private Step _step = Step.Loading;
    private Member? _effectiveMember;
    private DeclarationCycleRun? _run;
    private DateTime? _submittedAtUtc;
    private bool _submitting;
    private bool _savingDraft;
    private bool _isEditing;
    private int _editingDeclarationId;

    private bool _hadExistingRow;
    private bool _loadedAsSubmittedEdit;
    private bool _justAutoSaved;

    private DateTime? _uaePassVerifiedAtUtc;
    private string? _uaePassVerifiedName;
    private bool _verifyingUaePass;

    // Determines which form variant (heading, disclaimer wording, Note 1 text) renders, per the
    // Functional Spec §3 comparison table. IsBoardMember takes priority when both flags are set (a
    // Board member who also holds Executive Management still gets the Board-facing wording). Anyone
    // who is neither a Board member nor Executive Management -- an ordinary Normal User -- falls
    // through to the Executive Management variant, same as Member.IsExecutiveManagement itself.
    private bool IsBod => _effectiveMember?.IsBoardMember ?? false;

    private bool _nothingRelatives;
    private bool _nothingSelfOwned;
    private bool _nothingRelativeOwned;
    private bool _nothingBoardRoles;
    private bool _nothingConflicts;

    private readonly List<RelativeRow> _relatives = [];
    private readonly List<CompanyRow> _selfOwnedCompanies = [];
    private readonly List<CompanyRow> _relativeOwnedCompanies = [];
    private readonly List<CompanyRow> _boardRoleCompanies = [];
    private readonly List<ConflictRow> _conflicts = [];

    // My Workspace (Family Members / Owned Companies) -- the declarant's own reusable reference data,
    // offered here as "pick from My Workspace" quick-adds so the same relative/company name and
    // trade license documents don't need retyping/re-uploading every declaration cycle.
    private readonly List<FamilyMember> _myFamilyMembers = [];
    private readonly List<OwnedCompany> _myCompanies = [];
    private int? _pickFamilyMemberId;
    private int? _pickSelfCompanyId;
    private int? _pickRelativeCompanyId;
    private int? _pickBoardCompanyId;

    // I.D can reuse a company already entered in I.B or I.C; Part II's conflicts table can reuse
    // any company entered anywhere in I.B/I.C/I.D. Both drive the <datalist> suggestions so the
    // declarant doesn't retype trade license details for a company already declared above.
    private List<CompanyRow> CompaniesBeforeBoardRole => [.. _selfOwnedCompanies, .. _relativeOwnedCompanies];
    private List<CompanyRow> AllDeclaredCompanies => [.. _selfOwnedCompanies, .. _relativeOwnedCompanies, .. _boardRoleCompanies];

    private string _attestationName = string.Empty;
    private bool _attestationConfirmed;
    private bool _pendingFormConfirm;

    private const long MaxTradeLicenseFileBytes = 10 * 1024 * 1024;
    private const int MaxDocumentsPerRow = 10;

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
        (RelativeRelationship.Stepchildren, "Children of spouse"),
    ];

    // Public (not private): CompanyRowEditor.razor, a sibling component that renders one I.B/I.C/I.D
    // row, binds directly to these types.
    public class RelativeRow
    {
        public Guid Key { get; } = Guid.NewGuid();
        public string Name { get; set; } = string.Empty;
        public RelativeRelationship Relationship { get; set; } = RelativeRelationship.Father;
    }

    public class CompanyRow
    {
        public Guid Key { get; } = Guid.NewGuid();
        public string LegalCompanyName { get; set; } = string.Empty;
        public string PrincipalBusinessActivity { get; set; } = string.Empty;
        public string TradeLicenseNumber { get; set; } = string.Empty;
        public DateTime? TradeLicenseExpiryDate { get; set; }
        public string LicenseActivities { get; set; } = string.Empty;
        public List<UploadedFileRow> Documents { get; set; } = [];
        public Guid? LinkedRelativeKey { get; set; }
        public bool Uploading { get; set; }
        public int UploadProgress { get; set; }
    }

    public class UploadedFileRow
    {
        public string Path { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
    }

    private class ConflictRow
    {
        public Guid Key { get; } = Guid.NewGuid();
        public string CompanyOrCounterpartyName { get; set; } = string.Empty;
        public string PrincipalBusinessActivity { get; set; } = string.Empty;
        public string NatureOfHolding { get; set; } = string.Empty;
    }

    private static readonly string[] NatureOfHoldingOptions = ["Owned", "Affiliate", "Subsidiary"];

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

        if (_effectiveMember is null || !(_effectiveMember.ConflictOfInterestAccess || _effectiveMember.RelatedPartyRegisterAccess))
        {
            _step = Step.NoAccess;
            return;
        }

        _myFamilyMembers.Clear();
        _myFamilyMembers.AddRange(await db.FamilyMembers.AsNoTracking()
            .Where(f => f.MemberId == _effectiveMember.Id)
            .OrderBy(f => f.Id)
            .ToListAsync());

        _myCompanies.Clear();
        _myCompanies.AddRange(await db.OwnedCompanies.AsNoTracking()
            .Where(c => c.MemberId == _effectiveMember.Id)
            .OrderBy(c => c.Id)
            .ToListAsync());

        if (Id is { } editId)
        {
            await LoadForEditAsync(editId);
            return;
        }

        var submittedRunIds = await db.RelatedPartyCoiDeclarations
            .AsNoTracking()
            .Where(d => d.MemberId == _effectiveMember.Id && !d.IsDraft)
            .Select(d => d.DeclarationCycleRunId)
            .ToListAsync();

        _run = await db.DeclarationCycleRuns
            .AsNoTracking()
            .Where(r => r.Type == DeclarationCycleType.ConflictOfInterest
                && (r.CompanyId == null || r.CompanyId == _effectiveMember.CompanyId)
                && !r.Recalled
                && !submittedRunIds.Contains(r.Id))
            .OrderByDescending(r => r.SentAtUtc)
            .ThenByDescending(r => r.Id)
            .FirstOrDefaultAsync();

        if (_run is not null)
        {
            var existingDraftId = await db.RelatedPartyCoiDeclarations
                .AsNoTracking()
                .Where(d => d.MemberId == _effectiveMember.Id && d.DeclarationCycleRunId == _run.Id && d.IsDraft)
                .Select(d => (int?)d.Id)
                .FirstOrDefaultAsync();

            if (existingDraftId is { } draftId)
            {
                await LoadForEditAsync(draftId);
                return;
            }
        }

        if (_run is not null && string.IsNullOrWhiteSpace(_attestationName))
        {
            _attestationName = _effectiveMember.FullName;
        }

        _step = _run is null ? Step.NotDue : Step.Form;
    }

    private async Task LoadForEditAsync(int declarationId)
    {
        // Its own short-lived context rather than the circuit-scoped ApplicationDbContext:
        // sharing that one lets this race, or outlive, whatever else in the circuit is using
        // it -- which kills the circuit and takes the page with it.
        await using var db = await DbFactory.CreateDbContextAsync();

        var declaration = await db.RelatedPartyCoiDeclarations
            .AsNoTracking()
            .Include(d => d.DeclarationCycleRun)
            .Include(d => d.Relatives)
            .Include(d => d.Companies).ThenInclude(c => c.Documents)
            .Include(d => d.Conflicts)
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

        _nothingRelatives = declaration.NothingToDeclareRelatives;
        _nothingSelfOwned = declaration.NothingToDeclareSelfOwned;
        _nothingRelativeOwned = declaration.NothingToDeclareRelativeOwned;
        _nothingBoardRoles = declaration.NothingToDeclareBoardRoles;
        _nothingConflicts = declaration.NothingToDeclareConflicts;
        _attestationName = string.IsNullOrWhiteSpace(declaration.AttestationName) ? _effectiveMember!.FullName : declaration.AttestationName;
        _attestationConfirmed = declaration.AttestationConfirmed;
        _uaePassVerifiedAtUtc = declaration.UaePassVerifiedAtUtc;
        _uaePassVerifiedName = declaration.UaePassVerifiedName;

        _relatives.Clear();
        var relativeRowsByDbId = new Dictionary<int, RelativeRow>();
        foreach (var r in declaration.Relatives)
        {
            var row = new RelativeRow { Name = r.Name, Relationship = r.Relationship };
            _relatives.Add(row);
            relativeRowsByDbId[r.Id] = row;
        }

        _selfOwnedCompanies.Clear();
        _relativeOwnedCompanies.Clear();
        _boardRoleCompanies.Clear();
        foreach (var c in declaration.Companies)
        {
            var row = new CompanyRow
            {
                LegalCompanyName = c.LegalCompanyName,
                PrincipalBusinessActivity = c.PrincipalBusinessActivity ?? string.Empty,
                TradeLicenseNumber = c.TradeLicenseNumber ?? string.Empty,
                TradeLicenseExpiryDate = c.TradeLicenseExpiryDate,
                LicenseActivities = c.LicenseActivities ?? string.Empty,
                LinkedRelativeKey = c.CoiRelativeId is { } rid && relativeRowsByDbId.TryGetValue(rid, out var relRow) ? relRow.Key : null,
            };
            row.Documents.AddRange(c.Documents.Select(doc => new UploadedFileRow { Path = doc.FilePath, FileName = doc.FileName }));

            var target = c.OwnerType switch
            {
                CoiCompanyOwnerType.Self => _selfOwnedCompanies,
                CoiCompanyOwnerType.Relative => _relativeOwnedCompanies,
                _ => _boardRoleCompanies,
            };
            target.Add(row);
        }

        _conflicts.Clear();
        _conflicts.AddRange(declaration.Conflicts.Select(c => new ConflictRow
        {
            CompanyOrCounterpartyName = c.CompanyOrCounterpartyName,
            PrincipalBusinessActivity = c.PrincipalBusinessActivity ?? string.Empty,
            NatureOfHolding = c.NatureOfHolding ?? string.Empty,
        }));

        _step = Step.Form;
        ApplyUaePassReturnFlag();
    }

    // Detects the ?uaepass=verified|error flag /uaepass/callback appends when it redirects the
    // browser back here after UAE PASS login -- there's no live component instance to resume (the
    // external redirect tore down the previous Blazor circuit), so this is the only way this page
    // learns how that round trip went.
    private void ApplyUaePassReturnFlag()
    {
        var query = new Uri(Nav.Uri).Query;
        var flag = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(query).TryGetValue("uaepass", out var values)
            ? values.ToString()
            : null;

        if (flag == "verified")
        {
            Toasts.ShowSuccess(_uaePassVerifiedAtUtc is not null
                ? $"Identity verified via UAE PASS as {_uaePassVerifiedName}."
                : "UAE PASS verification did not complete -- please try again.");
            if (_uaePassVerifiedAtUtc is not null) _step = Step.Review;
        }
        else if (flag == "error")
        {
            Toasts.ShowError("UAE PASS verification failed or was cancelled. Please try again.");
        }
    }

    private void AddRelative() => _relatives.Add(new RelativeRow());
    private void RemoveRelative(RelativeRow row)
    {
        _relatives.Remove(row);
        // Company rows in I.C that pointed at this relative lose their link -- the declarant must
        // re-pick a relative before submitting, rather than silently keeping a dangling reference.
        foreach (var c in _relativeOwnedCompanies.Where(c => c.LinkedRelativeKey == row.Key))
        {
            c.LinkedRelativeKey = null;
        }
    }

    private static void AddCompany(List<CompanyRow> list) => list.Add(new CompanyRow());
    private static void RemoveCompany(List<CompanyRow> list, CompanyRow row) => list.Remove(row);

    // Family members/companies not yet added to this section -- offered in the "pick from My
    // Workspace" dropdowns so the same name/relationship isn't retyped, or the same trade license/MOA/
    // POA re-uploaded, every declaration cycle.
    private List<FamilyMember> AvailableFamilyMembers =>
        [.. _myFamilyMembers.Where(f => !_relatives.Any(r => string.Equals(r.Name, f.Name, StringComparison.OrdinalIgnoreCase)))];

    private List<OwnedCompany> AvailableCompaniesFor(List<CompanyRow> list) =>
        [.. _myCompanies.Where(c => !list.Any(r => string.Equals(r.LegalCompanyName, c.CompanyName, StringComparison.OrdinalIgnoreCase)))];

    private void AddFromMyFamily()
    {
        if (_pickFamilyMemberId is not { } id) return;
        var familyMember = _myFamilyMembers.FirstOrDefault(f => f.Id == id);
        if (familyMember is null) return;

        _relatives.Add(new RelativeRow { Name = familyMember.Name, Relationship = familyMember.Relationship });
        _pickFamilyMemberId = null;
    }

    private void AddCompanyFromWorkspace(List<CompanyRow> list, int? companyId)
    {
        if (companyId is not { } id) return;
        var company = _myCompanies.FirstOrDefault(c => c.Id == id);
        if (company is null) return;

        var row = new CompanyRow { LegalCompanyName = company.CompanyName };
        AddWorkspaceDocument(row, company.TradeLicensePath, "Trade License");
        AddWorkspaceDocument(row, company.MoaPath, "MOA");
        AddWorkspaceDocument(row, company.PoaPath, "POA");
        list.Add(row);
    }

    private static void AddWorkspaceDocument(CompanyRow row, string? path, string label)
    {
        if (string.IsNullOrEmpty(path)) return;
        row.Documents.Add(new UploadedFileRow { Path = path, FileName = $"{label} (from My Workspace)" });
    }

    private void AddConflict() => _conflicts.Add(new ConflictRow());
    private void RemoveConflict(ConflictRow row) => _conflicts.Remove(row);

    // Same manual-input pattern as CompanyRowEditor.OnLegalNameInput: keep typing, and once the
    // text exactly matches a company already declared in I.B/I.C/I.D, copy its Principal Business
    // Activity across too (Nature of Holding is a Part-II-specific judgment call, left untouched).
    private void OnConflictCompanyNameInput(ConflictRow row, ChangeEventArgs e)
    {
        var value = e.Value?.ToString() ?? string.Empty;
        row.CompanyOrCounterpartyName = value;

        var match = AllDeclaredCompanies.FirstOrDefault(c => string.Equals(c.LegalCompanyName, value, StringComparison.OrdinalIgnoreCase));
        if (match is not null)
        {
            row.PrincipalBusinessActivity = match.PrincipalBusinessActivity;
        }
    }

    // Functional Spec §5.6: expiry visibility. "Expired"/"Expiring soon" thresholds mirror the
    // spec's suggested 30/60-day window.
    public static string ExpiryStatus(DateTime? expiry)
    {
        if (expiry is null) return "unknown";
        var days = (expiry.Value.Date - DateTime.UtcNow.Date).TotalDays;
        if (days < 0) return "expired";
        if (days <= 60) return "expiring-soon";
        return "valid";
    }

    public static string ExpiryLabel(DateTime? expiry) => ExpiryStatus(expiry) switch
    {
        "expired" => "Expired",
        "expiring-soon" => "Expiring soon",
        "valid" => "Valid",
        _ => "No expiry set",
    };

    private async Task UploadTradeLicenseFileAsync(CompanyRow row, InputFileChangeEventArgs e)
    {
        if (_effectiveMember is null) return;

        if (row.Documents.Count >= MaxDocumentsPerRow)
        {
            Toasts.ShowError($"Maximum {MaxDocumentsPerRow} files per company row.");
            return;
        }

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
        if (file.Size > MaxTradeLicenseFileBytes)
        {
            Toasts.ShowError("File must be 10 MB or smaller.");
            return;
        }

        row.Uploading = true;
        row.UploadProgress = 0;
        StateHasChanged();
        try
        {
            var uploadsDir = Path.Combine(Env.WebRootPath, "uploads", "coi-trade-licenses");
            Directory.CreateDirectory(uploadsDir);

            var fileName = $"{_effectiveMember.Id}-{row.Key:N}-{row.Documents.Count}{extension}";
            var filePath = Path.Combine(uploadsDir, fileName);

            const int chunkBytes = 64 * 1024;
            await using (var stream = file.OpenReadStream(MaxTradeLicenseFileBytes))
            await using (var target = File.Create(filePath))
            {
                var buffer = new byte[chunkBytes];
                long totalRead = 0;
                int read;
                while ((read = await stream.ReadAsync(buffer)) > 0)
                {
                    await target.WriteAsync(buffer.AsMemory(0, read));
                    totalRead += read;
                    row.UploadProgress = (int)(totalRead * 100 / file.Size);
                    StateHasChanged();
                }
            }

            row.Documents.Add(new UploadedFileRow { Path = $"/uploads/coi-trade-licenses/{fileName}", FileName = file.Name });
            Toasts.ShowSuccess("File uploaded.");

            await AnalyzeAndFillTradeLicenseAsync(row, filePath);
        }
        finally
        {
            row.Uploading = false;
        }
    }

    // Same Azure AI Document Intelligence "prebuilt-layout" pass used by the Insider Trading
    // wizard's trade licence upload (see DocumentIntelligenceService.AnalyzeTradeLicenceAsync) --
    // best-effort only, silently does nothing when DocumentIntelligence isn't configured or the
    // model can't read the file, and every field it fills stays a plain editable input.
    private async Task AnalyzeAndFillTradeLicenseAsync(CompanyRow row, string filePath)
    {
        if (!DocIntel.IsConfigured) return;

        try
        {
            await using var stream = File.OpenRead(filePath);
            var extraction = await DocIntel.AnalyzeTradeLicenceAsync(stream);
            if (extraction is null) return;

            if (!string.IsNullOrWhiteSpace(extraction.LicenceNumber)) row.TradeLicenseNumber = extraction.LicenceNumber;
            if (!string.IsNullOrWhiteSpace(extraction.BusinessName)) row.LegalCompanyName = extraction.BusinessName;
            if (extraction.ExpiryDate is not null) row.TradeLicenseExpiryDate = extraction.ExpiryDate;

            if (extraction.LicenceNumber is not null || extraction.BusinessName is not null || extraction.ExpiryDate is not null)
            {
                Toasts.ShowSuccess("Best-effort details auto-filled from the trade licence — please check these carefully before continuing.");
                StateHasChanged();
            }
        }
        catch (Exception ex)
        {
            Toasts.ShowError($"Could not auto-read the trade licence ({ex.Message}). Enter the details manually.");
        }
    }

    private void RemoveDocument(CompanyRow row, UploadedFileRow doc)
    {
        row.Documents.Remove(doc);
        try
        {
            var fullPath = Path.Combine(Env.WebRootPath, doc.Path.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(fullPath)) File.Delete(fullPath);
        }
        catch
        {
            // Best-effort cleanup -- an orphaned file on disk is not worth blocking the user's edit over.
        }
    }

    private void GoToReview()
    {
        if (!ValidateForm()) return;
        _pendingFormConfirm = true;
    }

    private bool ValidateForm()
    {
        if (!_nothingRelatives && _relatives.Count == 0)
        {
            Toasts.ShowError("Add at least one relative in List of Relatives, or check \"I have nothing to declare\".");
            return false;
        }
        if (!_nothingRelatives && _relatives.Any(r => string.IsNullOrWhiteSpace(r.Name)))
        {
            Toasts.ShowError("Enter a name for every relative in List of Relatives.");
            return false;
        }

        if (!ValidateCompanySection(_selfOwnedCompanies, _nothingSelfOwned, "Companies you own ≥30%")) return false;
        if (!ValidateCompanySection(_relativeOwnedCompanies, _nothingRelativeOwned, "Companies a relative owns ≥30%", requireLinkedRelative: true)) return false;
        if (!ValidateCompanySection(_boardRoleCompanies, _nothingBoardRoles, "Companies where you are a board member/senior executive")) return false;

        if (!_nothingConflicts && _conflicts.Count == 0)
        {
            Toasts.ShowError("Add at least one entry in Part II, or check \"I have nothing to declare\".");
            return false;
        }
        if (!_nothingConflicts && _conflicts.Any(c => string.IsNullOrWhiteSpace(c.CompanyOrCounterpartyName) || string.IsNullOrWhiteSpace(c.NatureOfHolding)))
        {
            Toasts.ShowError("Enter the company/counterparty name and nature of holding for every Part II row.");
            return false;
        }

        if (string.IsNullOrWhiteSpace(_attestationName))
        {
            Toasts.ShowError("Enter your name to attest this declaration.");
            return false;
        }

        return true;
    }

    // Submission validation (Functional Spec §5.4/§7.8): every I.B/I.C/I.D row must have a company
    // name, trade license number, expiry date, license activities, and at least one uploaded document
    // before the declaration can move from Draft to Submitted. Draft save (SaveDraftAsync) skips all of
    // this so declarants aren't blocked mid-entry.
    private bool ValidateCompanySection(List<CompanyRow> rows, bool nothingToDeclare, string sectionLabel, bool requireLinkedRelative = false)
    {
        if (nothingToDeclare) return true;

        if (rows.Count == 0)
        {
            Toasts.ShowError($"Add at least one company in {sectionLabel}, or check \"I have nothing to declare\".");
            return false;
        }

        foreach (var row in rows)
        {
            if (string.IsNullOrWhiteSpace(row.LegalCompanyName))
            {
                Toasts.ShowError($"Enter the legal company name for every row in {sectionLabel}.");
                return false;
            }
            if (requireLinkedRelative && row.LinkedRelativeKey is null)
            {
                Toasts.ShowError($"Select which relative owns each company in {sectionLabel}.");
                return false;
            }
            if (string.IsNullOrWhiteSpace(row.TradeLicenseNumber) || row.TradeLicenseExpiryDate is null || string.IsNullOrWhiteSpace(row.LicenseActivities))
            {
                Toasts.ShowError($"Enter the trade license number, expiry date, and license activities for every row in {sectionLabel}.");
                return false;
            }
            if (row.Documents.Count == 0)
            {
                Toasts.ShowError($"Upload at least one trade license document for every row in {sectionLabel}.");
                return false;
            }
        }

        return true;
    }

    private void ConfirmForm()
    {
        _pendingFormConfirm = false;
        _step = Step.Review;
        _justAutoSaved = false;
    }

    private void CancelFormConfirm() => _pendingFormConfirm = false;

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
            catch (SqlException ex) when (ex.Number == 50012)
            {
                Toasts.ShowError("This Related Party & COI declaration was already submitted.");
                await LoadAsync();
                return;
            }

            await AuditLog.LogAsync(actorName, _hadExistingRow ? AuditAction.Update : AuditAction.Create, nameof(RelatedPartyCoiDeclaration), _effectiveMember.Id.ToString(),
                "Draft saved", actingOnBehalfOf: isImpersonating ? _effectiveMember.FullName : null);

            Toasts.ShowSuccess("Declaration saved as draft. You can come back and finish it any time before the due date.");
        }
        finally
        {
            _savingDraft = false;
        }
    }

    // Optional identity verification (see UaePassAuthService) -- not required to submit. Persists the
    // current form content as a draft first -- so /uaepass/callback has a declarationId to attach the
    // verification to -- then does a full-page redirect out to UAE PASS. There's no way back into this
    // exact component instance: the redirect tears down the Blazor circuit, and the browser lands back
    // on this page fresh via /uaepass/callback's own redirect (see ApplyUaePassReturnFlag).
    private async Task StartUaePassVerificationAsync()
    {
        if (_verifyingUaePass || _submitting || _savingDraft || _effectiveMember is null || _run is null) return;
        if (!ValidateForm()) return;

        _verifyingUaePass = true;
        try
        {
            var state = await AuthState.GetAuthenticationStateAsync();
            var actorName = state.User.Identity?.Name ?? "unknown";
            var isImpersonating = Impersonation.ActingMemberId is not null;

            try
            {
                await PersistAsync(isDraft: true, actorName, isImpersonating);
            }
            catch (SqlException ex) when (ex.Number == 50012)
            {
                Toasts.ShowError("This Related Party & COI declaration was already submitted.");
                await LoadAsync();
                return;
            }

            var redirectUri = UaePass.ResolveRedirectUri($"{Nav.BaseUri}uaepass/callback");
            var stateToken = UaePassStateProtector.Protect(_editingDeclarationId.ToString());
            var authorizeUrl = UaePass.BuildAuthorizeUrl(stateToken, redirectUri);
            Nav.NavigateTo(authorizeUrl, forceLoad: true);
        }
        finally
        {
            _verifyingUaePass = false;
        }
    }

    private async Task<int> PersistAsync(bool isDraft, string actorName, bool isImpersonating)
    {
        // Its own short-lived context rather than the circuit-scoped ApplicationDbContext:
        // sharing that one lets this race, or outlive, whatever else in the circuit is using
        // it -- which kills the circuit and takes the page with it.
        await using var db = await DbFactory.CreateDbContextAsync();

        var declaration = new RelatedPartyCoiDeclaration
        {
            Id = _editingDeclarationId,
            MemberId = _effectiveMember!.Id,
            DeclarationCycleRunId = _run!.Id,
            NothingToDeclareRelatives = _nothingRelatives,
            NothingToDeclareSelfOwned = _nothingSelfOwned,
            NothingToDeclareRelativeOwned = _nothingRelativeOwned,
            NothingToDeclareBoardRoles = _nothingBoardRoles,
            NothingToDeclareConflicts = _nothingConflicts,
            IsDraft = isDraft,
            AttestationName = _attestationName.Trim(),
            AttestationConfirmed = _attestationConfirmed,
            SubmittedByName = actorName,
            SubmittedOnBehalfOf = isImpersonating ? _effectiveMember.FullName : null,
        };

        int declarationId;
        await using (var tx = await db.Database.BeginTransactionAsync())
        {
            if (_isEditing)
            {
                await Writer.UpdateAsync(declaration);
                // Companies must go before relatives -- I.C company rows FK to CoiRelatives.
                await Writer.DeleteCompaniesAsync(_editingDeclarationId);
                await Writer.DeleteRelativesAsync(_editingDeclarationId);
                await Writer.DeleteConflictsAsync(_editingDeclarationId);
                declarationId = _editingDeclarationId;
            }
            else
            {
                declarationId = await Writer.InsertAsync(declaration);
            }

            var relativeIdMap = new Dictionary<Guid, int>();
            if (!_nothingRelatives)
            {
                foreach (var r in _relatives)
                {
                    var newId = await Writer.InsertRelativeAsync(declarationId, new CoiRelative { Name = r.Name.Trim(), Relationship = r.Relationship });
                    relativeIdMap[r.Key] = newId;
                }
            }

            await InsertCompanyRowsAsync(declarationId, _selfOwnedCompanies, CoiCompanyOwnerType.Self, _nothingSelfOwned, relativeIdMap);
            await InsertCompanyRowsAsync(declarationId, _relativeOwnedCompanies, CoiCompanyOwnerType.Relative, _nothingRelativeOwned, relativeIdMap);
            await InsertCompanyRowsAsync(declarationId, _boardRoleCompanies, CoiCompanyOwnerType.BoardOrExecutiveRole, _nothingBoardRoles, relativeIdMap);

            if (!_nothingConflicts)
            {
                foreach (var c in _conflicts)
                {
                    await Writer.InsertConflictAsync(declarationId, new CoiConflictEntry
                    {
                        CompanyOrCounterpartyName = c.CompanyOrCounterpartyName.Trim(),
                        PrincipalBusinessActivity = string.IsNullOrWhiteSpace(c.PrincipalBusinessActivity) ? null : c.PrincipalBusinessActivity.Trim(),
                        NatureOfHolding = string.IsNullOrWhiteSpace(c.NatureOfHolding) ? null : c.NatureOfHolding.Trim(),
                    });
                }
            }

            await tx.CommitAsync();
        }

        _isEditing = true;
        _editingDeclarationId = declarationId;
        return declarationId;
    }

    private async Task InsertCompanyRowsAsync(int declarationId, List<CompanyRow> rows, CoiCompanyOwnerType ownerType, bool nothingToDeclare, Dictionary<Guid, int> relativeIdMap)
    {
        if (nothingToDeclare) return;

        foreach (var row in rows)
        {
            var companyId = await Writer.InsertCompanyAsync(declarationId, new CoiCompanyEntry
            {
                OwnerType = ownerType,
                CoiRelativeId = row.LinkedRelativeKey is { } key && relativeIdMap.TryGetValue(key, out var rid) ? rid : null,
                LegalCompanyName = row.LegalCompanyName.Trim(),
                PrincipalBusinessActivity = string.IsNullOrWhiteSpace(row.PrincipalBusinessActivity) ? null : row.PrincipalBusinessActivity.Trim(),
                TradeLicenseNumber = string.IsNullOrWhiteSpace(row.TradeLicenseNumber) ? null : row.TradeLicenseNumber.Trim(),
                TradeLicenseExpiryDate = row.TradeLicenseExpiryDate,
                LicenseActivities = string.IsNullOrWhiteSpace(row.LicenseActivities) ? null : row.LicenseActivities.Trim(),
            });

            foreach (var doc in row.Documents)
            {
                await Writer.InsertDocumentAsync(companyId, new CoiTradeLicenseDocument { FilePath = doc.Path, FileName = doc.FileName });
            }
        }
    }

    private async Task SubmitAsync()
    {
        if (_submitting || _savingDraft || _effectiveMember is null || _run is null) return;
        if (!_attestationConfirmed)
        {
            Toasts.ShowError("Please confirm that the information given in this declaration is true, complete and accurate.");
            return;
        }
        _submitting = true;

        try
        {
            var state = await AuthState.GetAuthenticationStateAsync();
            var actorName = state.User.Identity?.Name ?? "unknown";
            var isImpersonating = Impersonation.ActingMemberId is not null;

            // Captured before PersistAsync flips _hadExistingRow/_loadedAsSubmittedEdit semantics --
            // the SEM escalation notification (Functional Spec §7.4) only fires when this is an edit
            // to an already-submitted declaration that now has Part II entries, not a first-time
            // submission (which is expected to carry whatever Part II content it has).
            var isEscalationCandidate = !IsBod && _hadExistingRow && _loadedAsSubmittedEdit && !_nothingConflicts && _conflicts.Count > 0;

            int declarationId;
            try
            {
                declarationId = await PersistAsync(isDraft: false, actorName, isImpersonating);
            }
            catch (SqlException ex) when (ex.Number == 50012)
            {
                Toasts.ShowError("This Related Party & COI declaration was already submitted.");
                await LoadAsync();
                return;
            }

            await AuditLog.LogAsync(actorName, _hadExistingRow ? AuditAction.Update : AuditAction.Create, nameof(RelatedPartyCoiDeclaration), _effectiveMember.Id.ToString(),
                BuildSummary(), actingOnBehalfOf: isImpersonating ? _effectiveMember.FullName : null);

            if (isEscalationCandidate)
            {
                await SendCorporateAffairsEscalationAsync(actorName);
            }

            _submittedAtUtc = DateTime.UtcNow;
            _step = Step.Done;
            Toasts.ShowSuccess($"Related Party & COI declaration {(_hadExistingRow ? "updated" : "submitted")}.");
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
            $"Relatives: {(_nothingRelatives ? "Nothing to declare" : $"{_relatives.Count} declared")}",
            $"Self-owned companies (I.B): {(_nothingSelfOwned ? "Nothing to declare" : $"{_selfOwnedCompanies.Count} declared")}",
            $"Relative-owned companies (I.C): {(_nothingRelativeOwned ? "Nothing to declare" : $"{_relativeOwnedCompanies.Count} declared")}",
            $"Board/executive role companies (I.D): {(_nothingBoardRoles ? "Nothing to declare" : $"{_boardRoleCompanies.Count} declared")}",
            $"Conflicts of interest (Part II): {(_nothingConflicts ? "Nothing to declare" : $"{_conflicts.Count} declared")}",
        };
        return string.Join("; ", parts);
    }

    // No pre-existing "Corporate Affairs" mailbox/config exists in this app -- Administrator is the
    // role that actually operates Corporate Affairs functions here, so the escalation goes to every
    // user holding it.
    private async Task SendCorporateAffairsEscalationAsync(string actorName)
    {
        var recipients = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var user in await UserManager.GetUsersInRoleAsync(GovernanceRoles.Administrator))
        {
            if (!string.IsNullOrWhiteSpace(user.Email)) recipients.Add(user.Email);
        }

        if (recipients.Count == 0)
        {
            await AuditLog.LogAsync(actorName, AuditAction.Notify, nameof(Member), _effectiveMember!.Id.ToString(),
                "SEM Part II update escalation NOT sent: no Administrator recipients found.");
            return;
        }

        var subject = $"RP/COI escalation — new conflict of interest disclosed by {_effectiveMember!.FullName}";
        var body = $"""
            <p>{_effectiveMember.FullName} has updated their Related Party &amp; Conflict of Interest declaration
            (Q{_run!.PeriodQuarter} {_run.PeriodYear}) with a new Part II (Conflict of Interest) entry, as required
            by the SEM ongoing-disclosure commitment.</p>
            <p>Please review their declaration in the Corporate Governance Tool.</p>
            """;

        try
        {
            foreach (var email in recipients)
            {
                await EmailSender.SendAsync(email, subject, body);
            }
            await AuditLog.LogAsync(actorName, AuditAction.Notify, nameof(Member), _effectiveMember.Id.ToString(),
                $"SEM Part II update escalation sent to {string.Join(", ", recipients)}");
        }
        catch (Exception ex)
        {
            await AuditLog.LogAsync(actorName, AuditAction.Notify, nameof(Member), _effectiveMember.Id.ToString(),
                $"SEM Part II update escalation FAILED: {ex.Message}");
        }
    }

    private static string RelationshipLabel(RelativeRelationship relationship) => relationship switch
    {
        RelativeRelationship.InLaws => "In-Laws",
        RelativeRelationship.FatherInLaw => "Father-in-Law",
        RelativeRelationship.MotherInLaw => "Mother-in-Law",
        RelativeRelationship.Stepchildren => "Children of spouse",
        _ => relationship.ToString(),
    };

    private string? RelativeName(Guid? key) => key is null ? null : _relatives.FirstOrDefault(r => r.Key == key)?.Name;
}
