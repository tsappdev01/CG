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
    private string _newName = string.Empty;
    private bool _showPrintPreview;
    private JobTitle? _printSingleRecord;
    private string _sortColumn = "Name";
    private bool _sortAscending = true;
    private HashSet<int> _selectedIds = [];
    private bool? _bulkSetActive;

    private async Task PrintAsync() => await JS.InvokeVoidAsync("print");

    private List<JobTitle> PrintRows() => _printSingleRecord is not null ? [_printSingleRecord] : SortedJobTitles();

    protected override async Task OnInitializedAsync() => await LoadAsync();

    private async Task LoadAsync()
    {
        _jobTitles = await Db.JobTitles.OrderBy(j => j.Name).ToListAsync();
    }

    private List<JobTitle> SortedJobTitles()
    {
        if (_jobTitles is null) return [];
        IOrderedEnumerable<JobTitle> sorted = _sortColumn switch
        {
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

    private bool AllSelected => _jobTitles is { Count: > 0 } && _jobTitles.All(j => _selectedIds.Contains(j.Id));

    private void ToggleSelectAll(bool select)
    {
        if (select) _selectedIds = _jobTitles!.Select(j => j.Id).ToHashSet();
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

    private static bool TryValidate(object model, out string errorMessage)
    {
        var results = new List<ValidationResult>();
        var isValid = Validator.TryValidateObject(model, new ValidationContext(model), results, validateAllProperties: true);
        errorMessage = string.Join(" ", results.Select(r => r.ErrorMessage));
        return isValid;
    }

    private async Task AddAsync()
    {
        var jobTitle = new JobTitle { Name = _newName.Trim() };
        if (!TryValidate(jobTitle, out var error))
        {
            Toasts.ShowError(error);
            return;
        }

        try
        {
            jobTitle.Id = await JobTitleWriter.InsertAsync(jobTitle);
        }
        catch (SqlException ex) when (ex.Number == 50010)
        {
            Toasts.ShowError($"A job title '{jobTitle.Name}' already exists.");
            return;
        }

        var state = await AuthState.GetAuthenticationStateAsync();
        await AuditLog.LogAsync(state.User.Identity?.Name ?? "unknown", AuditAction.Create, nameof(JobTitle), jobTitle.Id.ToString(), jobTitle.Name);

        Toasts.ShowSuccess($"{jobTitle.Name} added.");
        _newName = string.Empty;
        await LoadAsync();
    }

    private async Task SaveAsync(JobTitle jobTitle)
    {
        if (!TryValidate(jobTitle, out var error))
        {
            Toasts.ShowError(error);
            return;
        }

        try
        {
            await JobTitleWriter.UpdateAsync(jobTitle);
        }
        catch (SqlException ex) when (ex.Number == 50010)
        {
            Toasts.ShowError($"A job title '{jobTitle.Name}' already exists.");
            return;
        }

        var state = await AuthState.GetAuthenticationStateAsync();
        await AuditLog.LogAsync(state.User.Identity?.Name ?? "unknown", AuditAction.Update, nameof(JobTitle), jobTitle.Id.ToString(), jobTitle.Name);
        Toasts.ShowSuccess($"{jobTitle.Name} saved.");
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
