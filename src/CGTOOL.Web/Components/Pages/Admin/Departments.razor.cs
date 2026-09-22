using System.ComponentModel.DataAnnotations;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.JSInterop;
using CGTOOL.Web.Data;
using CGTOOL.Web.Data.Governance;

namespace CGTOOL.Web.Components.Pages.Admin;

public partial class Departments
{
    private List<Department>? _departments;
    private Dictionary<int, int> _memberCounts = [];
    private Department? _pendingJustify;
    private string _newCode = string.Empty;
    private string _newName = string.Empty;
    private bool _showPrintPreview;
    private Department? _printSingleRecord;
    private string _sortColumn = "Name";
    private bool _sortAscending = true;
    private HashSet<int> _selectedIds = [];
    private bool? _bulkSetActive;

    private async Task PrintAsync() => await JS.InvokeVoidAsync("print");

    private List<Department> PrintRows() => _printSingleRecord is not null ? [_printSingleRecord] : SortedDepartments();

    protected override async Task OnInitializedAsync() => await LoadAsync();

    private async Task LoadAsync()
    {
        _departments = await Db.Departments.OrderBy(d => d.Name).ToListAsync();
        _memberCounts = await Db.Members
            .Where(m => m.DepartmentId != null)
            .GroupBy(m => m.DepartmentId!.Value)
            .Select(g => new { DepartmentId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.DepartmentId, x => x.Count);
    }

    private int MemberCount(int departmentId) => _memberCounts.GetValueOrDefault(departmentId);

    private List<Department> SortedDepartments()
    {
        if (_departments is null) return [];
        IOrderedEnumerable<Department> sorted = _sortColumn switch
        {
            "Code" => _departments.OrderBy(d => d.Code),
            "Users" => _departments.OrderBy(d => MemberCount(d.Id)),
            "Status" => _departments.OrderBy(d => d.Active),
            _ => _departments.OrderBy(d => d.Name),
        };
        return (_sortAscending ? sorted : sorted.Reverse()).ToList();
    }

    private void Sort(string column)
    {
        if (_sortColumn == column) _sortAscending = !_sortAscending;
        else { _sortColumn = column; _sortAscending = true; }
    }

    private bool AllSelected => _departments is { Count: > 0 } && _departments.All(d => _selectedIds.Contains(d.Id));

    private void ToggleSelectAll(bool select)
    {
        if (select) _selectedIds = _departments!.Select(d => d.Id).ToHashSet();
        else _selectedIds.Clear();
    }

    private void ToggleSelect(int id, bool select)
    {
        if (select) _selectedIds.Add(id);
        else _selectedIds.Remove(id);
    }

    private void DeselectAll() => _selectedIds.Clear();

    private async Task BulkSetActiveAsync(string justification)
    {
        if (_bulkSetActive is not { } targetActive || _departments is null) return;

        foreach (var id in _selectedIds.ToList())
        {
            var department = _departments.FirstOrDefault(d => d.Id == id);
            if (department is null || department.Active == targetActive) continue;

            department.Active = targetActive;
            await DepartmentWriter.SetActiveAsync(department.Id, targetActive);

            var state = await AuthState.GetAuthenticationStateAsync();
            await AuditLog.LogAsync(
                state.User.Identity?.Name ?? "unknown",
                targetActive ? AuditAction.Reactivate : AuditAction.Deactivate,
                nameof(Department), department.Id.ToString(), department.Name,
                justification: justification);
        }

        Toasts.ShowSuccess($"{_selectedIds.Count} department(s) {(targetActive ? "reactivated" : "deactivated")}.");
        _bulkSetActive = null;
        _selectedIds.Clear();
        await LoadAsync();
    }

    private static bool TryValidate(object model, out string errorMessage)
    {
        var results = new List<ValidationResult>();
        var isValid = Validator.TryValidateObject(model, new ValidationContext(model), results, validateAllProperties: true);
        errorMessage = string.Join(" ", results.Select(r => r.ErrorMessage));
        return isValid;
    }

    private async Task AddAsync()
    {
        var department = new Department { Code = _newCode.Trim(), Name = _newName.Trim() };
        if (!TryValidate(department, out var error))
        {
            Toasts.ShowError(error);
            return;
        }

        try
        {
            department.Id = await DepartmentWriter.InsertAsync(department);
        }
        catch (SqlException ex) when (ex.Number == 50002)
        {
            Toasts.ShowError($"A department with code '{department.Code}' already exists.");
            return;
        }

        var state = await AuthState.GetAuthenticationStateAsync();
        await AuditLog.LogAsync(state.User.Identity?.Name ?? "unknown", AuditAction.Create, nameof(Department), department.Id.ToString(), department.Name);

        Toasts.ShowSuccess($"{department.Name} added.");
        _newCode = string.Empty;
        _newName = string.Empty;
        await LoadAsync();
    }

    private async Task SaveAsync(Department department)
    {
        if (!TryValidate(department, out var error))
        {
            Toasts.ShowError(error);
            return;
        }

        try
        {
            await DepartmentWriter.UpdateAsync(department);
        }
        catch (SqlException ex) when (ex.Number == 50002)
        {
            Toasts.ShowError($"A department with code '{department.Code}' already exists.");
            return;
        }

        var state = await AuthState.GetAuthenticationStateAsync();
        await AuditLog.LogAsync(state.User.Identity?.Name ?? "unknown", AuditAction.Update, nameof(Department), department.Id.ToString(), department.Name);
        Toasts.ShowSuccess($"{department.Name} saved.");
    }

    private async Task ToggleActiveAsync(string justification)
    {
        if (_pendingJustify is null) return;

        _pendingJustify.Active = !_pendingJustify.Active;
        await DepartmentWriter.SetActiveAsync(_pendingJustify.Id, _pendingJustify.Active);

        var state = await AuthState.GetAuthenticationStateAsync();
        await AuditLog.LogAsync(
            state.User.Identity?.Name ?? "unknown",
            _pendingJustify.Active ? AuditAction.Reactivate : AuditAction.Deactivate,
            nameof(Department), _pendingJustify.Id.ToString(), _pendingJustify.Name,
            justification: justification);

        Toasts.ShowSuccess($"{_pendingJustify.Name} {(_pendingJustify.Active ? "reactivated" : "deactivated")}.");
        _pendingJustify = null;
        await LoadAsync();
    }
}
