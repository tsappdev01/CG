using System.Security.Claims;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.JSInterop;
using CGTOOL.Web.Data;
using CGTOOL.Web.Data.Governance;

namespace CGTOOL.Web.Components.Pages;

public partial class SubmitRelatedPartyCoiDeclaration
{
    private enum Step { Loading, NoAccess, NotDue, EditWindowClosed, Relatives, SelfOwned, RelativeOwned, BoardRoles, Conflicts, Review, Done }

    /// <summary>The wizard in order, and what the chevron calls each one. Review is the last of
    /// them: the attestation belongs beside the summary it attests to, not half-way up the form.</summary>
    private static readonly (Step Step, string Label)[] WizardSteps =
    [
        (Step.Relatives, "Relatives"),
        (Step.SelfOwned, "My companies"),
        (Step.RelativeOwned, "Relatives' companies"),
        (Step.BoardRoles, "Board roles"),
        (Step.Conflicts, "Conflicts"),
        (Step.Review, "Review & submit"),
    ];

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

    // Every section starts at "nothing to declare". Most members have nothing in most of them, and a
    // section that starts open is a section already claiming something is there -- the answer should
    // be one the member gave. A section the register fills turns itself on below, because rows on
    // screen and "nothing to declare" cannot both be true.
    private bool _nothingRelatives = true;
    private bool _nothingSelfOwned = true;
    private bool _nothingRelativeOwned = true;
    private bool _nothingBoardRoles = true;
    private bool _nothingConflicts = true;

    private readonly List<RelativeRow> _relatives = [];
    private readonly List<CompanyRow> _selfOwnedCompanies = [];
    private readonly List<CompanyRow> _relativeOwnedCompanies = [];
    private readonly List<CompanyRow> _boardRoleCompanies = [];
    private readonly List<ConflictRow> _conflicts = [];

    // My Register (Family Members / Owned Companies) -- the declarant's own reusable reference data,
    // offered here as "pick from My Register" quick-adds so the same relative/company name and
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

