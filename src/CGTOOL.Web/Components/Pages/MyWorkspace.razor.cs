using System.Security.Claims;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.EntityFrameworkCore;
using CGTOOL.Web.Data;
using CGTOOL.Web.Data.Governance;

namespace CGTOOL.Web.Components.Pages;

public partial class MyWorkspace : ComponentBase
{
    [Inject] private IDbContextFactory<ApplicationDbContext> DbFactory { get; set; } = default!;
    [Inject] private IFamilyMemberWriter FamilyMemberWriter { get; set; } = default!;
    [Inject] private IOwnedCompanyWriter CompanyWriter { get; set; } = default!;
    [Inject] private IAuditLogger AuditLog { get; set; } = default!;
    [Inject] private AuthenticationStateProvider AuthState { get; set; } = default!;
    [Inject] private ImpersonationContext Impersonation { get; set; } = default!;
    [Inject] private ToastService Toasts { get; set; } = default!;
    [Inject] private IWebHostEnvironment Env { get; set; } = default!;
    [Inject] private IDocumentIntelligenceService DocIntel { get; set; } = default!;

    private bool _loaded;

    /// <summary>Separates "still loading" from "this login has no member record". Without it, a
    /// login that is not linked to a Member -- the built-in setup account, for one -- sat on the
    /// loading message for ever, which reads as a page that never finishes loading.</summary>
    private Member? _effectiveMember;

    private readonly List<FamilyMember> _familyMembers = [];
    private readonly List<OwnedCompany> _companies = [];

    // Relatives table: search, relation filter and paging.
    private string _search = string.Empty;
    private RelativeRelationship? _relationFilter;
    private int _page = 1;
    private const int RelativesPageSize = 10;

    private const long MaxIdUploadBytes = 1 * 1024 * 1024;
    private const long MaxCompanyDocUploadBytes = 10 * 1024 * 1024;

    private readonly HashSet<(int Id, string Kind)> _uploadingDocs = [];

    private bool IsUploading(int id, string kind) => _uploadingDocs.Contains((id, kind));

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

    protected override async Task OnInitializedAsync()
    {
        try { await LoadAsync(); }
        finally { _loaded = true; }
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
            _effectiveMember = await db.Members.FirstOrDefaultAsync(m => m.Id == actingId);
        }
        else
        {
            var userId = state.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (userId is not null)
            {
                _effectiveMember = await db.Members.FirstOrDefaultAsync(m => m.ApplicationUserId == userId);
            }
        }

        if (_effectiveMember is null) return;

        _familyMembers.Clear();
        _familyMembers.AddRange(await db.FamilyMembers.AsNoTracking()
            .Where(f => f.MemberId == _effectiveMember.Id)
            .OrderBy(f => f.Id)
            .ToListAsync());

