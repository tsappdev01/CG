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
    private Member? _effectiveMember;

    private readonly List<FamilyMember> _familyMembers = [];
    private readonly List<OwnedCompany> _companies = [];

    private string _newFamilyName = string.Empty;
    private RelativeRelationship _newFamilyRelationship = RelativeRelationship.Spouse;

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

    protected override async Task OnInitializedAsync() => await LoadAsync();

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
    }

    private async Task<string> CurrentActorNameAsync()
    {
        var state = await AuthState.GetAuthenticationStateAsync();
        return state.User.Identity?.Name ?? "unknown";
    }

    private async Task AddFamilyMemberAsync()
    {
        if (_effectiveMember is null) return;

        if (string.IsNullOrWhiteSpace(_newFamilyName))
        {
            Toasts.ShowError("Enter the family member's name.");
            return;
        }

        var familyMember = new FamilyMember
        {
            MemberId = _effectiveMember.Id,
            Name = _newFamilyName.Trim(),
            Relationship = _newFamilyRelationship,
        };
        var id = await FamilyMemberWriter.InsertAsync(familyMember);
        familyMember.Id = id;
        _familyMembers.Add(familyMember);

        var actorName = await CurrentActorNameAsync();
        await AuditLog.LogAsync(actorName, AuditAction.Create, nameof(FamilyMember), _effectiveMember.Id.ToString(),
            $"Added family member: {familyMember.Name} ({RelationshipLabel(familyMember.Relationship)}).");

        _newFamilyName = string.Empty;
        _newFamilyRelationship = RelativeRelationship.Spouse;
        Toasts.ShowSuccess("Family member added.");
    }

    private async Task SaveFamilyMemberAsync(FamilyMember familyMember)
    {
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