        /// <summary>The My Register record this row came from, where it came from one. What makes
        /// an edit here reach the register, and what the delete prompt has to offer to remove.
        /// Null for a row typed straight into the declaration.</summary>
        public int? FamilyMemberId { get; set; }
    }

    public class CompanyRow
    {
        public Guid Key { get; } = Guid.NewGuid();
        public string LegalCompanyName { get; set; } = string.Empty;
        public string PrincipalBusinessActivity { get; set; } = string.Empty;
        public string NatureOfHolding { get; set; } = string.Empty;
        public string TradeLicenseNumber { get; set; } = string.Empty;
        public DateTime? TradeLicenseExpiryDate { get; set; }
        public string LicenseActivities { get; set; } = string.Empty;
        public List<UploadedFileRow> Documents { get; set; } = [];
        public Guid? LinkedRelativeKey { get; set; }
        public bool Uploading { get; set; }
        public int UploadProgress { get; set; }

        /// <summary>Whether the row is open in the form. Collapsed by default -- a section of
        /// companies is a list to read before it is a form to fill -- and never saved: it is how the
        /// member is looking at the declaration, not part of it.</summary>
        public bool Expanded { get; set; }
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
        public string NatureOfInterest { get; set; } = string.Empty;
    }

    public static readonly string[] NatureOfHoldingOptions = ["Owned", "Affiliate", "Subsidiary"];

    protected override async Task OnParametersSetAsync() => await LoadAsync();

    /// <summary>Focus after the render, not during validation: the field may be on a step that was
    /// not on screen when the message was raised, and cannot be focused until it exists.</summary>
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (_focusFieldId is not { } id) return;
        _focusFieldId = null;

        try
        {
            await JS.InvokeVoidAsync("cgFocusField", id);
        }
        catch (JSException)
        {
            // The field went away between the message and the render -- the message still stands.
        }
    }

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
            .Include(f => f.Holdings)
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

        // A new declaration starts from the register rather than from an empty table: these are the
        // same people, and the member has already listed them once.
        if (_run is not null && !_isEditing && _relatives.Count == 0)
        {
            foreach (var familyMember in _myFamilyMembers)
            {
                _relatives.Add(new RelativeRow
                {
                    Name = familyMember.Name,
                    Relationship = familyMember.Relationship,
                    FamilyMemberId = familyMember.Id,
                });
            }

            if (_relatives.Count > 0) _nothingRelatives = false;
        }

        if (_run is not null && !_isEditing)
        {
            AutoLoadCompanySections();
        }

        FillConflictNaturesFromWorkspace();

        _step = _run is null ? Step.NotDue : Step.Relatives;
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

        // The due date bars changing a declaration that was submitted; it does not bar finishing one
        // that never was. Applied to a draft it does the opposite of what it is for: the member who
        // saved their work loses the declaration, while the member who never opened the form can
        // still submit it. Saving a draft must not forfeit the declaration.
        if (!declaration.IsDraft && declaration.DeclarationCycleRun.DueDateUtc.Date < DateTime.UtcNow.Date)
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
            // Matched by name: the declaration stores the answer, not a link to the register, and
            // the link is what lets an edit here reach My Register.
            var linked = _myFamilyMembers.FirstOrDefault(f =>
                string.Equals(f.Name.Trim(), r.Name.Trim(), StringComparison.OrdinalIgnoreCase));

            var row = new RelativeRow { Name = r.Name, Relationship = r.Relationship, FamilyMemberId = linked?.Id };
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
                NatureOfHolding = c.NatureOfHolding ?? string.Empty,
                TradeLicenseNumber = c.TradeLicenseNumber ?? string.Empty,
                TradeLicenseExpiryDate = c.TradeLicenseExpiryDate,
                LicenseActivities = c.LicenseActivities ?? string.Empty,
                LinkedRelativeKey = c.CoiRelativeId is { } rid && relativeRowsByDbId.TryGetValue(rid, out var relRow) ? relRow.Key : null,
            };
            row.Documents.AddRange(c.Documents.Select(doc => new UploadedFileRow { Path = doc.FilePath, FileName = doc.FileName }));
            WithActivitiesFilled(row);

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
            NatureOfInterest = c.NatureOfInterest ?? string.Empty,
        }));
        FillConflictNaturesFromWorkspace();

        _step = Step.Relatives;
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

    /// <summary>Writes a change made here back to the member's register, for a row that came from
    /// it. The two are the same person: correcting a name on the declaration and leaving My
    /// Workspace saying something else would put the mistake back next quarter.</summary>
    // Removing a relative asks how far the removal should reach: out of this declaration, or out of
    // the register as well. Held here while the question is on screen.
    private RelativeRow? _pendingRelativeRemoval;

    private void AskRemoveRelative(RelativeRow row)
    {
        // Nothing to ask about a row that only exists here.
        if (row.FamilyMemberId is null) { RemoveRelative(row); return; }
        _pendingRelativeRemoval = row;
    }

    private void CancelRemoveRelative() => _pendingRelativeRemoval = null;

    private void RemoveRelativeFromDeclarationOnly()
    {
        if (_pendingRelativeRemoval is { } row)
        {
            _pendingRelativeRemoval = null;
            RemoveRelative(row);
        }
    }

    private async Task RemoveRelativeEverywhereAsync()
    {
        if (_pendingRelativeRemoval is not { } row) return;
        _pendingRelativeRemoval = null;

        if (_effectiveMember is not null && row.FamilyMemberId is { } id)
        {
            var familyMember = _myFamilyMembers.FirstOrDefault(f => f.Id == id);
            await FamilyMemberWriter.DeleteAsync(id);
            _myFamilyMembers.RemoveAll(f => f.Id == id);

            var state = await AuthState.GetAuthenticationStateAsync();
            var actorName = state.User.Identity?.Name ?? "unknown";
            await AuditLog.LogAsync(actorName, AuditAction.Delete, nameof(FamilyMember), id.ToString(),
                $"Deleted related party {familyMember?.Name ?? row.Name} ({RelationshipLabel(row.Relationship)}) "
                + "from My Register, via the Related Party & COI declaration.",
                actingOnBehalfOf: Impersonation.ActingMemberId is not null ? _effectiveMember.FullName : null);

            Toasts.ShowSuccess("Removed from this declaration and from My Register.");
        }

        RemoveRelative(row);
    }

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

    private static void AddCompany(List<CompanyRow> list) => list.Add(new CompanyRow { Expanded = true });
    private static void RemoveCompany(List<CompanyRow> list, CompanyRow row) => list.Remove(row);

    // Family members/companies not yet added to this section -- offered in the "pick from My
    // Workspace" dropdowns so the same name/relationship isn't retyped, or the same trade license/MOA/
    // POA re-uploaded, every declaration cycle.
    private List<FamilyMember> AvailableFamilyMembers =>
        [.. _myFamilyMembers.Where(f => !_relatives.Any(r => string.Equals(r.Name, f.Name, StringComparison.OrdinalIgnoreCase)))];

    /// <summary>The declared companies still worth offering for this Part II row. One another row
    /// already names is not a second conflict, so it is dropped -- the list would otherwise keep
    /// offering a company that has just been picked, and a duplicate is easier to make than to
    /// spot. The row's own company stays, since it is what the input already holds.</summary>
    private List<string> ConflictSuggestionsFor(ConflictRow row) =>
    [
        .. AllDeclaredCompanies
            .Select(c => c.LegalCompanyName.Trim())
            .Where(name => name.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(name => !_conflicts.Any(other => !ReferenceEquals(other, row)
                && string.Equals(other.CompanyOrCounterpartyName.Trim(), name, StringComparison.OrdinalIgnoreCase)))
    ];

    private List<OwnedCompany> AvailableCompaniesFor(List<CompanyRow> list) =>
        [.. _myCompanies.Where(c => !list.Any(r => string.Equals(r.LegalCompanyName, c.CompanyName, StringComparison.OrdinalIgnoreCase)))];

    private void AddFromMyFamily()
    {
        if (_pickFamilyMemberId is not { } id) return;
        var familyMember = _myFamilyMembers.FirstOrDefault(f => f.Id == id);
        if (familyMember is null) return;

        _relatives.Add(new RelativeRow
        {
            Name = familyMember.Name,
            Relationship = familyMember.Relationship,
            FamilyMemberId = familyMember.Id,
        });
        _pickFamilyMemberId = null;
    }

    private void AddCompanyFromWorkspace(List<CompanyRow> list, int? companyId)
    {
        if (companyId is not { } id) return;
        var company = _myCompanies.FirstOrDefault(c => c.Id == id);
        if (company is null) return;

        list.Add(RowFor(company));
    }

    private CompanyRow RowFor(OwnedCompany company)
    {
        var row = new CompanyRow
        {
            LegalCompanyName = company.CompanyName,
            PrincipalBusinessActivity = company.PrincipalBusinessActivity ?? string.Empty,
            NatureOfHolding = company.NatureOfHolding == RelatedPartyHoldingNature.None ? string.Empty : company.NatureOfHolding.ToString(),
            TradeLicenseNumber = company.TradeLicenceNumber ?? string.Empty,
            TradeLicenseExpiryDate = company.TradeLicenceExpiryDate,
        };
        AddWorkspaceDocument(row, company.TradeLicensePath, "Trade License");
        AddWorkspaceDocument(row, company.MoaPath, "MOA");
        AddWorkspaceDocument(row, company.PoaPath, "POA");
        return WithActivitiesFilled(row);
    }

    // ---------- the three company sections and My Register ----------

    /// <summary>The threshold the two ownership sections are asking about.</summary>
    private const decimal DeclarableOwnershipPercentage = 30m;

    /// <summary>Each section's question, answered against the register rather than asked again:
    /// companies the member owns at least 30% of, companies a relative owns at least 30% of, and
    /// companies the member sits on the board of or runs. Where the register says yes, the section
    /// is turned on and filled; the member can still turn it off, and turning it off clears what
    /// was filled in so the answer and the rows never disagree.</summary>
    private void AutoLoadCompanySections()
    {
        if (FillSelfOwned()) _nothingSelfOwned = false;
        if (FillBoardRoles()) _nothingBoardRoles = false;
        if (FillRelativeOwned()) _nothingRelativeOwned = false;
    }

    /// <summary>The section's own toggle. Switching it off clears the section; switching it back on
    /// fills it from the register again, because the member has just said the section applies after
    /// all -- and the register is where its rows come from. Without this the answer is destructive:
    /// one accidental tap empties the section and nothing brings it back but reloading the page.
    ///
    /// Rows typed in by hand are gone either way. They were cleared by the answer that said this
    /// section has nothing in it, and there is nowhere to read them back from.</summary>
    private void SetNothingSelfOwned()
    {
        if (_nothingSelfOwned) ClearSection(_selfOwnedCompanies); else FillSelfOwned();
    }

    private void SetNothingRelativeOwned()
    {
        if (_nothingRelativeOwned) ClearSection(_relativeOwnedCompanies); else FillRelativeOwned();
    }

    private void SetNothingBoardRoles()
    {
        if (_nothingBoardRoles) ClearSection(_boardRoleCompanies); else FillBoardRoles();
    }

    private bool FillSelfOwned() => FillSection(
        _selfOwnedCompanies,
        _myCompanies.Where(c => c.OwnershipPercentage >= DeclarableOwnershipPercentage).ToList());

    private bool FillBoardRoles() => FillSection(
        _boardRoleCompanies,
        _myCompanies.Where(c => c.ServesAsBoardMemberOrExecutive).ToList());

    /// <summary>A relative's company is recorded on the relative -- the organization they hold, and
    /// how much of it -- so this section is built from the relatives, not from My Companies. The row
    /// is linked to that relative where the declaration already lists them.
    ///
    /// Every qualifying holding, not one per relative: a relative with two companies over the
    /// threshold has two to declare, and the register records both.</summary>
    private bool FillRelativeOwned()
    {
        var relativeOwned = _myFamilyMembers
            .SelectMany(f => f.Holdings
                .Where(h => h.OwnershipPercentage >= DeclarableOwnershipPercentage
                            && !string.IsNullOrWhiteSpace(h.CompanyName))
                .Select(h => (Relative: f, Holding: h)))
            .ToList();

        foreach (var (relative, holding) in relativeOwned)
        {
            if (_relativeOwnedCompanies.Any(r =>
                    string.Equals(r.LegalCompanyName.Trim(), holding.CompanyName.Trim(), StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            // Everything the register holds about the holding, so the declarant confirms it rather
            // than retyping it: the activity, the licence's number, expiry and permitted activities,
            // and the licence document itself.
            var row = new CompanyRow
            {
                LegalCompanyName = holding.CompanyName.Trim(),
                PrincipalBusinessActivity = holding.PrincipalBusinessActivity ?? string.Empty,
                NatureOfHolding = holding.NatureOfHolding == RelatedPartyHoldingNature.None ? string.Empty : holding.NatureOfHolding.ToString(),
                TradeLicenseNumber = holding.TradeLicenceNumber ?? string.Empty,
                TradeLicenseExpiryDate = holding.TradeLicenceExpiryDate,
                LicenseActivities = holding.LicenceActivities ?? string.Empty,
                LinkedRelativeKey = _relatives
                    .FirstOrDefault(r => string.Equals(r.Name.Trim(), relative.Name.Trim(), StringComparison.OrdinalIgnoreCase))?.Key,
            };
            AddWorkspaceDocument(row, holding.TradeLicencePath, "Trade License");
            _relativeOwnedCompanies.Add(WithActivitiesFilled(row));
        }

        return relativeOwned.Count > 0;
    }

    /// <summary>Adds the register's matches that the section does not already list, and says whether
    /// the register had any -- which is what decides the section's answer on first load.</summary>
    private bool FillSection(List<CompanyRow> list, List<OwnedCompany> matches)
    {
        foreach (var company in matches)
        {
            if (list.Any(r => string.Equals(r.LegalCompanyName.Trim(), company.CompanyName.Trim(), StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            list.Add(RowFor(company));
        }

        return matches.Count > 0;
    }

    /// <summary>Answering "nothing to declare" empties the section. Leaving the rows behind would
    /// submit a section that says nothing is declared and lists three companies; and the member who
    /// turns it off has just said the loaded rows do not belong there.</summary>
    private static void ClearSection(List<CompanyRow> list)
    {
        foreach (var row in list.Where(r => r.LinkedRelativeKey is not null).ToList())
        {
            row.LinkedRelativeKey = null;
        }
        list.Clear();
    }

    private static void AddWorkspaceDocument(CompanyRow row, string? path, string label)
    {
        if (string.IsNullOrEmpty(path)) return;
        row.Documents.Add(new UploadedFileRow { Path = path, FileName = $"{label} (from My Register)" });
    }

    /// <summary>Answers the rows that were entered before the register held the answer, or saved
    /// while Part II still asked for it separately. Only the blank ones: what the member typed is
    /// their answer, and the register does not overrule it.</summary>
    private void FillConflictNaturesFromWorkspace()
    {
        foreach (var row in _conflicts)
        {
            if (string.IsNullOrWhiteSpace(row.NatureOfHolding)
                && HoldingNatureFromWorkspace(row.CompanyOrCounterpartyName) is { } nature)
            {
                row.NatureOfHolding = nature;
            }
            if (string.IsNullOrWhiteSpace(row.NatureOfInterest)
                && InterestFromWorkspace(row.CompanyOrCounterpartyName) is { } interest)
            {
                row.NatureOfInterest = interest;
            }
        }
    }

    // ---------- Part II corrections, offered back to My Register ----------

    /// <summary>One field the declaration now states differently from the register.</summary>
    private sealed record RegisterChange(string Field, string? Before, string? After);

    /// <summary>One record the declaration now describes differently from the register -- a relative
    /// or a company. Carries the write itself, so the dialog can offer relatives and companies the
    /// same way without knowing what either is.</summary>
    private sealed record RegisterUpdate(string Title, List<RegisterChange> Changes, Func<Task> ApplyAsync)
    {
        /// <summary>Identifies the answer that was declined, so declining is remembered for the
        /// values declined and changing them again asks afresh.</summary>
        public string Fingerprint => $"{Title}|" + string.Join("|", Changes.Select(c => $"{c.Field}={c.After}"));
    }

    private List<RegisterUpdate>? _pendingWorkspaceUpdates;
    private Step? _stepAfterWorkspacePrompt;

    /// <summary>What the member has already said no to, and for which values. Keyed that way so
    /// declining is remembered for the answer given, and changing the figures again asks afresh
    /// rather than being silently taken as the same refusal.</summary>
    private readonly HashSet<string> _declinedWorkspaceUpdates = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The relatives this declaration now names differently from the register. Only the ones
    /// that came from it: a relative typed in here is offered to the register separately, on save.</summary>
    private List<RegisterUpdate> PendingRelativeUpdates()
    {
        var updates = new List<RegisterUpdate>();
        if (_nothingRelatives) return updates;

        foreach (var row in _relatives)
        {
            if (row.FamilyMemberId is not { } id) continue;

            var familyMember = _myFamilyMembers.FirstOrDefault(f => f.Id == id);
            if (familyMember is null) continue;

            var name = row.Name.Trim();
            if (name.Length == 0) continue;

            var changes = new List<RegisterChange>();
            if (!string.Equals(familyMember.Name.Trim(), name, StringComparison.Ordinal))
            {
                changes.Add(new RegisterChange("Name", familyMember.Name, name));
            }
            if (familyMember.Relationship != row.Relationship)
            {
                changes.Add(new RegisterChange("Relationship",
                    RelationshipLabel(familyMember.Relationship), RelationshipLabel(row.Relationship)));
            }

            if (changes.Count == 0) continue;

            var target = familyMember;
            var newName = name;
            var newRelationship = row.Relationship;
            updates.Add(new RegisterUpdate(familyMember.Name, changes, async () =>
            {
                target.Name = newName;
                target.Relationship = newRelationship;
                await FamilyMemberWriter.UpdateAsync(target);
                await LogRegisterUpdateAsync(nameof(FamilyMember), target.Id,
                    $"related party {newName}", changes);
            }));
        }

        return updates;
    }

    /// <summary>Every Part II row whose company the register knows and now describes differently.
    ///
    /// A cleared field is not a change to carry over: blanking an activity here says this
    /// declaration does not state it, not that the register should forget it.</summary>
    private List<RegisterUpdate> PendingCompanyUpdates()
    {
        var updates = new List<RegisterUpdate>();
        if (_nothingConflicts) return updates;

        foreach (var row in _conflicts)
        {
            var name = row.CompanyOrCounterpartyName.Trim();
            if (name.Length == 0) continue;

            var (company, holding) = CompanyInRegister(name);
            if (company is null && holding is null) continue;

            var beforeActivity = company?.PrincipalBusinessActivity ?? holding?.PrincipalBusinessActivity;
            var beforeNature = company?.NatureOfHolding ?? holding!.NatureOfHolding;
            var beforeInterest = company?.NatureOfInterest ?? holding?.NatureOfInterest;

            var afterActivity = Entered(row.PrincipalBusinessActivity);
            var afterNature = ParseHoldingNature(row.NatureOfHolding) ?? beforeNature;
            var afterInterest = Entered(row.NatureOfInterest);

            var changes = new List<RegisterChange>();
            if (afterActivity is not null && !string.Equals(beforeActivity?.Trim(), afterActivity, StringComparison.Ordinal))
            {
                changes.Add(new RegisterChange("Principal Business Activity", beforeActivity, afterActivity));
            }
            if (afterNature != beforeNature)
            {
                changes.Add(new RegisterChange("Nature of Holding",
                    beforeNature == RelatedPartyHoldingNature.None ? null : HoldingNatureLabel(beforeNature),
                    HoldingNatureLabel(afterNature)));
            }
            if (afterInterest is not null && !string.Equals(beforeInterest?.Trim(), afterInterest, StringComparison.Ordinal))
            {
                changes.Add(new RegisterChange("Nature of Interest", beforeInterest, afterInterest));
            }

            if (changes.Count == 0) continue;

            var activity = changes.Any(c => c.Field == "Principal Business Activity") ? afterActivity : null;
            var interest = changes.Any(c => c.Field == "Nature of Interest") ? afterInterest : null;
            updates.Add(new RegisterUpdate(name, changes, async () =>
            {
                if (company is not null)
                {
                    if (activity is not null) company.PrincipalBusinessActivity = activity;
                    if (interest is not null) company.NatureOfInterest = interest;
                    company.NatureOfHolding = afterNature;
                    await CompanyWriter.UpdateAsync(company);
                    await LogRegisterUpdateAsync(nameof(OwnedCompany), company.Id, name, changes);
                }
                else
                {
                    if (activity is not null) holding!.PrincipalBusinessActivity = activity;
                    if (interest is not null) holding!.NatureOfInterest = interest;
                    holding!.NatureOfHolding = afterNature;
                    await HoldingWriter.UpdateAsync(holding);
                    await LogRegisterUpdateAsync(nameof(FamilyMemberHolding), holding.Id, name, changes);
                }
            }));
        }

        return updates;
    }

    /// <summary>What the member actually put in a box, or null for a blank -- which says this
    /// declaration does not state it, never that the register should forget what it holds.</summary>
    private static string? Entered(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static RelatedPartyHoldingNature? ParseHoldingNature(string? value) =>
        Enum.TryParse<RelatedPartyHoldingNature>(value, ignoreCase: true, out var nature)
            && nature != RelatedPartyHoldingNature.None
                ? nature
                : null;

    /// <summary>A value the register does not hold yet, said out loud -- an empty cell beside an
    /// arrow reads as a rendering fault rather than as "there was nothing here".</summary>
    private static string BlankAware(string? value) => string.IsNullOrWhiteSpace(value) ? "Not recorded" : value.Trim();

    /// <summary>A value being replaced is struck through; "Not recorded" is not a value, and struck
    /// through it reads as though something had been deleted.</summary>
    private static string BeforeClass(bool nothingThere) => nothingThere ? "cg-ws-none" : "cg-ws-before";

    private static string Quote(string? value) => string.IsNullOrWhiteSpace(value) ? "(blank)" : $"“{value.Trim()}”";

    private static string HoldingNatureLabel(RelatedPartyHoldingNature nature) =>
        nature == RelatedPartyHoldingNature.None ? "(not set)" : nature.ToString();

    /// <summary>Offers what has changed to the register, if anything has, and holds the move to
    /// <paramref name="target"/> until the member answers. Returns false when nothing was asked, so
    /// the caller carries on.</summary>
    private bool OfferRegisterUpdates(List<RegisterUpdate> updates, Step target)
    {
        var unanswered = updates.Where(u => !_declinedWorkspaceUpdates.Contains(u.Fingerprint)).ToList();
        if (unanswered.Count == 0) return false;

        _pendingWorkspaceUpdates = unanswered;
        _stepAfterWorkspacePrompt = target;
        return true;
    }

    private async Task LogRegisterUpdateAsync(string entity, int id, string what, List<RegisterChange> changes)
    {
        var state = await AuthState.GetAuthenticationStateAsync();
        var actorName = state.User.Identity?.Name ?? "unknown";

        var described = string.Join("; ", changes.Select(c => $"{c.Field} {Quote(c.Before)} → {Quote(c.After)}"));
        await AuditLog.LogAsync(actorName, AuditAction.Update, entity, id.ToString(),
            $"Updated {what} in My Register from the Related Party & COI declaration: {described}.",
            actingOnBehalfOf: Impersonation.ActingMemberId is not null ? _effectiveMember?.FullName : null);
    }

    private async Task ConfirmWorkspaceUpdatesAsync()
    {
        var updates = _pendingWorkspaceUpdates ?? [];
        _pendingWorkspaceUpdates = null;

        foreach (var update in updates)
        {
            await update.ApplyAsync();
        }

        Toasts.ShowSuccess(updates.Count == 1
            ? $"{updates[0].Title} updated in My Register."
            : $"{updates.Count} records updated in My Register.");

        ResumeAfterWorkspacePrompt();
    }

    private void DeclineWorkspaceUpdates()
    {
        foreach (var update in _pendingWorkspaceUpdates ?? [])
        {
            _declinedWorkspaceUpdates.Add(update.Fingerprint);
        }

        _pendingWorkspaceUpdates = null;
        ResumeAfterWorkspacePrompt();
    }

    /// <summary>Carries on to the step the member was heading for. Going back through GoToStep means
    /// the confirmation before Review still happens, in its usual order.</summary>
    private void ResumeAfterWorkspacePrompt()
    {
        var target = _stepAfterWorkspacePrompt;
        _stepAfterWorkspacePrompt = null;
        if (target is { } step) GoToStep(step);
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

        if (HoldingNatureFromWorkspace(value) is { } nature)
        {
            row.NatureOfHolding = nature;
        }
        if (InterestFromWorkspace(value) is { } interest)
        {
            row.NatureOfInterest = interest;
        }
    }

    /// <summary>What the register says the member's interest in this company is. It is recorded once
    /// in My Register -- on the company for their own, on the holding for a relative's -- so Part II
    /// reads it from there rather than asking for the same answer a second time. It stays editable:
    /// the register says how the interest is held, and Part II is where the member says so for this
    /// declaration.</summary>
    private string? HoldingNatureFromWorkspace(string companyName)
    {
        var nature = CompanyInRegister(companyName) switch
        {
            (OwnedCompany company, _) => company.NatureOfHolding,
            (_, FamilyMemberHolding holding) => holding.NatureOfHolding,
            _ => (RelatedPartyHoldingNature?)null,
        };

        // "None" is the register saying it was never answered, not an answer to copy over.
        return nature is null or RelatedPartyHoldingNature.None ? null : nature.ToString();
    }

    /// <summary>What the register says the interest in this company is, read on the same match.</summary>
    private string? InterestFromWorkspace(string companyName)
    {
        var interest = CompanyInRegister(companyName) switch
        {
            (OwnedCompany company, _) => company.NatureOfInterest,
            (_, FamilyMemberHolding holding) => holding.NatureOfInterest,
            _ => null,
        };

        return string.IsNullOrWhiteSpace(interest) ? null : interest.Trim();
    }

    /// <summary>The register's record of a company by name -- the member's own first, then a
    /// relative's holding. One lookup, so everything read off the register agrees about which row it
    /// came from.</summary>
    private (OwnedCompany? Company, FamilyMemberHolding? Holding) CompanyInRegister(string companyName)
    {
        var name = companyName.Trim();
        if (name.Length == 0) return (null, null);

        var company = _myCompanies.FirstOrDefault(c => string.Equals(c.CompanyName.Trim(), name, StringComparison.OrdinalIgnoreCase));
        if (company is not null) return (company, null);

        return (null, _myFamilyMembers
            .SelectMany(f => f.Holdings)
            .FirstOrDefault(h => string.Equals(h.CompanyName.Trim(), name, StringComparison.OrdinalIgnoreCase)));
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

    // ---------- moving between steps ----------

    private static int IndexOfStep(Step step) => Array.FindIndex(WizardSteps, w => w.Step == step);

    private bool IsWizardStep => IndexOfStep(_step) >= 0;

    private Step? PreviousStep => IndexOfStep(_step) is var i && i > 0 ? WizardSteps[i - 1].Step : null;

    private Step? NextStep => IndexOfStep(_step) is var i && i >= 0 && i < WizardSteps.Length - 1
        ? WizardSteps[i + 1].Step
        : null;

    private string StepClass(Step step)
    {
        var (here, current) = (IndexOfStep(step), IndexOfStep(_step));
        return here == current ? "is-current" : here < current ? "is-done" : string.Empty;
    }

    private string RuleClass(int afterIndex) => IndexOfStep(_step) > afterIndex ? "is-done" : string.Empty;

    /// <summary>Going back is free -- the member is re-reading what they already entered. Going
    /// forward checks every step being skipped over, so a chevron cannot be used to jump past a
    /// section that is not filled in. Reaching Review goes through the same confirmation the single
    /// long form used.</summary>
    private void GoToStep(Step target)
    {
        var (from, to) = (IndexOfStep(_step), IndexOfStep(target));
        if (to < 0 || from < 0 || to == from) return;

        if (to > from)
        {
            for (var i = from; i < to; i++)
            {
                if (ValidateStep(WizardSteps[i].Step)) continue;

                // Land on the step that refused, not the one they were on: the message names a field,
                // and a field they cannot see is a field they cannot fix.
                _step = WizardSteps[i].Step;
                return;
            }

            // Leaving a step behind: what was corrected on it may be a correction to the register,
            // and this is the last moment the member is looking at it. Asked here rather than as
            // they type, so a dialog does not interrupt every field they tab out of.
            var relativesIndex = IndexOfStep(Step.Relatives);
            if (from <= relativesIndex && to > relativesIndex && OfferRegisterUpdates(PendingRelativeUpdates(), target))
            {
                return;
            }

            var conflictsIndex = IndexOfStep(Step.Conflicts);
            if (from <= conflictsIndex && to > conflictsIndex && OfferRegisterUpdates(PendingCompanyUpdates(), target))
            {
                return;
            }

            if (target == Step.Review)
            {
                _pendingFormConfirm = true;
                return;
            }
        }

        _step = target;
    }

    // ---------- putting the cursor where the message points ----------

    public static string CompanyFieldId(CompanyRow row, string field) => $"coi-company-{row.Key:N}-{field}";

    private static string RelativeFieldId(RelativeRow row, string field) => $"coi-relative-{row.Key:N}-{field}";

    private static string ConflictFieldId(ConflictRow row, string field) => $"coi-conflict-{row.Key:N}-{field}";

    /// <summary>The field the last refused step was about, focused once the browser has rendered the
    /// step it lives on.</summary>
    private string? _focusFieldId;

    /// <summary>Refuses a step, saying why and remembering which box to put the cursor in. Every
    /// refusal goes through here so no message is left pointing at a field the member has to hunt
    /// for -- on a step they may not even be looking at.</summary>
    /// <summary>Refuses on a company row, opening it first: the field the message names is inside,
    /// and a cursor in a collapsed row points at nothing.</summary>
    private bool RefuseRow(CompanyRow row, string message, string field)
    {
        row.Expanded = true;
        return Refuse(message, CompanyFieldId(row, field));
    }

    private bool Refuse(string message, string fieldId)
    {
        Toasts.ShowError(message);
        _focusFieldId = fieldId;
        return false;
    }

    private bool ValidateStep(Step step) => step switch
    {
        Step.Relatives => ValidateRelatives(),
        Step.SelfOwned => ValidateCompanySection(_selfOwnedCompanies, _nothingSelfOwned, "Companies you own ≥30%"),
        Step.RelativeOwned => ValidateCompanySection(_relativeOwnedCompanies, _nothingRelativeOwned, "Companies a relative owns ≥30%", requireLinkedRelative: true),
        Step.BoardRoles => ValidateCompanySection(_boardRoleCompanies, _nothingBoardRoles, "Companies where you are a board member/senior executive"),
        Step.Conflicts => ValidateConflicts(),
        _ => true,
    };

    private bool ValidateRelatives()
    {
        if (_nothingRelatives) return true;

        if (_relatives.Count == 0)
        {
            Toasts.ShowError("Add at least one relative in List of Relatives, or answer \"nothing to declare\".");
            return false;
        }
        if (_relatives.FirstOrDefault(r => string.IsNullOrWhiteSpace(r.Name)) is { } unnamed)
        {
            return Refuse("Enter a name for every relative in List of Relatives.", RelativeFieldId(unnamed, "name"));
        }
        return true;
    }

    private bool ValidateConflicts()
    {
        if (!_nothingConflicts && _conflicts.Count == 0)
        {
            Toasts.ShowError("Add at least one entry in Conflict of Interest, or answer \"nothing to declare\".");
            return false;
        }
        if (_conflicts.FirstOrDefault(c => string.IsNullOrWhiteSpace(c.CompanyOrCounterpartyName)) is { } unnamed)
        {
            return Refuse("Enter the company or counterparty name for every Conflict of Interest row.",
                ConflictFieldId(unnamed, "name"));
        }
        if (_conflicts.FirstOrDefault(c => string.IsNullOrWhiteSpace(c.NatureOfHolding)) is { } noNature)
        {
            return Refuse("Select the nature of holding for every Conflict of Interest row.",
                ConflictFieldId(noNature, "nature"));
        }
        return true;
    }

    /// <summary>Names the row a message is about, so it reads as being about one company rather than
    /// about the section as a whole.</summary>
    private static string RowLabel(CompanyRow row, string sectionLabel) =>
        string.IsNullOrWhiteSpace(row.LegalCompanyName) ? $"every row in {sectionLabel}" : row.LegalCompanyName.Trim();

    /// <summary>Everything the declaration needs before it can be submitted. The attestation is part
    /// of it, and is entered on the last step -- so this runs there, never on the way forward.</summary>
    private bool ValidateForm()
    {
        foreach (var wizardStep in WizardSteps)
        {
            if (ValidateStep(wizardStep.Step)) continue;

            // Called from Review, where the offending field is a step or four back. Go to it, the
            // same as refusing a Next would.
            _step = wizardStep.Step;
            return false;
        }

        if (string.IsNullOrWhiteSpace(_attestationName))
        {
            Toasts.ShowError("Enter your name to attest this declaration.");
            return false;
        }

        return true;
    }

    // Submission validation: every I.B/I.C/I.D row must name the company, its principal business
    // activity, how the interest is held, and the trade license number before the declaration can
    // move from Draft to Submitted. Draft save (SaveDraftAsync) skips all of this so declarants
    // aren't blocked mid-entry.
    //
    // The expiry date, license activities and an uploaded document were required too (Functional
    // Spec §5.4/§7.8) and are not any more, on the 27-Sep-2026 instruction naming the four above as
    // the mandatory set. They are still captured, still pre-filled from My Register, and still shown
    // on the report -- a row can now be submitted without them.
    /// <summary>On most licences the permitted activities are the business activity said again, so a
    /// blank one is filled from it -- on screen, as the row is built, not silently at submission.
    /// Only ever a blank: what the member read off the licence is what the licence says.</summary>
    private static CompanyRow WithActivitiesFilled(CompanyRow row)
    {
        if (string.IsNullOrWhiteSpace(row.LicenseActivities) && !string.IsNullOrWhiteSpace(row.PrincipalBusinessActivity))
        {
            row.LicenseActivities = row.PrincipalBusinessActivity.Trim();
        }
        return row;
    }

    /// <summary>The same rule over a whole section, for rows that were built before it applied.</summary>
    private static void FillBlankActivities(List<CompanyRow> rows)
    {
        foreach (var row in rows) WithActivitiesFilled(row);
    }

    private bool ValidateCompanySection(List<CompanyRow> rows, bool nothingToDeclare, string sectionLabel, bool requireLinkedRelative = false)
    {
        if (nothingToDeclare) return true;

        FillBlankActivities(rows);

        if (rows.Count == 0)
        {
            Toasts.ShowError($"Add at least one company in {sectionLabel}, or check \"I have nothing to declare\".");
            return false;
        }

        foreach (var row in rows)
        {
            if (string.IsNullOrWhiteSpace(row.LegalCompanyName))
            {
                return RefuseRow(row, $"Enter the legal company name for every row in {sectionLabel}.", "name");
            }
            if (requireLinkedRelative && row.LinkedRelativeKey is null)
            {
                return RefuseRow(row, $"Select which relative owns each company in {sectionLabel}.", "relative");
            }

            // Named one at a time: the message says what to do, and the cursor is already in the
            // box to do it in.
            if (string.IsNullOrWhiteSpace(row.PrincipalBusinessActivity))
            {
                return RefuseRow(row, $"Enter the principal business activity for {RowLabel(row, sectionLabel)}.", "activity");
            }
            if (string.IsNullOrWhiteSpace(row.NatureOfHolding))
            {
                return RefuseRow(row, $"Select the nature of holding for {RowLabel(row, sectionLabel)}.", "nature");
            }
            if (string.IsNullOrWhiteSpace(row.TradeLicenseNumber))
            {
                return RefuseRow(row, $"Enter the trade license number for {RowLabel(row, sectionLabel)}.", "licence");
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
                        NatureOfInterest = string.IsNullOrWhiteSpace(c.NatureOfInterest) ? null : c.NatureOfInterest.Trim(),
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
                NatureOfHolding = string.IsNullOrWhiteSpace(row.NatureOfHolding) ? null : row.NatureOfHolding.Trim(),
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
            $"Conflicts of interest: {(_nothingConflicts ? "Nothing to declare" : $"{_conflicts.Count} declared")}",
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
            (Q{_run!.PeriodQuarter} {_run.PeriodYear}) with a new Conflict of Interest entry, as required
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