        _companies.Clear();
        _companies.AddRange(await db.OwnedCompanies.AsNoTracking()
            .Where(c => c.MemberId == _effectiveMember.Id)
            .OrderBy(c => c.Id)
            .ToListAsync());
    }

    private IEnumerable<FamilyMember> FilteredRelatives()
    {
        IEnumerable<FamilyMember> query = _familyMembers;

        if (_relationFilter is { } relation)
        {
            query = query.Where(f => f.Relationship == relation);
        }

        if (!string.IsNullOrWhiteSpace(_search))
        {
            var q = _search.Trim();
            query = query.Where(f =>
                f.Name.Contains(q, StringComparison.OrdinalIgnoreCase)
                || (f.Organization?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false)
                || (f.IdentificationNumber?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false)
                || (f.EmiratesIdNumber?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false)
                || (f.PassportNumber?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false)
                || (f.Occupation?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false));
        }

        return query;
    }

    private List<FamilyMember> PagedRelatives() =>
        FilteredRelatives().Skip((_page - 1) * RelativesPageSize).Take(RelativesPageSize).ToList();

    private int RelativesTotal => FilteredRelatives().Count();

    private int RelativesPages => Math.Max(1, (int)Math.Ceiling(RelativesTotal / (double)RelativesPageSize));

    private int FirstRelativeOnPage => RelativesTotal == 0 ? 0 : ((_page - 1) * RelativesPageSize) + 1;

    private int LastRelativeOnPage => Math.Min(_page * RelativesPageSize, RelativesTotal);

    private void ResetRelativesPage() => _page = 1;

    private void GoToRelativesPage(int page) => _page = Math.Clamp(page, 1, RelativesPages);

    private static string RelationLabel(RelativeRelationship relationship) =>
        RelativeOptions.FirstOrDefault(o => o.Value == relationship).Label ?? relationship.ToString();

    /// <summary>What the "Emirates ID / Passport" column shows. The typed IdentificationNumber is no
    /// longer editable -- the number now comes off the uploaded document -- but rows captured before
    /// that still carry one, so it stays as the last fallback rather than having those rows go
    /// blank.</summary>
    private static string? IdentifierOf(FamilyMember f)
    {
        if (!string.IsNullOrWhiteSpace(f.EmiratesIdNumber)) return f.EmiratesIdNumber;
        if (!string.IsNullOrWhiteSpace(f.PassportNumber)) return f.PassportNumber;
        return string.IsNullOrWhiteSpace(f.IdentificationNumber) ? null : f.IdentificationNumber;
    }

    private static string HoldingLabel(RelatedPartyHoldingNature nature) =>
        nature == RelatedPartyHoldingNature.None ? "—" : nature.ToString();

    private async Task<string> CurrentActorNameAsync()
    {
        var state = await AuthState.GetAuthenticationStateAsync();
        return state.User.Identity?.Name ?? "unknown";
    }

    // Add and edit both run through a dialog: _*Target is the row in the grid (null while adding),
    // _*Form is the working copy the dialog binds to, and _*Before is the snapshot the audit entry
    // is diffed against. Binding the dialog straight to the grid row would leave half-typed edits
    // on screen after a cancel, and would leave nothing to diff against.
    private bool _relativesOpen = true;
    private bool _companiesOpen = true;

    // The occupation picker binds to these rather than to the entity: the entity keeps one column,
    // and "Other" plus what was typed is only a way of filling it in.
    private string _occupationChoice = string.Empty;
    private string _occupationOther = string.Empty;

    // Delete asks first, and the record is held here rather than deleted from the grid's own
    // iteration: the button that starts it is inside the loop over that list.
    /// <summary>A record already on file that the one being saved looks like. Held while the
    /// warning is on screen; cleared when the dialog is opened, closed, or overridden.</summary>
    private sealed record DuplicateMatch(FamilyMember Existing, string Field, string Value);

    private List<DuplicateMatch> _duplicateMatches = [];
    private bool _duplicateAcknowledged;

    private FamilyMember? _pendingDeleteRelative;
    private OwnedCompany? _pendingDeleteCompany;

    private bool _relativeDialogOpen;
    private FamilyMember? _relativeForm;
    private FamilyMember? _relativeTarget;
    private FamilyMember? _relativeBefore;

    private bool _companyDialogOpen;
    private OwnedCompany? _companyForm;
    private OwnedCompany? _companyTarget;
    private OwnedCompany? _companyBefore;

    private static FamilyMember CopyOf(FamilyMember f) => new()
    {
        Id = f.Id,
        MemberId = f.MemberId,
        Name = f.Name,
        Relationship = f.Relationship,
        IdentificationNumber = f.IdentificationNumber,
        NinNumber = f.NinNumber,
        DateOfBirth = f.DateOfBirth,
        Nationality = f.Nationality,
        Occupation = f.Occupation,
        Organization = f.Organization,
        NatureOfHolding = f.NatureOfHolding,
        OwnershipPercentage = f.OwnershipPercentage,
        EmiratesIdPath = f.EmiratesIdPath,
        EmiratesIdNumber = f.EmiratesIdNumber,
        EmiratesIdExpiryDate = f.EmiratesIdExpiryDate,
        PassportPath = f.PassportPath,
        PassportNumber = f.PassportNumber,
        PassportExpiryDate = f.PassportExpiryDate,
        TradeLicencePath = f.TradeLicencePath,
        TradeLicenceNumber = f.TradeLicenceNumber,
        TradeLicenceLegalName = f.TradeLicenceLegalName,
        TradeLicenceExpiryDate = f.TradeLicenceExpiryDate,
    };

    private static void CopyInto(FamilyMember from, FamilyMember to)
    {
        to.Name = from.Name;
        to.Relationship = from.Relationship;
        to.IdentificationNumber = from.IdentificationNumber;
        to.NinNumber = from.NinNumber;
        to.DateOfBirth = from.DateOfBirth;
        to.Nationality = from.Nationality;
        to.Occupation = from.Occupation;
        to.Organization = from.Organization;
        to.NatureOfHolding = from.NatureOfHolding;
        to.OwnershipPercentage = from.OwnershipPercentage;
        to.EmiratesIdPath = from.EmiratesIdPath;
        to.EmiratesIdNumber = from.EmiratesIdNumber;
        to.EmiratesIdExpiryDate = from.EmiratesIdExpiryDate;
        to.PassportPath = from.PassportPath;
        to.PassportNumber = from.PassportNumber;
        to.PassportExpiryDate = from.PassportExpiryDate;
        to.TradeLicencePath = from.TradeLicencePath;
        to.TradeLicenceNumber = from.TradeLicenceNumber;
        to.TradeLicenceLegalName = from.TradeLicenceLegalName;
        to.TradeLicenceExpiryDate = from.TradeLicenceExpiryDate;
    }

    private static OwnedCompany CopyOf(OwnedCompany c) => new()
    {
        Id = c.Id,
        MemberId = c.MemberId,
        CompanyName = c.CompanyName,
        TradeLicenseDetails = c.TradeLicenseDetails,
        NatureOfHolding = c.NatureOfHolding,
        OwnershipPercentage = c.OwnershipPercentage,
        TradeLicensePath = c.TradeLicensePath,
        MoaPath = c.MoaPath,
        PoaPath = c.PoaPath,
        TradeLicenceNumber = c.TradeLicenceNumber,
        TradeLicenceLegalName = c.TradeLicenceLegalName,
        TradeLicenceExpiryDate = c.TradeLicenceExpiryDate,
    };

    private static void CopyInto(OwnedCompany from, OwnedCompany to)
    {
        to.CompanyName = from.CompanyName;
        to.TradeLicenseDetails = from.TradeLicenseDetails;
        to.NatureOfHolding = from.NatureOfHolding;
        to.OwnershipPercentage = from.OwnershipPercentage;
        to.TradeLicensePath = from.TradeLicensePath;
        to.MoaPath = from.MoaPath;
        to.PoaPath = from.PoaPath;
        to.TradeLicenceNumber = from.TradeLicenceNumber;
        to.TradeLicenceLegalName = from.TradeLicenceLegalName;
        to.TradeLicenceExpiryDate = from.TradeLicenceExpiryDate;
    }

    private static string Show(string? value) => string.IsNullOrWhiteSpace(value) ? "\u2014" : value.Trim();

    private static string? Pct(decimal? value) => value?.ToString("0.##");

    private static string? Day(DateTime? value) => value?.ToString("dd MMM yyyy");

    /// <summary>Applies a typed date, complaining rather than silently clearing what is stored when
    /// it cannot be read -- the input re-renders from the stored value, so a typo does not take the
    /// captured date with it.</summary>
    private void SetDate(string? text, Action<DateTime?> assign)
    {
        if (string.IsNullOrWhiteSpace(text)) { assign(null); return; }

        if (UaeDate.Parse(text) is { } date) { assign(date); return; }

        Toasts.ShowError($"Enter the date as {UaeDate.Pattern.ToLowerInvariant()}.");
    }

    /// <summary>The audit trail records what actually changed, field by field, rather than "updated
    /// X": a reviewer reading the trail needs to see the old value as well as the new one.</summary>
    private static List<string> DiffOf(FamilyMember before, FamilyMember after)
    {
        var changes = new List<string>();

        void Cmp(string label, string? a, string? b)
        {
            if (!string.Equals(a ?? string.Empty, b ?? string.Empty, StringComparison.Ordinal))
            {
                changes.Add($"{label}: {Show(a)} \u2192 {Show(b)}");
            }
        }

        Cmp("Name", before.Name, after.Name);
        Cmp("Relation", RelationshipLabel(before.Relationship), RelationshipLabel(after.Relationship));
        Cmp("Emirates ID / Passport", before.IdentificationNumber, after.IdentificationNumber);
        Cmp("NIN Number", before.NinNumber, after.NinNumber);
        Cmp("Nationality", before.Nationality, after.Nationality);
        Cmp("Occupation / Business", before.Occupation, after.Occupation);
        Cmp("Company / Organization", before.Organization, after.Organization);
        Cmp("Nature Of Holding", HoldingLabel(before.NatureOfHolding), HoldingLabel(after.NatureOfHolding));
        Cmp("Ownership %", Pct(before.OwnershipPercentage), Pct(after.OwnershipPercentage));
        Cmp("Emirates ID No", before.EmiratesIdNumber, after.EmiratesIdNumber);
        Cmp("Emirates ID Expiry", Day(before.EmiratesIdExpiryDate), Day(after.EmiratesIdExpiryDate));
        Cmp("Passport No", before.PassportNumber, after.PassportNumber);
        Cmp("Passport Expiry", Day(before.PassportExpiryDate), Day(after.PassportExpiryDate));
        Cmp("Trade License No", before.TradeLicenceNumber, after.TradeLicenceNumber);
        Cmp("Trade License Legal Name", before.TradeLicenceLegalName, after.TradeLicenceLegalName);
        Cmp("Trade License Expiry", Day(before.TradeLicenceExpiryDate), Day(after.TradeLicenceExpiryDate));
        return changes;
    }

    private static List<string> DiffOf(OwnedCompany before, OwnedCompany after)
    {
        var changes = new List<string>();

        void Cmp(string label, string? a, string? b)
        {
            if (!string.Equals(a ?? string.Empty, b ?? string.Empty, StringComparison.Ordinal))
            {
                changes.Add($"{label}: {Show(a)} \u2192 {Show(b)}");
            }
        }

        Cmp("Name of the Company", before.CompanyName, after.CompanyName);
        Cmp("Nature Of Holding", HoldingLabel(before.NatureOfHolding), HoldingLabel(after.NatureOfHolding));
        Cmp("Ownership %", Pct(before.OwnershipPercentage), Pct(after.OwnershipPercentage));
        Cmp("Trade License No", before.TradeLicenceNumber, after.TradeLicenceNumber);
        Cmp("Trade License Legal Name", before.TradeLicenceLegalName, after.TradeLicenceLegalName);
        Cmp("Trade License Expiry", Day(before.TradeLicenceExpiryDate), Day(after.TradeLicenceExpiryDate));
        return changes;
    }

    /// <summary>Every field, not the interesting ones: once the row is deleted the audit entry is
    /// the only place it still exists, so anything left out of here is gone. The document paths are
    /// included because the uploaded files are deliberately left on disk -- a deletion that also
    /// destroyed the evidence would not be much of a record.</summary>
    private static string SnapshotOf(FamilyMember f) => string.Join("; ",
    [
        $"Name: {Show(f.Name)}",
        $"Relation: {RelationshipLabel(f.Relationship)}",
        $"NIN Number: {Show(f.NinNumber)}",
        $"Nationality: {Show(f.Nationality)}",
        $"Occupation / Business: {Show(f.Occupation)}",
        $"Company / Organization: {Show(f.Organization)}",
        $"Nature Of Holding: {HoldingLabel(f.NatureOfHolding)}",
        $"Ownership %: {Show(Pct(f.OwnershipPercentage))}",
        $"Emirates ID No: {Show(f.EmiratesIdNumber)}",
        $"Emirates ID Expiry: {Show(Day(f.EmiratesIdExpiryDate))}",
        $"Passport No: {Show(f.PassportNumber)}",
        $"Passport Expiry: {Show(Day(f.PassportExpiryDate))}",
        $"Trade License No: {Show(f.TradeLicenceNumber)}",
        $"Trade License Legal Name: {Show(f.TradeLicenceLegalName)}",
        $"Trade License Expiry: {Show(Day(f.TradeLicenceExpiryDate))}",
        $"Emirates ID / Passport (typed): {Show(f.IdentificationNumber)}",
        $"Documents: {Show(string.Join(", ", new[] { f.EmiratesIdPath, f.PassportPath, f.TradeLicencePath }.Where(d => !string.IsNullOrWhiteSpace(d))))}",
    ]);

    private static string SnapshotOf(OwnedCompany c) => string.Join("; ",
    [
        $"Name of the Company: {Show(c.CompanyName)}",
        $"Nature Of Holding: {HoldingLabel(c.NatureOfHolding)}",
        $"Ownership %: {Show(Pct(c.OwnershipPercentage))}",
        $"Trade License No: {Show(c.TradeLicenceNumber)}",
        $"Trade License Legal Name: {Show(c.TradeLicenceLegalName)}",
        $"Trade License Expiry: {Show(Day(c.TradeLicenceExpiryDate))}",
        $"Trade License Details (typed): {Show(c.TradeLicenseDetails)}",
        $"Documents: {Show(string.Join(", ", new[] { c.TradeLicensePath, c.MoaPath, c.PoaPath }.Where(d => !string.IsNullOrWhiteSpace(d))))}",
    ]);

    private static List<(string Path, string Short)> RelativeDocuments(FamilyMember f)
    {
        var docs = new List<(string, string)>();
        if (!string.IsNullOrEmpty(f.EmiratesIdPath)) docs.Add((f.EmiratesIdPath, "EID"));
        if (!string.IsNullOrEmpty(f.PassportPath)) docs.Add((f.PassportPath, "Passport"));
        if (!string.IsNullOrEmpty(f.TradeLicencePath)) docs.Add((f.TradeLicencePath, "Trade licence"));
        return docs;
    }

    // ---------- relatives ----------

    private void LoadOccupationChoice(string? occupation)
    {
        if (string.IsNullOrWhiteSpace(occupation))
        {
            _occupationChoice = string.Empty;
            _occupationOther = string.Empty;
        }
        else if (Occupations.IsListed(occupation))
        {
            _occupationChoice = occupation;
            _occupationOther = string.Empty;
        }
        else
        {
            // Typed before this list existed, or typed into the "Other" box last time.
            _occupationChoice = Occupations.Other;
            _occupationOther = occupation;
        }
    }

    private string? ChosenOccupation() => _occupationChoice switch
    {
        "" => null,
        Occupations.Other => string.IsNullOrWhiteSpace(_occupationOther) ? null : _occupationOther.Trim(),
        var listed => listed,
    };

    /// <summary>Relatives already on file that the one being saved collides with, by name, Emirates
    /// ID number or NIN. Blank fields never match -- two relatives with no NIN yet are not the same
    /// person -- and the record being edited never matches itself.</summary>
    private List<DuplicateMatch> DuplicatesOf(FamilyMember candidate)
    {
        var matches = new List<DuplicateMatch>();

        foreach (var existing in _familyMembers)
        {
            if (candidate.Id != 0 && existing.Id == candidate.Id) continue;

            if (SameValue(existing.EmiratesIdNumber, candidate.EmiratesIdNumber))
            {
                matches.Add(new DuplicateMatch(existing, "Emirates ID No", existing.EmiratesIdNumber!));
            }
            else if (SameValue(existing.NinNumber, candidate.NinNumber))
            {
                matches.Add(new DuplicateMatch(existing, "NIN Number", existing.NinNumber!));
            }
            else if (SameValue(existing.Name, candidate.Name))
            {
                matches.Add(new DuplicateMatch(existing, "Name", existing.Name));
            }
        }

        return matches;
    }

    private static bool SameValue(string? a, string? b) =>
        !string.IsNullOrWhiteSpace(a) && !string.IsNullOrWhiteSpace(b)
        && string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);

    /// <summary>Whether the collision is one a person could legitimately explain. Two relatives can
    /// genuinely share a name -- two cousins called Mohammed Ali are not a data error -- so that
    /// warning can be overridden. An Emirates ID number or a NIN identifies exactly one person, so
    /// a second record carrying it is wrong however sure the member is, and there is no way past
    /// this one except correcting the number.</summary>
    private bool DuplicateIsBlocking => _duplicateMatches.Any(m => m.Field != "Name");

    private void DismissDuplicateWarning() => _duplicateMatches = [];

    private async Task SaveDespiteDuplicateAsync()
    {
        _duplicateMatches = [];
        _duplicateAcknowledged = true;
        await SaveRelativeAsync();
    }

    private static string DescribeRelative(FamilyMember f) => string.Join(" · ",
        new[]
        {
            RelationLabel(f.Relationship),
            string.IsNullOrWhiteSpace(f.NinNumber) ? null : $"NIN {f.NinNumber}",
            string.IsNullOrWhiteSpace(f.EmiratesIdNumber) ? null : $"Emirates ID {f.EmiratesIdNumber}",
            string.IsNullOrWhiteSpace(f.Nationality) ? null : f.Nationality,
            string.IsNullOrWhiteSpace(f.Occupation) ? null : f.Occupation,
            string.IsNullOrWhiteSpace(f.Organization) ? null : f.Organization,
            f.NatureOfHolding == RelatedPartyHoldingNature.None ? null : HoldingLabel(f.NatureOfHolding),
            f.OwnershipPercentage is { } pct ? $"{pct:0.##}%" : null,
        }.Where(part => part is not null));

    private void OpenAddRelative()
    {
        if (_effectiveMember is null) return;
        _relativeTarget = null;
        _relativeBefore = null;
        _relativeForm = new FamilyMember
        {
            MemberId = _effectiveMember.Id,
            Relationship = RelativeRelationship.Spouse,
            Nationality = Countries.Default,
        };
        LoadOccupationChoice(null);
        _duplicateMatches = [];
        _duplicateAcknowledged = false;
        _relativesOpen = true;
        _relativeDialogOpen = true;
    }

    private void OpenEditRelative(FamilyMember familyMember)
    {
        _relativeTarget = familyMember;
        _relativeBefore = CopyOf(familyMember);
        _relativeForm = CopyOf(familyMember);
        LoadOccupationChoice(familyMember.Occupation);
        _duplicateMatches = [];
        _duplicateAcknowledged = false;
        _relativeDialogOpen = true;
    }

    private void CloseRelativeDialog()
    {
        _duplicateMatches = [];
        _duplicateAcknowledged = false;
        _relativeDialogOpen = false;
        _relativeForm = null;
        _relativeTarget = null;
        _relativeBefore = null;
    }

    private async Task SaveRelativeAsync()
    {
        if (_effectiveMember is null || _relativeForm is null) return;

        if (string.IsNullOrWhiteSpace(_relativeForm.Name))
        {
            Toasts.ShowError("Enter the related party's name.");
            return;
        }
        if (_occupationChoice == Occupations.Other && string.IsNullOrWhiteSpace(_occupationOther))
        {
            Toasts.ShowError("Enter the occupation or business.");
            return;
        }

        _relativeForm.Occupation = ChosenOccupation();
        if (_relativeForm.OwnershipPercentage is < 0 or > 100)
        {
            Toasts.ShowError("Ownership must be between 0 and 100.");
            return;
        }

        _relativeForm.Name = _relativeForm.Name.Trim();

        if (!_duplicateAcknowledged)
        {
            var duplicates = DuplicatesOf(_relativeForm);
            if (duplicates.Count > 0)
            {
                _duplicateMatches = duplicates;
                return;
            }
        }
        _duplicateAcknowledged = false;

        var actorName = await CurrentActorNameAsync();

        if (_relativeTarget is null)
        {
            var saved = CopyOf(_relativeForm);
            saved.Id = await FamilyMemberWriter.InsertAsync(_relativeForm);
            _familyMembers.Add(saved);

            await AuditLog.LogAsync(actorName, AuditAction.Create, nameof(FamilyMember), saved.Id.ToString(),
                $"Added related party {saved.Name}. {SnapshotOf(saved)}");

            // Stay open on the saved record so the documents can be attached without reopening:
            // they are filed under the record's id, which only exists once it is saved.
            _relativeTarget = saved;
            _relativeBefore = CopyOf(saved);
            _relativeForm = CopyOf(saved);
            LoadOccupationChoice(saved.Occupation);
            Toasts.ShowSuccess("Related party added. You can now attach documents.");
            return;
        }

        var changes = DiffOf(_relativeBefore!, _relativeForm);
        if (changes.Count == 0)
        {
            CloseRelativeDialog();
            Toasts.ShowSuccess("No changes to save.");
            return;
        }

        _relativeForm.Id = _relativeTarget.Id;
        await FamilyMemberWriter.UpdateAsync(_relativeForm);
        CopyInto(_relativeForm, _relativeTarget);

        await AuditLog.LogAsync(actorName, AuditAction.Update, nameof(FamilyMember), _relativeTarget.Id.ToString(),
            $"Edited related party {_relativeTarget.Name}. {string.Join("; ", changes)}");

        CloseRelativeDialog();
        Toasts.ShowSuccess("Related party saved.");
    }

    private void AskRemoveRelative(FamilyMember familyMember)
    {
        _pendingDeleteCompany = null;
        _pendingDeleteRelative = familyMember;
    }

    private void CancelRemoveRelative() => _pendingDeleteRelative = null;

    private async Task ConfirmRemoveRelativeAsync()
    {
        if (_pendingDeleteRelative is { } familyMember)
        {
            _pendingDeleteRelative = null;
            await RemoveFamilyMemberAsync(familyMember);
        }
    }

    private async Task RemoveFamilyMemberAsync(FamilyMember familyMember)
    {
        if (_effectiveMember is null) return;

        await FamilyMemberWriter.DeleteAsync(familyMember.Id);
        _familyMembers.Remove(familyMember);
        if (_relativeTarget == familyMember) CloseRelativeDialog();

        var actorName = await CurrentActorNameAsync();
        await AuditLog.LogAsync(actorName, AuditAction.Delete, nameof(FamilyMember), familyMember.Id.ToString(),
            $"Deleted related party {familyMember.Name}. {SnapshotOf(familyMember)}");

        GoToRelativesPage(_page);
        Toasts.ShowSuccess("Related party removed.");
    }

    // ---------- companies ----------

    private void OpenAddCompany()
    {
        if (_effectiveMember is null) return;
        _companyTarget = null;
        _companyBefore = null;
        _companyForm = new OwnedCompany { MemberId = _effectiveMember.Id };
        _companiesOpen = true;
        _companyDialogOpen = true;
    }

    private void OpenEditCompany(OwnedCompany company)
    {
        _companyTarget = company;
        _companyBefore = CopyOf(company);
        _companyForm = CopyOf(company);
        _companyDialogOpen = true;
    }

    private void CloseCompanyDialog()
    {
        _companyDialogOpen = false;
        _companyForm = null;
        _companyTarget = null;
        _companyBefore = null;
    }

    private async Task SaveCompanyAsync()
    {
        if (_effectiveMember is null || _companyForm is null) return;

        if (string.IsNullOrWhiteSpace(_companyForm.CompanyName))
        {
            Toasts.ShowError("Enter the company name.");
            return;
        }
        if (_companyForm.OwnershipPercentage is < 0 or > 100)
        {
            Toasts.ShowError("Ownership must be between 0 and 100.");
            return;
        }

        _companyForm.CompanyName = _companyForm.CompanyName.Trim();
        var actorName = await CurrentActorNameAsync();

        if (_companyTarget is null)
        {
            var saved = CopyOf(_companyForm);
            saved.Id = await CompanyWriter.InsertAsync(_companyForm);
            _companies.Add(saved);

            await AuditLog.LogAsync(actorName, AuditAction.Create, nameof(OwnedCompany), saved.Id.ToString(),
                $"Added company {saved.CompanyName}. {SnapshotOf(saved)}");

            _companyTarget = saved;
            _companyBefore = CopyOf(saved);
            _companyForm = CopyOf(saved);
            Toasts.ShowSuccess("Company added. You can now attach documents.");
            return;
        }

        var changes = DiffOf(_companyBefore!, _companyForm);
        if (changes.Count == 0)
        {
            CloseCompanyDialog();
            Toasts.ShowSuccess("No changes to save.");
            return;
        }

        _companyForm.Id = _companyTarget.Id;
        await CompanyWriter.UpdateAsync(_companyForm);
        CopyInto(_companyForm, _companyTarget);

        await AuditLog.LogAsync(actorName, AuditAction.Update, nameof(OwnedCompany), _companyTarget.Id.ToString(),
            $"Edited company {_companyTarget.CompanyName}. {string.Join("; ", changes)}");

        CloseCompanyDialog();
        Toasts.ShowSuccess("Company saved.");
    }

    private void AskRemoveCompany(OwnedCompany company)
    {
        _pendingDeleteRelative = null;
        _pendingDeleteCompany = company;
    }

    private void CancelRemoveCompany() => _pendingDeleteCompany = null;

    private async Task ConfirmRemoveCompanyAsync()
    {
        if (_pendingDeleteCompany is { } company)
        {
            _pendingDeleteCompany = null;
            await RemoveCompanyAsync(company);
        }
    }

    private async Task RemoveCompanyAsync(OwnedCompany company)
    {
        if (_effectiveMember is null) return;

        await CompanyWriter.DeleteAsync(company.Id);
        _companies.Remove(company);
        if (_companyTarget == company) CloseCompanyDialog();

        var actorName = await CurrentActorNameAsync();
        await AuditLog.LogAsync(actorName, AuditAction.Delete, nameof(OwnedCompany), company.Id.ToString(),
            $"Deleted company {company.CompanyName}. {SnapshotOf(company)}");

        Toasts.ShowSuccess("Company removed.");
    }

    // ---------- documents ----------

    private static string? ExtensionFor(string contentType) => contentType switch
    {
        "image/png" => ".png",
        "image/jpeg" => ".jpg",
        "application/pdf" => ".pdf",
        _ => null,
    };

    /// <summary>Reads the trade licence number, expiry and legal name off the uploaded document via
    /// Azure AI Document Intelligence. Best-effort by design: there is no prebuilt trade-licence
    /// model, so the service pattern-matches labels over the OCR text (see
    /// DocumentIntelligenceService). Whatever it returns lands in fields the member can correct, and
    /// a failure -- or an unconfigured service -- never blocks the upload itself.</summary>
    private async Task<TradeLicenceExtraction?> ReadTradeLicenceAsync(string filePath)
    {
        if (!DocIntel.IsConfigured) return null;

        try
        {
            await using var stream = File.OpenRead(filePath);
            return await DocIntel.AnalyzeTradeLicenceAsync(stream);
        }
        catch (Exception ex)
        {
            Toasts.ShowError($"Could not auto-read the trade licence ({ex.Message}). Enter the details manually.");
            return null;
        }
    }

    /// <summary>Same best-effort contract as the trade licence read: prebuilt-idDocument is a
    /// purpose-built model and stronger than the licence's label matching, but an Emirates ID's
    /// bilingual layout still defeats it often enough that every field it fills stays editable.</summary>
    private async Task<IdDocumentExtraction?> ReadIdDocumentAsync(string filePath)
    {
        if (!DocIntel.IsConfigured) return null;

        try
        {
            await using var stream = File.OpenRead(filePath);
            return await DocIntel.AnalyzeIdDocumentAsync(stream);
        }
        catch (Exception ex)
        {
            Toasts.ShowError($"Could not auto-read the document ({ex.Message}). Enter the details manually.");
            return null;
        }
    }

    private static string CapturedNote(IdDocumentExtraction? captured, string label) => captured is null
        ? "No details were read from it."
        : $"Read from it -- {label} No: {Show(captured.DocumentNumber)}; Expiry: {Show(Day(captured.DateOfExpiration))}.";

    private static string CapturedNote(TradeLicenceExtraction? captured) => captured is null
        ? "No details were read from it."
        : $"Read from it -- Trade License No: {Show(captured.LicenceNumber)}; "
          + $"Legal Name: {Show(captured.BusinessName)}; "
          + $"Expiry: {Show(Day(captured.ExpiryDate))}.";

    /// <summary>Uploads write to the saved record, not to the dialog's working copy: a document is
    /// filed the moment it is chosen, and sweeping the half-typed field edits in beside it would
    /// save changes the member has not pressed Save on.</summary>
    private async Task UploadRelativeDocumentAsync(string kind, string label, InputFileChangeEventArgs e)
    {
        if (_effectiveMember is null || _relativeTarget is null) return;

        var extension = ExtensionFor(e.File.ContentType);
        if (extension is null) { Toasts.ShowError("Only JPEG, PNG, or PDF files are supported."); return; }
        if (e.File.Size > MaxIdUploadBytes) { Toasts.ShowError("File must be 1 MB or smaller."); return; }

        var target = _relativeTarget;
        _uploadingDocs.Add((target.Id, kind));
        StateHasChanged();
        try
        {
            var uploadsDir = Path.Combine(Env.WebRootPath, "uploads", "my-workspace", "family");
            Directory.CreateDirectory(uploadsDir);
            foreach (var existing in Directory.GetFiles(uploadsDir, $"{target.Id}-{kind}.*")) File.Delete(existing);

            var fileName = $"{target.Id}-{kind}{extension}";
            var filePath = Path.Combine(uploadsDir, fileName);
            await using (var stream = e.File.OpenReadStream(MaxIdUploadBytes))
            await using (var file = File.Create(filePath))
            {
                await stream.CopyToAsync(file);
            }

            var path = $"/uploads/my-workspace/family/{fileName}";
            TradeLicenceExtraction? captured = null;
            IdDocumentExtraction? capturedId = null;
            switch (kind)
            {
                case "emirates-id":
                    target.EmiratesIdPath = path;
                    capturedId = await ReadIdDocumentAsync(filePath);
                    if (!string.IsNullOrWhiteSpace(capturedId?.DocumentNumber)) target.EmiratesIdNumber = capturedId.DocumentNumber;
                    if (capturedId?.DateOfExpiration is not null) target.EmiratesIdExpiryDate = capturedId.DateOfExpiration;
                    break;
                case "passport":
                    target.PassportPath = path;
                    capturedId = await ReadIdDocumentAsync(filePath);
                    if (!string.IsNullOrWhiteSpace(capturedId?.DocumentNumber)) target.PassportNumber = capturedId.DocumentNumber;
                    if (capturedId?.DateOfExpiration is not null) target.PassportExpiryDate = capturedId.DateOfExpiration;
                    break;
                case "trade-licence":
                    target.TradeLicencePath = path;
                    captured = await ReadTradeLicenceAsync(filePath);
                    if (!string.IsNullOrWhiteSpace(captured?.LicenceNumber)) target.TradeLicenceNumber = captured.LicenceNumber;
                    if (!string.IsNullOrWhiteSpace(captured?.BusinessName)) target.TradeLicenceLegalName = captured.BusinessName;
                    if (captured?.ExpiryDate is not null) target.TradeLicenceExpiryDate = captured.ExpiryDate;
                    break;
            }

            await FamilyMemberWriter.UpdateAsync(target);

            foreach (var copy in new[] { _relativeForm, _relativeBefore })
            {
                if (copy is null) continue;
                copy.EmiratesIdPath = target.EmiratesIdPath;
                copy.EmiratesIdNumber = target.EmiratesIdNumber;
                copy.EmiratesIdExpiryDate = target.EmiratesIdExpiryDate;
                copy.PassportPath = target.PassportPath;
                copy.PassportNumber = target.PassportNumber;
                copy.PassportExpiryDate = target.PassportExpiryDate;
                copy.TradeLicencePath = target.TradeLicencePath;
                copy.TradeLicenceNumber = target.TradeLicenceNumber;
                copy.TradeLicenceLegalName = target.TradeLicenceLegalName;
                copy.TradeLicenceExpiryDate = target.TradeLicenceExpiryDate;
            }

            var actorName = await CurrentActorNameAsync();
            var detail = $"Attached {label} to related party {target.Name}: {fileName}";
            if (DocIntel.IsConfigured)
            {
                if (kind == "trade-licence") detail += $" {CapturedNote(captured)}";
                else detail += $" {CapturedNote(capturedId, label)}";
            }
            await AuditLog.LogAsync(actorName, AuditAction.Update, nameof(FamilyMember), target.Id.ToString(), detail);

            var readSomething = kind == "trade-licence"
                ? captured is not null && (captured.LicenceNumber is not null || captured.BusinessName is not null || captured.ExpiryDate is not null)
                : capturedId is not null && (capturedId.DocumentNumber is not null || capturedId.DateOfExpiration is not null);

            Toasts.ShowSuccess(readSomething
                ? $"{label} uploaded. The details below were read off it — check them before saving."
                : $"{label} uploaded.");
        }
        finally
        {
            _uploadingDocs.Remove((target.Id, kind));
        }
    }

    private async Task UploadCompanyDocumentAsync(string kind, string label, InputFileChangeEventArgs e)
    {
        if (_effectiveMember is null || _companyTarget is null) return;

        var extension = ExtensionFor(e.File.ContentType);
        if (extension is null) { Toasts.ShowError("Only JPEG, PNG, or PDF files are supported."); return; }
        if (e.File.Size > MaxCompanyDocUploadBytes) { Toasts.ShowError("File must be 10 MB or smaller."); return; }

        var target = _companyTarget;
        _uploadingDocs.Add((target.Id, kind));
        StateHasChanged();
        try
        {
            var uploadsDir = Path.Combine(Env.WebRootPath, "uploads", "my-workspace", "companies");
            Directory.CreateDirectory(uploadsDir);
            foreach (var existing in Directory.GetFiles(uploadsDir, $"{target.Id}-{kind}.*")) File.Delete(existing);

            var fileName = $"{target.Id}-{kind}{extension}";
            var filePath = Path.Combine(uploadsDir, fileName);
            await using (var stream = e.File.OpenReadStream(MaxCompanyDocUploadBytes))
            await using (var file = File.Create(filePath))
            {
                await stream.CopyToAsync(file);
            }

            var path = $"/uploads/my-workspace/companies/{fileName}";
            TradeLicenceExtraction? captured = null;
            switch (kind)
            {
                case "trade-license":
                    target.TradeLicensePath = path;
                    captured = await ReadTradeLicenceAsync(filePath);
                    if (!string.IsNullOrWhiteSpace(captured?.LicenceNumber)) target.TradeLicenceNumber = captured.LicenceNumber;
                    if (!string.IsNullOrWhiteSpace(captured?.BusinessName)) target.TradeLicenceLegalName = captured.BusinessName;
                    if (captured?.ExpiryDate is not null) target.TradeLicenceExpiryDate = captured.ExpiryDate;
                    break;
                case "moa": target.MoaPath = path; break;
                default: target.PoaPath = path; break;
            }

            await CompanyWriter.UpdateAsync(target);

            foreach (var copy in new[] { _companyForm, _companyBefore })
            {
                if (copy is null) continue;
                copy.TradeLicensePath = target.TradeLicensePath;
                copy.MoaPath = target.MoaPath;
                copy.PoaPath = target.PoaPath;
                copy.TradeLicenceNumber = target.TradeLicenceNumber;
                copy.TradeLicenceLegalName = target.TradeLicenceLegalName;
                copy.TradeLicenceExpiryDate = target.TradeLicenceExpiryDate;
            }

            var actorName = await CurrentActorNameAsync();
            var detail = $"Attached {label} to company {target.CompanyName}: {fileName}";
            if (kind == "trade-license" && DocIntel.IsConfigured) detail += $" {CapturedNote(captured)}";
            await AuditLog.LogAsync(actorName, AuditAction.Update, nameof(OwnedCompany), target.Id.ToString(), detail);

            if (kind == "trade-license" && captured is not null
                && (captured.LicenceNumber is not null || captured.BusinessName is not null || captured.ExpiryDate is not null))
            {
                Toasts.ShowSuccess("Trade licence uploaded. The details below were read off it — check them before saving.");
            }
            else
            {
                Toasts.ShowSuccess($"{label} uploaded.");
            }
        }
        finally
        {
            _uploadingDocs.Remove((target.Id, kind));
        }
    }

    private static string RelationshipLabel(RelativeRelationship relationship) => relationship switch
    {
        RelativeRelationship.InLaws => "In-Laws",
        RelativeRelationship.FatherInLaw => "Father-in-Law",
        RelativeRelationship.MotherInLaw => "Mother-in-Law",
        _ => relationship.ToString(),
    };
}
