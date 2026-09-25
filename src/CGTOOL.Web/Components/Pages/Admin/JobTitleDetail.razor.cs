using Microsoft.AspNetCore.Components;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using CGTOOL.Web.Data.Governance;

namespace CGTOOL.Web.Components.Pages.Admin;

public partial class JobTitleDetail
{
    [Parameter] public int Id { get; set; }

    private JobTitle? _editing;
    private int _memberCount;
    private bool _saving;

    protected override async Task OnParametersSetAsync()
    {
        // Its own short-lived context rather than the circuit-scoped ApplicationDbContext:
        // sharing that one lets this race, or outlive, whatever else in the circuit is using
        // it -- which kills the circuit and takes the page with it.
        await using var db = await DbFactory.CreateDbContextAsync();

        _editing = Id == 0
            ? new JobTitle()
            : await db.JobTitles.AsNoTracking().FirstOrDefaultAsync(j => j.Id == Id);

        if (_editing is null)
        {
            Toasts.ShowError("That job title no longer exists.");
            Nav.NavigateTo("/admin/job-titles");
            return;
        }

        // JobTitle is held on Member as free text rather than a foreign key, so the count matches
        // on the name -- the same thing the list page counts.
        _memberCount = Id == 0 ? 0 : await db.Members.CountAsync(m => m.JobTitle == _editing.Name);
    }

    private async Task<string> CurrentActorAsync()
    {
        var state = await AuthState.GetAuthenticationStateAsync();
        return state.User.Identity?.Name ?? "unknown";
    }

    private async Task SaveAsync()
    {
        if (_editing is null || _saving) return;
        _saving = true;

        try
        {
            var isNew = _editing.Id == 0;
            _editing.Name = _editing.Name.Trim();

            try
            {
                if (isNew) _editing.Id = await JobTitleWriter.InsertAsync(_editing);
                else await JobTitleWriter.UpdateAsync(_editing);
            }
            catch (SqlException ex) when (ex.Number == 50010)
            {
                Toasts.ShowError($"A job title named '{_editing.Name}' already exists.");
                return;
            }

            await AuditLog.LogAsync(
                await CurrentActorAsync(),
                isNew ? AuditAction.Create : AuditAction.Update,
                nameof(JobTitle), _editing.Id.ToString(), _editing.Name);

            Toasts.ShowSuccess($"{_editing.Name} saved.");
            Nav.NavigateTo("/admin/job-titles");
        }
        finally
        {
            _saving = false;
        }
    }
}
