using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.JSInterop;
using CGTOOL.Web.Data;
using CGTOOL.Web.Data.Governance;

namespace CGTOOL.Web.Components.Pages.Admin;

public partial class UserManagement
{
    private const int PageSize = 10;

    public enum StatusFilter { Active, Recent, Inactive }

    // FRD §2.1: "the recency window for the Recent tab... is not yet defined" — default to 7 days,
    // with a 7/30-day toggle so this doesn't need a code change once Corporate Affairs confirms it.
    private static readonly int[] RecentWindowOptions = [7, 30];

    private List<Member>? _members;
    private List<ApplicationUser>? _users;
    private List<IdentityRole>? _roles;
    private List<Company>? _companies;
    private string? _roleToDelete;
    private string _search = string.Empty;
    private string _newRoleName = string.Empty;
    private StatusFilter _statusFilter = StatusFilter.Active;
    private int _recentDays = 7;
    private int _page = 1;
    private int _companyFilter;
    private string _sortColumn = "CreatedOn";
    private bool _sortAscending = false;
    private HashSet<int> _selectedIds = [];
    private bool? _bulkSetActive;
    private Member? _pendingJustify;
    private Member? _printSingleRecord;

    private async Task PrintAsync() => await JS.InvokeVoidAsync("print");

    protected override async Task OnInitializedAsync() => await LoadAsync();

    private async Task LoadAsync()
    {
        _members = await Db.Members.Include(m => m.Company).Include(m => m.Department).OrderBy(m => m.FullName).ToListAsync();
        _users = await UserManager.Users.OrderBy(u => u.Email).ToListAsync();
        _roles = await RoleManager.Roles.OrderBy(r => r.Name).ToListAsync();
        _companies = await Db.Companies.OrderBy(c => c.Name).ToListAsync();
    }

