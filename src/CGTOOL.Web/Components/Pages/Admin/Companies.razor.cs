using Microsoft.EntityFrameworkCore;
using Microsoft.JSInterop;
using CGTOOL.Web.Data;
using CGTOOL.Web.Data.Governance;

namespace CGTOOL.Web.Components.Pages.Admin;

public partial class Companies
{
    private List<Company>? _companies;
    private Dictionary<int, int> _memberCounts = [];
    private Company? _pendingJustify;
    private bool _showPrintPreview;
    private Company? _printSingleRecord;
    private string _sortColumn = "Name";
    private bool _sortAscending = true;
    private HashSet<int> _selectedIds = [];
    private bool? _bulkSetActive;
    private int _page = 1;
    private int _pageSize = 10;

    private List<Company> PagedCompanies() =>
        SortedCompanies().Skip((_page - 1) * _pageSize).Take(_pageSize).ToList();

    private async Task PrintAsync() => await JS.InvokeVoidAsync("print");

    private List<Company> PrintRows() => _printSingleRecord is not null ? [_printSingleRecord] : SortedCompanies();

    protected override async Task OnInitializedAsync() => await LoadAsync();

    private async Task LoadAsync()
    {
        _companies = await Db.Companies.OrderBy(c => c.Name).ToListAsync();
        _memberCounts = await Db.Members
            .GroupBy(m => m.CompanyId)
            .Select(g => new { CompanyId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.CompanyId, x => x.Count);
    }

    private int MemberCount(int companyId) => _memberCounts.GetValueOrDefault(companyId);

    private List<Company> SortedCompanies()
    {
        if (_companies is null) return [];
        IOrderedEnumerable<Company> sorted = _sortColumn switch
        {
            "ShortCode" => _companies.OrderBy(c => c.ShortCode),
            "City" => _companies.OrderBy(c => c.City),
            "Country" => _companies.OrderBy(c => c.Country),
            "Users" => _companies.OrderBy(c => MemberCount(c.Id)),
            "Status" => _companies.OrderBy(c => c.Active),
            _ => _companies.OrderBy(c => c.Name),
        };
        return (_sortAscending ? sorted : sorted.Reverse()).ToList();
    }

    private void Sort(string column)
    {
        if (_sortColumn == column) _sortAscending = !_sortAscending;
        else { _sortColumn = column; _sortAscending = true; }
    }

    private bool AllSelected => _companies is { Count: > 0 } && _companies.All(c => _selectedIds.Contains(c.Id));

    private void ToggleSelectAll(bool select)
    {
        if (select) _selectedIds = _companies!.Select(c => c.Id).ToHashSet();
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
        if (_bulkSetActive is not { } targetActive || _companies is null) return;

        foreach (var id in _selectedIds.ToList())
        {
            var company = _companies.FirstOrDefault(c => c.Id == id);
            if (company is null || company.Active == targetActive) continue;

            company.Active = targetActive;
            await CompanyWriter.SetActiveAsync(company.Id, targetActive);

            var state = await AuthState.GetAuthenticationStateAsync();
            await AuditLog.LogAsync(
                state.User.Identity?.Name ?? "unknown",
                targetActive ? AuditAction.Reactivate : AuditAction.Deactivate,
                nameof(Company), company.Id.ToString(), company.Name,
                justification: justification);
        }

        Toasts.ShowSuccess($"{_selectedIds.Count} compan{(_selectedIds.Count == 1 ? "y" : "ies")} {(targetActive ? "reactivated" : "deactivated")}.");
        _bulkSetActive = null;
        _selectedIds.Clear();
        await LoadAsync();
    }

    private void AddNew() => Nav.NavigateTo("/admin/companies/0");

    private void Edit(Company company) => Nav.NavigateTo($"/admin/companies/{company.Id}");

    private async Task ToggleActiveAsync(string justification)
    {
        if (_pendingJustify is null) return;

        _pendingJustify.Active = !_pendingJustify.Active;
        await CompanyWriter.SetActiveAsync(_pendingJustify.Id, _pendingJustify.Active);

        var state = await AuthState.GetAuthenticationStateAsync();
        await AuditLog.LogAsync(
            state.User.Identity?.Name ?? "unknown",
            _pendingJustify.Active ? AuditAction.Reactivate : AuditAction.Deactivate,
            nameof(Company), _pendingJustify.Id.ToString(), _pendingJustify.Name,
            justification: justification);

        Toasts.ShowSuccess($"{_pendingJustify.Name} {(_pendingJustify.Active ? "reactivated" : "deactivated")}.");
        _pendingJustify = null;
        await LoadAsync();
    }
}
