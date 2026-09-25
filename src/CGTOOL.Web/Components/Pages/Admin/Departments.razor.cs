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
    private bool _showPrintPreview;
    private Department? _printSingleRecord;
    private string _sortColumn = "Name";
    private bool _sortAscending = true;
    private HashSet<int> _selectedIds = [];
    private bool? _bulkSetActive;
    private string _search = string.Empty;
    private string _statusFilter = "all";



    private bool HasActiveFilters => !string.IsNullOrWhiteSpace(_search) || _statusFilter != "all";

    private void ClearFilters()
    {
        _search = string.Empty;
        _statusFilter = "all";
    }



    private void AddNew() => Nav.NavigateTo("/admin/departments/0");

    private void Edit(Department department) => Nav.NavigateTo($"/admin/departments/{department.Id}");

    private async Task PrintAsync() => await JS.InvokeVoidAsync("print");

    private List<Department> PrintRows() => _printSingleRecord is not null ? [_printSingleRecord] : SortedDepartments();

    protected override async Task OnInitializedAsync() => await LoadAsync();

    private async Task LoadAsync()
    {
        // A short-lived context of its own, not the circuit-scoped ApplicationDbContext. Sharing
        // that one lets this load race whatever else in the circuit is using it at the same moment,
        // which surfaces as "A second operation was started on this context instance before a
        // previous operation completed" -- the same reason NavMenu and TopBar take their own.
        await using var db = await DbFactory.CreateDbContextAsync();

        _departments = await db.Departments.OrderBy(d => d.Name).ToListAsync();
        _memberCounts = await db.Members
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

    private List<Department> VisibleDepartments()
    {
        IEnumerable<Department> rows = SortedDepartments();

        if (_statusFilter == "active") rows = rows.Where(d => d.Active);
        else if (_statusFilter == "inactive") rows = rows.Where(d => !d.Active);

        if (!string.IsNullOrWhiteSpace(_search))
        {
            var term = _search.Trim();
            rows = rows.Where(d =>
                d.Code.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                d.Name.Contains(term, StringComparison.OrdinalIgnoreCase));
        }

        return rows.ToList();
    }

    private void Sort(string column)
    {
        if (_sortColumn == column) _sortAscending = !_sortAscending;
        else { _sortColumn = column; _sortAscending = true; }
    }

    private bool AllSelected => VisibleDepartments() is { Count: > 0 } visible && visible.All(d => _selectedIds.Contains(d.Id));

    private void ToggleSelectAll(bool select)
    {
        if (select) _selectedIds = VisibleDepartments().Select(d => d.Id).ToHashSet();
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
