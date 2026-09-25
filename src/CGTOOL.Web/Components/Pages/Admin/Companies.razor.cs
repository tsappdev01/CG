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
    private string _search = string.Empty;
    private string _statusFilter = "all";

    private bool HasActiveFilters => !string.IsNullOrWhiteSpace(_search) || _statusFilter != "all";

    private void ClearFilters()
    {
        _search = string.Empty;
        _statusFilter = "all";
        _page = 1;
    }

    /// <summary>Everything the filters leave, in sort order. Selection and the counts work off this
    /// rather than the page, so paging never silently drops a selection.</summary>
    private List<Company> VisibleCompanies()
    {
        IEnumerable<Company> rows = SortedCompanies();

        if (_statusFilter == "active") rows = rows.Where(c => c.Active);
        else if (_statusFilter == "inactive") rows = rows.Where(c => !c.Active);

        if (!string.IsNullOrWhiteSpace(_search))
        {
            // Name, short code, city and country -- every text column on the table, so what you can
            // see is what you can search for.
            var term = _search.Trim();
            rows = rows.Where(c =>
                c.Name.Contains(term, StringComparison.OrdinalIgnoreCase)
                || c.ShortCode.Contains(term, StringComparison.OrdinalIgnoreCase)
                || (c.City ?? string.Empty).Contains(term, StringComparison.OrdinalIgnoreCase)
                || (c.Country ?? string.Empty).Contains(term, StringComparison.OrdinalIgnoreCase));
        }

        return rows.ToList();
    }

    private List<Company> PagedCompanies() =>
        VisibleCompanies().Skip((_page - 1) * _pageSize).Take(_pageSize).ToList();

    private async Task PrintAsync() => await JS.InvokeVoidAsync("print");

    // The print preview follows the filters -- printing rows the screen is hiding would surprise.
    private List<Company> PrintRows() => _printSingleRecord is not null ? [_printSingleRecord] : VisibleCompanies();

    protected override async Task OnInitializedAsync() => await LoadAsync();

    private async Task LoadAsync()
    {
        // Its own short-lived context rather than the circuit-scoped ApplicationDbContext: sharing
        // that one lets this race, or outlive, whatever else in the circuit is using it.
        await using var db = await DbFactory.CreateDbContextAsync();

        _companies = await db.Companies.OrderBy(c => c.Name).ToListAsync();
        _memberCounts = await db.Members
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

    private bool AllSelected => VisibleCompanies() is { Count: > 0 } visible && visible.All(c => _selectedIds.Contains(c.Id));

    private void ToggleSelectAll(bool select)
    {
        if (select) _selectedIds = VisibleCompanies().Select(c => c.Id).ToHashSet();
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