    private List<Member> FilteredMembers()
    {
        if (_members is null) return [];

        IEnumerable<Member> query = _members;

        query = _statusFilter switch
        {
            StatusFilter.Active => query.Where(m => m.Active),
            StatusFilter.Recent => query.Where(IsRecent),
            _ => query.Where(m => !m.Active),
        };

        if (_companyFilter != 0)
        {
            query = query.Where(m => m.CompanyId == _companyFilter);
        }

        if (!string.IsNullOrWhiteSpace(_search))
        {
            var q = _search.Trim();
            query = query.Where(m =>
                m.FullName.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                (m.Email?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (m.JobTitle?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false));
        }

        IOrderedEnumerable<Member> sorted = _sortColumn switch
        {
            "Entity" => query.OrderBy(m => m.Company?.ShortCode),
            "Department" => query.OrderBy(m => m.Department?.Name),
            "CreatedOn" => query.OrderBy(m => m.CreatedAtUtc),
            "ModifiedOn" => query.OrderBy(m => m.ModifiedAtUtc),
            "Active" => query.OrderBy(m => m.Active),
            _ => query.OrderBy(m => m.FullName),
        };
        return (_sortAscending ? sorted : sorted.Reverse()).ToList();
    }

    private List<Member> PagedMembers() =>
        FilteredMembers().Skip((_page - 1) * PageSize).Take(PageSize).ToList();

    private int TotalPages => Math.Max(1, (int)Math.Ceiling(FilteredMembers().Count / (double)PageSize));

    private void Sort(string column)
    {
        if (_sortColumn == column) _sortAscending = !_sortAscending;
        else { _sortColumn = column; _sortAscending = true; }
    }

    private bool AllVisibleSelected => PagedMembers().Count > 0 && PagedMembers().All(m => _selectedIds.Contains(m.Id));

    private void ToggleSelectAllVisible(bool select)
    {
        foreach (var m in PagedMembers())
        {
            if (select) _selectedIds.Add(m.Id);
            else _selectedIds.Remove(m.Id);
        }
    }

    private void ToggleSelect(int id, bool select)
    {
        if (select) _selectedIds.Add(id);
        else _selectedIds.Remove(id);
    }

    private void DeselectAll() => _selectedIds.Clear();

    private async Task BulkSetActiveAsync(string justification)
    {
        if (_bulkSetActive is not { } targetActive || _members is null) return;

        foreach (var id in _selectedIds.ToList())
        {
            var member = _members.FirstOrDefault(m => m.Id == id);
            if (member is null || member.Active == targetActive) continue;
            await SetMemberActiveAsync(member, targetActive, justification);
        }

        Toasts.ShowSuccess($"{_selectedIds.Count} user(s) {(targetActive ? "reactivated" : "deactivated")}.");
        _bulkSetActive = null;
        _selectedIds.Clear();
        await LoadAsync();
    }

    private async Task ToggleActiveAsync(string justification)
    {
        if (_pendingJustify is null) return;

        var member = _pendingJustify;
        await SetMemberActiveAsync(member, !member.Active, justification);

        Toasts.ShowSuccess($"{member.FullName} {(member.Active ? "reactivated" : "deactivated")}.");
        _pendingJustify = null;
        await LoadAsync();
    }

    private async Task SetMemberActiveAsync(Member member, bool targetActive, string justification)
    {
        member.Active = targetActive;
        await MemberWriter.SetActiveAsync(member.Id, targetActive);

        if (!string.IsNullOrEmpty(member.ApplicationUserId))
        {
            var linkedUser = await UserManager.FindByIdAsync(member.ApplicationUserId);
            if (linkedUser is not null)
            {
                await UserManager.SetLockoutEnabledAsync(linkedUser, true);
                await UserManager.SetLockoutEndDateAsync(linkedUser, targetActive ? null : DateTimeOffset.MaxValue);
            }
        }

        await AuditLog.LogAsync(
            await CurrentActorAsync(),
            targetActive ? AuditAction.Reactivate : AuditAction.Deactivate,
            nameof(Member), member.Id.ToString(), member.FullName,
            justification: justification);
    }

    private bool IsRecent(Member m)
    {
        var cutoff = DateTime.UtcNow.AddDays(-_recentDays);
        return m.CreatedAtUtc >= cutoff || m.ModifiedAtUtc >= cutoff;
    }

    private static string RecencyLabel(Member m) => m.ModifiedAtUtc > m.CreatedAtUtc
        ? $"Modified {m.ModifiedAtUtc.ToLocalDisplay():yyyy-MM-dd}"
        : $"Added {m.CreatedAtUtc.ToLocalDisplay():yyyy-MM-dd}";

    private string StatusFilterLabel() => _statusFilter switch
    {
        StatusFilter.Active => "active",
        StatusFilter.Recent => $"recent (last {_recentDays} days)",
        _ => "inactive",
    };

    private void ResetToFirstPage() => _page = 1;

    private void SetStatusFilter(StatusFilter filter)
    {
        _statusFilter = filter;
        ResetToFirstPage();
    }

    private void SetRecentWindow(int days)
    {
        _recentDays = days;
        ResetToFirstPage();
    }

    private void SetCompanyFilter(int companyId)
    {
        _companyFilter = companyId;
        ResetToFirstPage();
    }

    private void GoToPage(int page) => _page = page;

    private void AddNew() => Nav.NavigateTo("/admin/user-management/member/0");

    private void Edit(Member member) => Nav.NavigateTo($"/admin/user-management/member/{member.Id}");

    private async Task CreateRoleAsync()
    {
        var name = _newRoleName.Trim();
        if (string.IsNullOrEmpty(name))
        {
            Toasts.ShowError("Role name is required.");
            return;
        }

        if (await RoleManager.RoleExistsAsync(name))
        {
            Toasts.ShowError($"Role '{name}' already exists.");
            return;
        }

        var result = await RoleManager.CreateAsync(new IdentityRole(name));
        if (!result.Succeeded)
        {
            Toasts.ShowError(string.Join(" ", result.Errors.Select(e => e.Description)));
            return;
        }

        await AuditLog.LogAsync(await CurrentActorAsync(), AuditAction.Create, "Role", name, $"Role '{name}' created");
        Toasts.ShowSuccess($"Role '{name}' created.");
        _newRoleName = string.Empty;
        await LoadAsync();
    }

    private async Task DeleteRoleAsync(string justification)
    {
        var roleName = _roleToDelete;
        if (roleName is null) return;

        if (GovernanceRoles.All.Contains(roleName))
        {
            Toasts.ShowError($"'{roleName}' is a built-in system role and cannot be deleted.");
            _roleToDelete = null;
            return;
        }

        var role = await RoleManager.FindByNameAsync(roleName);
        if (role is null) { _roleToDelete = null; return; }

        var usersInRole = await UserManager.GetUsersInRoleAsync(roleName);
        if (usersInRole.Count > 0)
        {
            Toasts.ShowError($"Cannot delete '{roleName}': {usersInRole.Count} user(s) still have this role.");
            _roleToDelete = null;
            return;
        }

        await RoleManager.DeleteAsync(role);
        await AuditLog.LogAsync(await CurrentActorAsync(), AuditAction.Delete, "Role", roleName, $"Role '{roleName}' deleted", justification: justification);
        Toasts.ShowSuccess($"Role '{roleName}' deleted.");
        _roleToDelete = null;
        await LoadAsync();
    }

    private async Task<string> CurrentActorAsync()
    {
        var state = await AuthState.GetAuthenticationStateAsync();
        return state.User.Identity?.Name ?? "unknown";
    }
}
