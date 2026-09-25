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
    [Inject] private ApplicationDbContext Db { get; set; } = default!;
    [Inject] private IFamilyMemberWriter FamilyMemberWriter { get; set; } = default!;
    [Inject] private IOwnedCompanyWriter CompanyWriter { get; set; } = default!;
    [Inject] private IAuditLogger AuditLog { get; set; } = default!;
    [Inject] private AuthenticationStateProvider AuthState { get; set; } = default!;
    [Inject] private ImpersonationContext Impersonation { get; set; } = default!;
    [Inject] private ToastService Toasts { get; set; } = default!;
    [Inject] private IWebHostEnvironment Env { get; set; } = default!;

    private string _activeTab = "family";
    private bool _loaded;

    /// <summary>Separates "still loading" from "this login has no member record". Without it, a
    /// login that is not linked to a Member -- the built-in setup account, for one -- sat on the
    /// loading message for ever, which reads as a page that never finishes loading.</summary>
    private Member? _effectiveMember;

    private readonly List<FamilyMember> _familyMembers = [];
    private readonly List<OwnedCompany> _companies = [];

    private string _newFamilyName = string.Empty;
    private RelativeRelationship _newFamilyRelationship = RelativeRelationship.Spouse;

    // Relatives table: search, relation filter, paging and which row is open for editing.
    private string _search = string.Empty;
    private RelativeRelationship? _relationFilter;
    private int _page = 1;
    private const int RelativesPageSize = 10;
    private int? _editingId;
    private bool _addingRow;

    // Declaration summary strip.
    private string? _lastDeclared;
    private bool _hasSubmitted;
    private string? _declarationPeriod;
    private string? _nextDueDate;

    private string _newCompanyName = string.Empty;
    private string _newCompanyTradeLicenseDetails = string.Empty;
    private decimal? _newCompanyOwnership;

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
        var state = await AuthState.GetAuthenticationStateAsync();

        if (Impersonation.ActingMemberId is { } actingId)
        {
            _effectiveMember = await Db.Members.FirstOrDefaultAsync(m => m.Id == actingId);
        }
        else
        {
            var userId = state.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (userId is not null)
            {
                _effectiveMember = await Db.Members.FirstOrDefaultAsync(m => m.ApplicationUserId == userId);
            }
        }

        if (_effectiveMember is null) return;

        _familyMembers.Clear();
        _familyMembers.AddRange(await Db.FamilyMembers.AsNoTracking()
            .Where(f => f.MemberId == _effectiveMember.Id)
            .OrderBy(f => f.Id)
            .ToListAsync());

        _companies.Clear();
        _companies.AddRange(await Db.OwnedCompanies.AsNoTracking()
            .Where(c => c.MemberId == _effectiveMember.Id)
            .OrderBy(c => c.Id)
            .ToListAsync());

        await LoadDeclarationSummaryAsync();
    }

    /// <summary>The summary strip reads the member's latest Insider Trading declaration and the run
    /// it answered, so the period and due date shown are the ones the member is actually being asked
    /// for rather than a calendar quarter worked out here.</summary>
    private async Task LoadDeclarationSummaryAsync()
    {
        if (_effectiveMember is null) return;

        var latest = await Db.InsiderDeclarations
            .Include(d => d.DeclarationCycleRun)
            .Where(d => d.MemberId == _effectiveMember.Id && !d.IsDraft)
            .OrderByDescending(d => d.SubmittedAtUtc)
            .FirstOrDefaultAsync();

        if (latest is not null)
        {
            _hasSubmitted = true;
            _lastDeclared = latest.SubmittedAtUtc.ToLocalDisplay("MMM dd, yyyy");
            _declarationPeriod = latest.DeclarationCycleRun is { } run ? $"Q{run.PeriodQuarter} {run.PeriodYear}" : null;
            _nextDueDate = latest.DeclarationCycleRun?.DueDateUtc.ToLocalDisplay("MMM dd, yyyy");
            return;
        }

        // Nothing submitted yet: fall back to the open run so the member can still see what is due.
        var open = await Db.DeclarationCycleRuns
            .Where(r => r.Type == DeclarationCycleType.InsiderTrading && r.Sent && !r.Recalled)
            .OrderByDescending(r => r.PeriodYear).ThenByDescending(r => r.PeriodQuarter)
            .FirstOrDefaultAsync();

        _hasSubmitted = false;
        _declarationPeriod = open is null ? null : $"Q{open.PeriodQuarter} {open.PeriodYear}";
        _nextDueDate = open?.DueDateUtc.ToLocalDisplay("MMM dd, yyyy");
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

    private static string InterestLabel(RelatedPartyInterestType interest) =>
        interest == RelatedPartyInterestType.None ? "—" : interest.ToString();

    private async Task<string> CurrentActorNameAsync()
    {
        var state = await AuthState.GetAuthenticationStateAsync();
        return state.User.Identity?.Name ?? "unknown";
    }

    private FamilyMember? _draft;

    /// <summary>Opens a blank row at the top of the table. The row is only written once it is saved,
    /// so an abandoned "Add Declaration" leaves nothing behind.</summary>
    private void StartAddRelative()
    {
        if (_effectiveMember is null) return;
        _editingId = null;
        _addingRow = true;
        _draft = new FamilyMember
        {
            MemberId = _effectiveMember.Id,
            Relationship = RelativeRelationship.Spouse,
        };
    }

    private void CancelAddRelative()
    {
        _addingRow = false;
        _draft = null;
    }

    private async Task AddFamilyMemberAsync()
    {
        if (_effectiveMember is null || _draft is null) return;

        if (string.IsNullOrWhiteSpace(_draft.Name))
        {
            Toasts.ShowError("Enter the related party's name.");
            return;
        }

        _draft.Name = _draft.Name.Trim();
        var id = await FamilyMemberWriter.InsertAsync(_draft);
        _draft.Id = id;
        _familyMembers.Add(_draft);

        var actorName = await CurrentActorNameAsync();
        await AuditLog.LogAsync(actorName, AuditAction.Create, nameof(FamilyMember), _effectiveMember.Id.ToString(),
            $"Added related party: {_draft.Name} ({RelationshipLabel(_draft.Relationship)}).");

        _addingRow = false;
        _draft = null;
        Toasts.ShowSuccess("Related party added.");
    }

    private void StartEditRelative(FamilyMember familyMember)
    {
        CancelAddRelative();
        _editingId = familyMember.Id;
    }

    private void CancelEditRelative() => _editingId = null;

    private async Task SaveFamilyMemberAsync(FamilyMember familyMember)
    {
        _editingId = null;
        if (_effectiveMember is null) return;

        if (string.IsNullOrWhiteSpace(familyMember.Name))
        {
            Toasts.ShowError("Enter the family member's name.");
            return;
        }

        await FamilyMemberWriter.UpdateAsync(familyMember);

        var actorName = await CurrentActorNameAsync();
        await AuditLog.LogAsync(actorName, AuditAction.Update, nameof(FamilyMember), _effectiveMember.Id.ToString(),
            $"Updated family member: {familyMember.Name} ({RelationshipLabel(familyMember.Relationship)}).");

        Toasts.ShowSuccess("Family member saved.");
    }

    private async Task RemoveFamilyMemberAsync(FamilyMember familyMember)
    {
        if (_effectiveMember is null) return;

        await FamilyMemberWriter.DeleteAsync(familyMember.Id);
        _familyMembers.Remove(familyMember);

        var actorName = await CurrentActorNameAsync();
        await AuditLog.LogAsync(actorName, AuditAction.Delete, nameof(FamilyMember), _effectiveMember.Id.ToString(),
            $"Removed family member: {familyMember.Name} ({RelationshipLabel(familyMember.Relationship)}).");

        Toasts.ShowSuccess("Family member removed.");
    }

    private async Task AddCompanyAsync()
    {
        if (_effectiveMember is null) return;

        if (string.IsNullOrWhiteSpace(_newCompanyName))
        {
            Toasts.ShowError("Enter the company name.");
            return;
        }
        if (_newCompanyOwnership is < 0 or > 100)
        {
            Toasts.ShowError("Ownership must be between 0 and 100.");
            return;
        }

        var company = new OwnedCompany
        {
            MemberId = _effectiveMember.Id,
            CompanyName = _newCompanyName.Trim(),
            TradeLicenseDetails = string.IsNullOrWhiteSpace(_newCompanyTradeLicenseDetails) ? null : _newCompanyTradeLicenseDetails.Trim(),
            OwnershipPercentage = _newCompanyOwnership,
        };
        var id = await CompanyWriter.InsertAsync(company);
        company.Id = id;
        _companies.Add(company);

        var actorName = await CurrentActorNameAsync();
        await AuditLog.LogAsync(actorName, AuditAction.Create, nameof(OwnedCompany), _effectiveMember.Id.ToString(),
            $"Added company: {company.CompanyName}.");

        _newCompanyName = string.Empty;
        _newCompanyTradeLicenseDetails = string.Empty;
        _newCompanyOwnership = null;
        Toasts.ShowSuccess("Company added.");
    }

    private async Task SaveCompanyAsync(OwnedCompany company)
    {
        if (_effectiveMember is null) return;

        if (string.IsNullOrWhiteSpace(company.CompanyName))
        {
            Toasts.ShowError("Enter the company name.");
            return;
        }
        if (company.OwnershipPercentage is < 0 or > 100)
        {
            Toasts.ShowError("Ownership must be between 0 and 100.");
            return;
        }

        await CompanyWriter.UpdateAsync(company);

        var actorName = await CurrentActorNameAsync();
        await AuditLog.LogAsync(actorName, AuditAction.Update, nameof(OwnedCompany), _effectiveMember.Id.ToString(),
            $"Updated company: {company.CompanyName}.");

        Toasts.ShowSuccess("Company saved.");
    }

    private async Task RemoveCompanyAsync(OwnedCompany company)
    {
        if (_effectiveMember is null) return;

        await CompanyWriter.DeleteAsync(company.Id);
        _companies.Remove(company);

        var actorName = await CurrentActorNameAsync();
        await AuditLog.LogAsync(actorName, AuditAction.Delete, nameof(OwnedCompany), _effectiveMember.Id.ToString(),
            $"Removed company: {company.CompanyName}.");

        Toasts.ShowSuccess("Company removed.");
    }

    private Task OnEmiratesIdSelectedAsync(FamilyMember familyMember, InputFileChangeEventArgs e) =>
        UploadFamilyDocumentAsync(familyMember, "emirates-id", "Emirates ID", e);

    private Task OnPassportSelectedAsync(FamilyMember familyMember, InputFileChangeEventArgs e) =>
        UploadFamilyDocumentAsync(familyMember, "passport", "Passport", e);

    private Task OnTradeLicenseSelectedAsync(OwnedCompany company, InputFileChangeEventArgs e) =>
        UploadCompanyDocumentAsync(company, "trade-license", "Trade License", e);

    private Task OnMoaSelectedAsync(OwnedCompany company, InputFileChangeEventArgs e) =>
        UploadCompanyDocumentAsync(company, "moa", "MOA", e);

    private Task OnPoaSelectedAsync(OwnedCompany company, InputFileChangeEventArgs e) =>
        UploadCompanyDocumentAsync(company, "poa", "POA", e);

    private static string? ExtensionFor(string contentType) => contentType switch
    {
        "image/png" => ".png",
        "image/jpeg" => ".jpg",
        "application/pdf" => ".pdf",
        _ => null,
    };

    private async Task UploadFamilyDocumentAsync(FamilyMember familyMember, string kind, string label, InputFileChangeEventArgs e)
    {
        if (_effectiveMember is null) return;

        var extension = ExtensionFor(e.File.ContentType);
        if (extension is null) { Toasts.ShowError("Only JPEG, PNG, or PDF files are supported."); return; }
        if (e.File.Size > MaxIdUploadBytes) { Toasts.ShowError("File must be 1 MB or smaller."); return; }

        _uploadingDocs.Add((familyMember.Id, kind));
        StateHasChanged();
        try
        {
            var uploadsDir = Path.Combine(Env.WebRootPath, "uploads", "my-workspace", "family");
            Directory.CreateDirectory(uploadsDir);
            foreach (var existing in Directory.GetFiles(uploadsDir, $"{familyMember.Id}-{kind}.*")) File.Delete(existing);

            var fileName = $"{familyMember.Id}-{kind}{extension}";
            var filePath = Path.Combine(uploadsDir, fileName);
            await using (var stream = e.File.OpenReadStream(MaxIdUploadBytes))
            await using (var target = File.Create(filePath))
            {
                await stream.CopyToAsync(target);
            }

            var path = $"/uploads/my-workspace/family/{fileName}";
            if (kind == "emirates-id") familyMember.EmiratesIdPath = path;
            else familyMember.PassportPath = path;

            await FamilyMemberWriter.UpdateAsync(familyMember);

            var actorName = await CurrentActorNameAsync();
            await AuditLog.LogAsync(actorName, AuditAction.Update, nameof(FamilyMember), _effectiveMember.Id.ToString(),
                $"Uploaded {label} for family member: {familyMember.Name}.");

            Toasts.ShowSuccess("File uploaded.");
        }
        finally
        {
            _uploadingDocs.Remove((familyMember.Id, kind));
        }
    }

    private async Task UploadCompanyDocumentAsync(OwnedCompany company, string kind, string label, InputFileChangeEventArgs e)
    {
        if (_effectiveMember is null) return;

        var extension = ExtensionFor(e.File.ContentType);
        if (extension is null) { Toasts.ShowError("Only JPEG, PNG, or PDF files are supported."); return; }
        if (e.File.Size > MaxCompanyDocUploadBytes) { Toasts.ShowError("File must be 10 MB or smaller."); return; }

        _uploadingDocs.Add((company.Id, kind));
        StateHasChanged();
        try
        {
            var uploadsDir = Path.Combine(Env.WebRootPath, "uploads", "my-workspace", "companies");
            Directory.CreateDirectory(uploadsDir);
            foreach (var existing in Directory.GetFiles(uploadsDir, $"{company.Id}-{kind}.*")) File.Delete(existing);

            var fileName = $"{company.Id}-{kind}{extension}";
            var filePath = Path.Combine(uploadsDir, fileName);
            await using (var stream = e.File.OpenReadStream(MaxCompanyDocUploadBytes))
            await using (var target = File.Create(filePath))
            {
                await stream.CopyToAsync(target);
            }

            var path = $"/uploads/my-workspace/companies/{fileName}";
            switch (kind)
            {
                case "trade-license": company.TradeLicensePath = path; break;
                case "moa": company.MoaPath = path; break;
                default: company.PoaPath = path; break;
            }

            await CompanyWriter.UpdateAsync(company);

            var actorName = await CurrentActorNameAsync();
            await AuditLog.LogAsync(actorName, AuditAction.Update, nameof(OwnedCompany), _effectiveMember.Id.ToString(),
                $"Uploaded {label} for company: {company.CompanyName}.");

            Toasts.ShowSuccess("File uploaded.");
        }
        finally
        {
            _uploadingDocs.Remove((company.Id, kind));
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
