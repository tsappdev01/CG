using System.ComponentModel.DataAnnotations;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.JSInterop;
using CGTOOL.Web.Data;
using CGTOOL.Web.Data.Governance;

namespace CGTOOL.Web.Components.Pages.Admin;

public partial class JobTitles
{
    private List<JobTitle>? _jobTitles;
    private JobTitle? _pendingJustify;
    private bool _showPrintPreview;
    private JobTitle? _printSingleRecord;
    private string _sortColumn = "Name";
    private bool _sortAscending = true;
    private HashSet<int> _selectedIds = [];
    private bool? _bulkSetActive;
    private string _search = string.Empty;
    private string _statusFilter = "all";

    // Member.JobTitle is free text rather than a foreign key, so the count matches on the name.
    private Dictionary<string, int> _memberCounts = [];

    private bool HasActiveFilters => !string.IsNullOrWhiteSpace(_search) || _statusFilter != "all";

    private void ClearFilters()
    {
        _search = string.Empty;
        _statusFilter = "all";
    }

    private int MemberCount(string name) => _memberCounts.GetValueOrDefault(name);

    private void AddNew() => Nav.NavigateTo("/admin/job-titles/0");

    private void Edit(JobTitle jobTitle) => Nav.NavigateTo($"/admin/job-titles/{jobTitle.Id}");

    private List<JobTitle> VisibleJobTitles()
    {
        IEnumerable<JobTitle> rows = SortedJobTitles();

        if (_statusFilter == "active") rows = rows.Where(j => j.Active);
        else if (_statusFilter == "inactive") rows = rows.Where(j => !j.Active);

        if (!string.IsNullOrWhiteSpace(_search))
        {
            var term = _search.Trim();
            rows = rows.Where(j => j.Name.Contains(term, StringComparison.OrdinalIgnoreCase));
        }

        return rows.ToList();
    }

    private async Task PrintAsync() => await JS.InvokeVoidAsync("print");

    private List<JobTitle> PrintRows() => _printSingleRecord is not null ? [_printSingleRecord] : SortedJobTitles();

    protected override async Task OnInitializedAsync() => await LoadAsync();

    private async Task LoadAsync()
    {
        _jobTitles = await Db.JobTitles.OrderBy(j => j.Name).ToListAsync();

        _memberCounts = await Db.Members
            .Where(m => m.JobTitle != null)
            .GroupBy(m => m.JobTitle!)
            .Select(g => new { Title = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Title, x => x.Count);
    }

    private List<JobTitle> SortedJobTitles()
    {
        if (_jobTitles is null) return [];
        IOrderedEnumerable<JobTitle> sorted = _sortColumn switch
        {
            "Users" => _jobTitles.OrderBy(j => MemberCount(j.Name)),
            "Status" => _jobTitles.OrderBy(j => j.Active),
            _ => _jobTitles.OrderBy(j => j.Name),
        };
        return (_sortAscending ? sorted : sorted.Reverse()).ToList();
    }

    private void Sort(string column)
    {
        if (_sortColumn == column) _sortAscending = !_sortAscending;
        else { _sortColumn = column; _sortAscending = true; }
    }

    private bool AllSelected => VisibleJobTitles() is { Count: > 0 } visible && visible.All(j => _selectedIds.Contains(j.Id));

    private void ToggleSelectAll(bool select)
    {
        if (select) _selectedIds = VisibleJobTitles().Select(j => j.Id).ToHashSet();
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
        if (_bulkSetActive is not { } targetActive || _jobTitles is null) return;

        foreach (var id in _selectedIds.ToList())
        {
            var jobTitle = _jobTitles.FirstOrDefault(j => j.Id == id);
            if (jobTitle is null || jobTitle.Active == targetActive) continue;

            jobTitle.Active = targetActive;
            await JobTitleWriter.SetActiveAsync(jobTitle.Id, targetActive);

            var state = await AuthState.GetAuthenticationStateAsync();
            await AuditLog.LogAsync(
                state.User.Identity?.Name ?? "unknown",
                targetActive ? AuditAction.Reactivate : AuditAction.Deactivate,
                nameof(JobTitle), jobTitle.Id.ToString(), jobTitle.Name,
                justification: justification);
        }

        Toasts.ShowSuccess($"{_selectedIds.Count} job title(s) {(targetActive ? "reactivated" : "deactivated")}.");
        _bulkSetActive = null;
        _selectedIds.Clear();
        await LoadAsync();
    }




    private async Task ToggleActiveAsync(string justification)
    {
        if (_pendingJustify is null) return;

        _pendingJustify.Active = !_pendingJustify.Active;
        await JobTitleWriter.SetActiveAsync(_pendingJustify.Id, _pendingJustify.Active);

        var state = await AuthState.GetAuthenticationStateAsync();
        await AuditLog.LogAsync(
            state.User.Identity?.Name ?? "unknown",
            _pendingJustify.Active ? AuditAction.Reactivate : AuditAction.Deactivate,
            nameof(JobTitle), _pendingJustify.Id.ToString(), _pendingJustify.Name,
            justification: justification);

        Toasts.ShowSuccess($"{_pendingJustify.Name} {(_pendingJustify.Active ? "reactivated" : "deactivated")}.");
        _pendingJustify = null;
        await LoadAsync();
    }
}
