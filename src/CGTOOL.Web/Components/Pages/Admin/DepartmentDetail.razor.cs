using Microsoft.AspNetCore.Components;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using CGTOOL.Web.Data.Governance;

namespace CGTOOL.Web.Components.Pages.Admin;

public partial class DepartmentDetail
{
    [Parameter] public int Id { get; set; }

    private Department? _editing;
    private int _memberCount;
    private bool _saving;

    protected override async Task OnParametersSetAsync()
    {
        // Its own short-lived context rather than the circuit-scoped ApplicationDbContext:
        // sharing that one lets this race, or outlive, whatever else in the circuit is using
        // it -- which kills the circuit and takes the page with it.
        await using var db = await DbFactory.CreateDbContextAsync();

        // Id 0 is the "new" route, exactly as CompanyDetail treats it.
        _editing = Id == 0
            ? new Department()
            : await db.Departments.AsNoTracking().FirstOrDefaultAsync(d => d.Id == Id);

        if (_editing is null)
        {
            Toasts.ShowError("That department no longer exists.");
            Nav.NavigateTo("/admin/departments");
            return;
        }

        _memberCount = Id == 0 ? 0 : await db.Members.CountAsync(m => m.DepartmentId == Id);
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
            _editing.Code = _editing.Code.Trim();
            _editing.Name = _editing.Name.Trim();

            try
            {
                if (isNew) _editing.Id = await DepartmentWriter.InsertAsync(_editing);
                else await DepartmentWriter.UpdateAsync(_editing);
            }
            catch (SqlException ex) when (ex.Number == 50002)
            {
                Toasts.ShowError($"A department with code '{_editing.Code}' already exists.");
                return;
            }

            await AuditLog.LogAsync(
                await CurrentActorAsync(),
                isNew ? AuditAction.Create : AuditAction.Update,
                nameof(Department), _editing.Id.ToString(), _editing.Name);

            Toasts.ShowSuccess($"{_editing.Name} saved.");
            Nav.NavigateTo("/admin/departments");
        }
        finally
        {
            _saving = false;
        }
    }
}
