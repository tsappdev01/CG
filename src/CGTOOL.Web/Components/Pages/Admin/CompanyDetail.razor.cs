using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using CGTOOL.Web.Data;
using CGTOOL.Web.Data.Governance;

namespace CGTOOL.Web.Components.Pages.Admin;

public partial class CompanyDetail
{
    private const long MaxLogoBytes = UploadLimits.PictureBytes;

    [Parameter] public int Id { get; set; }

    private Company? _editing;
    private List<Member>? _members;
    private bool _saving;
    private bool _showDeactivateJustify;

    // LOVs configured under CompanyLookups in appsettings.json rather than hard-coded, so the sector
    // and group name options can be edited/extended without a code change.
    /// <summary>The select's value for "nobody holds this". A string rather than a member id
    /// because the choice is not a member, and a sentinel id would be a member id that must never
    /// exist.</summary>
    public const string NotApplicable = "not-applicable";

    /// <summary>Maps the one select onto the two things it answers: who holds the authority, or
    /// that nobody does. Set together, so the pair can never say both or neither.</summary>
    private string ApprovingAuthorityChoice
    {
        get => _editing is null ? string.Empty
            : _editing.ApprovingAuthorityNotApplicable ? NotApplicable
            : _editing.ApprovingAuthorityMemberId?.ToString() ?? string.Empty;
        set
        {
            if (_editing is null) return;
            _editing.ApprovingAuthorityNotApplicable = value == NotApplicable;
            _editing.ApprovingAuthorityMemberId = int.TryParse(value, out var id) ? id : null;
        }
    }

    private string DelegateAuthorityChoice
    {
        get => _editing is null ? string.Empty
            : _editing.DelegateAuthorityNotApplicable ? NotApplicable
            : _editing.DelegateAuthorityMemberId?.ToString() ?? string.Empty;
        set
        {
            if (_editing is null) return;
            _editing.DelegateAuthorityNotApplicable = value == NotApplicable;
            _editing.DelegateAuthorityMemberId = int.TryParse(value, out var id) ? id : null;
        }
    }

    private List<string> Sectors => Configuration.GetSection("CompanyLookups:Sectors").Get<string[]>()?.ToList() ?? [];
    private List<string> GroupNames => Configuration.GetSection("CompanyLookups:GroupNames").Get<string[]>()?.ToList() ?? [];

    protected override async Task OnParametersSetAsync()
    {
        // Its own short-lived context rather than the circuit-scoped ApplicationDbContext:
        // sharing that one lets this race, or outlive, whatever else in the circuit is using
        // it -- which kills the circuit and takes the page with it.
        await using var db = await DbFactory.CreateDbContextAsync();

        _members = await db.Members.Where(m => m.Active).OrderBy(m => m.FullName).ToListAsync();

        if (Id == 0)
        {
            _editing = new Company { Active = true };
            return;
        }

        _editing = await db.Companies.FirstOrDefaultAsync(c => c.Id == Id);
        if (_editing is null)
        {
            Nav.NavigateTo("/admin/companies");
        }
    }

    private void GoBack() => Nav.NavigateTo("/admin/companies");

    private async Task<string> CurrentActorAsync()
    {
        var state = await AuthState.GetAuthenticationStateAsync();
        return state.User.Identity?.Name ?? "unknown";
    }

    private async Task OnLogoSelectedAsync(InputFileChangeEventArgs e)
    {
        if (_editing is null || _editing.Id == 0) return;

        var file = e.File;
        var extension = file.ContentType switch
        {
            "image/png" => ".png",
            "image/webp" => ".webp",
            "image/jpeg" => ".jpg",
            _ => null,
        };
        if (extension is null)
        {
            Toasts.ShowError("Only JPEG, PNG, or WebP images are supported.");
            return;
        }
        if (file.Size > MaxLogoBytes)
        {
            Toasts.ShowError(UploadLimits.TooLarge(file.Size, MaxLogoBytes));
            return;
        }

        // Read through a bounded stream rather than trusting the browser's reported size, then
        // store the bytes. Nothing is written to disk: the logo is a row in dbo.DocumentBlobs and
        // is served from /documents/{id}.
        using var buffer = new MemoryStream();
        await using (var stream = file.OpenReadStream(MaxLogoBytes))
        {
            await stream.CopyToAsync(buffer);
        }

        var blobId = await Documents.SaveAsync(
            buffer.ToArray(), file.Name, file.ContentType, Client.IpAddress);

        // The path is cleared at the same time. Leaving the old one set would mean a company whose
        // logo was replaced still has a path to a file nobody serves any more, and DocumentUrl
        // prefers the blob anyway -- so it would be dead data that reads like a fallback.
        await CompanyWriter.SetLogoAsync(_editing.Id, null, blobId);
        _editing.LogoPath = null;
        _editing.LogoBlobId = blobId;

        await AuditLog.LogAsync(await CurrentActorAsync(), AuditAction.Update, nameof(Company), _editing.Id.ToString(), "Logo updated");
        Toasts.ShowSuccess("Logo updated.");
    }

    private async Task SaveAsync()
    {
        // Its own short-lived context rather than the circuit-scoped ApplicationDbContext:
        // sharing that one lets this race, or outlive, whatever else in the circuit is using
        // it -- which kills the circuit and takes the page with it.
        await using var db = await DbFactory.CreateDbContextAsync();

        if (_editing is null || _saving) return;
        _saving = true;

        try
        {
            var isNew = _editing.Id == 0;

            try
            {
                if (isNew)
                {
                    _editing.Id = await CompanyWriter.InsertAsync(_editing);
                }
                else
                {
                    await CompanyWriter.UpdateAsync(_editing);
                }
            }
            catch (SqlException ex) when (ex.Number == 50001)
            {
                Toasts.ShowError($"A company with short code '{_editing.ShortCode}' already exists.");
                return;
            }

            await AuditLog.LogAsync(await CurrentActorAsync(), isNew ? AuditAction.Create : AuditAction.Update, nameof(Company), _editing.Id.ToString(), _editing.Name);
            Toasts.ShowSuccess($"{_editing.Name} saved.");

            if (isNew)
            {
                Nav.NavigateTo($"/admin/companies/{_editing.Id}");
                return;
            }

            var editingId = _editing.Id;
            _editing = await db.Companies.FirstAsync(c => c.Id == editingId);
        }
        finally
        {
            _saving = false;
        }
    }

    private async Task ToggleActiveAsync(string justification)
    {
        if (_editing is null || _editing.Id == 0) return;
        _showDeactivateJustify = false;

        _editing.Active = !_editing.Active;
        await CompanyWriter.SetActiveAsync(_editing.Id, _editing.Active);

        await AuditLog.LogAsync(
            await CurrentActorAsync(),
            _editing.Active ? AuditAction.Reactivate : AuditAction.Deactivate,
            nameof(Company), _editing.Id.ToString(), _editing.Name,
            justification: justification);

        Toasts.ShowSuccess($"{_editing.Name} {(_editing.Active ? "reactivated" : "deactivated")}.");
    }
}
