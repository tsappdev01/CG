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
    private int _page = 1;
    private int _pageSize = 10;

    // Member.JobTitle is free text rather than a foreign key, so the count matches on the name.
    private Dictionary<string, int> _memberCounts = [];

    private bool HasActiveFilters => !string.IsNullOrWhiteSpace(_search) || _statusFilter != "all";

    private void ClearFilters()
    {
        _search = string.Empty;
        _statusFilter = "all";
        _page = 1;
    }

    private List<JobTitle> PagedJobTitles() =>
        VisibleJobTitles().Skip((_page - 1) * _pageSize).Take(_pageSize).ToList();

    private int MemberCount(string name) => _memberCounts.GetValueOrDefault(name);

    /// <summary>A job title held by at least one user cannot be withdrawn: those Member rows are its
    /// child records, and taking the title out of the list while people still carry it leaves their
    /// record pointing at something the picklist no longer offers -- which then silently changes the
    /// next time anyone saves them. Move those users to another title first.</summary>
    private bool IsInUse(JobTitle jobTitle) => MemberCount(jobTitle.Name) > 0;

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
        // A short-lived context of its own, not the circuit-scoped ApplicationDbContext. Sharing
        // that one lets this load race whatever else in the circuit is using it at the same moment,
        // which surfaces as "A second operation was started on this context instance before a
        // previous operation completed" -- the same reason NavMenu and TopBar take their own.
        await using var db = await DbFactory.CreateDbContextAsync();

        _jobTitles = await db.JobTitles.OrderBy(j => j.Name).ToListAsync();

        _memberCounts = await db.Members
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

        var changed = 0;
        var blocked = new List<string>();

        foreach (var id in _selectedIds.ToList())
        {
            var jobTitle = _jobTitles.FirstOrDefault(j => j.Id == id);
            if (jobTitle is null || jobTitle.Active == targetActive) continue;

            if (!targetActive && IsInUse(jobTitle))
            {
                blocked.Add($"{jobTitle.Name} ({MemberCount(jobTitle.Name)})");
                continue;
            }

            jobTitle.Active = targetActive;
            await JobTitleWriter.SetActiveAsync(jobTitle.Id, targetActive);

            var state = await AuthState.GetAuthenticationStateAsync();
            await AuditLog.LogAsync(
                state.User.Identity?.Name ?? "unknown",
                targetActive ? AuditAction.Reactivate : AuditAction.Deactivate,
                nameof(JobTitle), jobTitle.Id.ToString(), jobTitle.Name,
                justification: justification);
            changed++;
        }

        if (changed > 0) Toasts.ShowSuccess($"{changed} job title(s) {(targetActive ? "reactivated" : "deactivated")}.");
        if (blocked.Count > 0)
        {
            Toasts.ShowError($"Still in use, so not withdrawn: {string.Join(", ", blocked)}. Move those users to another title first.");
        }

        _bulkSetActive = null;
        _selectedIds.Clear();
        await LoadAsync();
    }




    private async Task ToggleActiveAsync(string justification)
    {
        if (_pendingJustify is null) return;

        if (_pendingJustify.Active && IsInUse(_pendingJustify))
        {
            Toasts.ShowError($"{_pendingJustify.Name} is held by {MemberCount(_pendingJustify.Name)} user(s). Move them to another title before withdrawing it.");
            _pendingJustify = null;
            return;
        }

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
