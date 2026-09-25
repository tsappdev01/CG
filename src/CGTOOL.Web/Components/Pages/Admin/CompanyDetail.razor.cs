using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using CGTOOL.Web.Data;
using CGTOOL.Web.Data.Governance;

namespace CGTOOL.Web.Components.Pages.Admin;

public partial class CompanyDetail
{
    private const long MaxLogoBytes = 2 * 1024 * 1024;

    [Parameter] public int Id { get; set; }

    private Company? _editing;
    private List<Member>? _members;
    private bool _saving;
    private bool _showDeactivateJustify;

    // LOVs configured under CompanyLookups in appsettings.json rather than hard-coded, so the sector
    // and group name options can be edited/extended without a code change.
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
            Toasts.ShowError("Logo must be 2 MB or smaller.");
            return;
        }

        var uploadsDir = Path.Combine(Env.WebRootPath, "uploads", "company-logos");
        Directory.CreateDirectory(uploadsDir);

        foreach (var existing in Directory.GetFiles(uploadsDir, $"{_editing.Id}.*"))
        {
            File.Delete(existing);
        }

        var fileName = $"{_editing.Id}{extension}";
        var filePath = Path.Combine(uploadsDir, fileName);

        await using (var stream = file.OpenReadStream(MaxLogoBytes))
        await using (var target = File.Create(filePath))
        {
            await stream.CopyToAsync(target);
        }

        await CompanyWriter.SetLogoPathAsync(_editing.Id, $"/uploads/company-logos/{fileName}");
        _editing.LogoPath = $"/uploads/company-logos/{fileName}?v={DateTime.UtcNow.Ticks}";

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
