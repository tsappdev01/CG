using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.JSInterop;
using CGTOOL.Web.Data;
using CGTOOL.Web.Data.Governance;

namespace CGTOOL.Web.Components.Pages.Admin;

public partial class UserManagement
{
    private static readonly int[] PageSizeOptions = [10, 25, 50, 100];

    public enum StatusFilter { Active, Recent, Inactive }

    // FRD §2.1: "the recency window for the Recent tab... is not yet defined" — default to 7 days,
    // with a 7/30-day toggle so this doesn't need a code change once Corporate Affairs confirms it.
    private static readonly int[] RecentWindowOptions = [7, 30];

    private List<Member>? _members;
    private List<ApplicationUser>? _users;
    private List<IdentityRole>? _roles;
    private List<Company>? _companies;
    private List<Department>? _departments;
    // Role names per login account, read straight from the Identity join tables in one pass --
    // UserManager.GetRolesAsync would be a query per row.
    private Dictionary<string, List<string>> _userRoles = [];
    private int _departmentFilter;
    private string _roleFilter = string.Empty;
    private int _pageSize = 10;

    /// <summary>Seeds the search box from the top bar's global search (?q=...).</summary>
    [SupplyParameterFromQuery(Name = "q")]
    private string? Query { get; set; }
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
    private bool _syncing;
    private bool _showUpload;
    private bool _importing;

    /// <summary>Which part of the upload is running, so the bar can say what it is waiting on
    /// rather than just spinning: the three phases have very different durations and only two of
    /// them can be measured.</summary>
    private enum ImportPhase { Idle, Uploading, Reading, Importing }

    private ImportPhase _progressPhase = ImportPhase.Idle;
    private string? _progressName;
    private long _progressDone;
    private long _progressTotal;
    private DateTime _lastRenderUtc = DateTime.MinValue;

    private int ProgressPercent => _progressTotal <= 0 ? 0 : (int)Math.Min(100, _progressDone * 100 / _progressTotal);

    private string ProgressLabel => _progressPhase switch
    {
        ImportPhase.Uploading => $"Uploading {_progressName}",
        ImportPhase.Reading => "Reading the spreadsheet",
        ImportPhase.Importing => $"Importing {_progressDone} of {_progressTotal} people",
        _ => string.Empty,
    };

    /// <summary>Reading the spreadsheet is one synchronous parse with nothing to count, so its bar
    /// is striped rather than claiming a position it does not know.</summary>
    private bool ProgressIsIndeterminate => _progressPhase == ImportPhase.Reading;

    /// <summary>A render per 64 KB chunk would be thousands of round trips over the circuit for one
    /// file and would itself slow the upload down. Ten a second is smooth to watch and cheap.</summary>
    private async Task ThrottledRenderAsync()
    {
        var now = DateTime.UtcNow;
        if ((now - _lastRenderUtc).TotalMilliseconds < 100) return;
        _lastRenderUtc = now;
        await RenderNowAsync();
    }

    private async Task RenderNowAsync()
    {
        _lastRenderUtc = DateTime.UtcNow;
        await InvokeAsync(StateHasChanged);
        // Hands the circuit back so the frame is actually sent before the next chunk is read;
        // without it the whole copy can run to completion inside one continuation and the bar
        // jumps from nothing to done.
        await Task.Yield();
    }
    private string? _importResult;
    private List<string> _importProblems = [];

    private async Task PrintAsync() => await JS.InvokeVoidAsync("print");

    /// <summary>Reloads the member directory from Entra ID. Entra is the system of record for who
    /// exists and what their profile says, so this refreshes names, addresses, entities,
    /// departments, designations and photos; the governance flags an administrator set here are
    /// left alone.</summary>
    /// <summary>
    /// The spreadsheet route into the same import Entra ID uses, for a deployment with no tenant
    /// connection. Parsing problems and import problems are shown together, per row, because a file
    /// assembled by hand is usually half right and saying which half is the useful part.
    /// </summary>
    private async Task OnUserListSelectedAsync(InputFileChangeEventArgs e)
    {
        if (_importing) return;

        var file = e.File;
        if (file is null) return;

        _importing = true;
        _importResult = null;
        _importProblems = [];
        _progressPhase = ImportPhase.Uploading;
        _progressName = file.Name;
        _progressDone = 0;
        _progressTotal = file.Size;
        StateHasChanged();

        try
        {
            UserListParseResult parsed;
            try
            {
                // Read fully into memory first: ExcelDataReader seeks, and the browser file stream
                // does not. 10 MB is far beyond any plausible staff list.
                using var buffer = new MemoryStream();
                await using (var source = file.OpenReadStream(maxAllowedSize: 10 * 1024 * 1024))
                {
                    // Copied a chunk at a time rather than with CopyToAsync so the bar can move: the
                    // browser sends the file over the circuit in pieces, and this is the only point
                    // that knows how much has arrived.
                    var chunk = new byte[64 * 1024];
                    int read;
                    while ((read = await source.ReadAsync(chunk)) > 0)
                    {
                        await buffer.WriteAsync(chunk.AsMemory(0, read));
                        _progressDone += read;
                        await ThrottledRenderAsync();
                    }
                }
                buffer.Position = 0;

                _progressPhase = ImportPhase.Reading;
                await RenderNowAsync();

                parsed = UserListWorkbookParser.Parse(buffer, file.Name);
            }
            catch (Exception ex)
            {
                // A file that cannot be read at all fails here, before anything is written.
                Toasts.ShowError($"Could not read {file.Name}: {ex.Message}");
                _importResult = $"{file.Name} could not be read.";
                return;
            }

            if (parsed.People.Count == 0)
            {
                _importProblems = parsed.Problems;
                _importResult = $"No usable rows in {file.Name} — nothing was imported.";
                Toasts.ShowError(_importResult);
                return;
            }

            var state = await AuthState.GetAuthenticationStateAsync();

            _progressPhase = ImportPhase.Importing;
            _progressDone = 0;
            _progressTotal = parsed.People.Count;
            await RenderNowAsync();

            var result = await DirectoryImporter.ImportAsync(
                parsed.People,
                state.User.Identity?.Name ?? "unknown",
                $"User list upload ({file.Name})",
                progress: new Progress<int>(n =>
                {
                    _progressDone = n;
                    _ = ThrottledRenderAsync();
                }));

            _importProblems = [.. parsed.Problems, .. result.Problems];
            _importResult =
                $"{file.Name}: {result.Created} added, {result.Updated} updated, {result.Skipped} skipped.";

            Toasts.ShowSuccess(_importResult);
            foreach (var problem in _importProblems.Take(3)) Toasts.ShowWarning(problem);

            await LoadAsync();
        }
        finally
        {
            _importing = false;
            _progressPhase = ImportPhase.Idle;
        }
    }

    private async Task SyncFromEntraAsync()
    {
        if (_syncing) return;
        _syncing = true;
        try
        {
            var state = await AuthState.GetAuthenticationStateAsync();
            var result = await EntraSync.SyncAsync(state.User.Identity?.Name ?? "unknown");

            if (result.Total == 0 && result.Problems.Count > 0)
            {
                Toasts.ShowError(result.Problems[0]);
            }
            else
            {
                Toasts.ShowSuccess(
                    $"Entra ID sync complete — {result.Created} added, {result.Updated} updated, " +
                    $"{result.PhotosStored} photo(s), {result.Skipped} skipped.");

                foreach (var problem in result.Problems.Take(3))
                {
                    Toasts.ShowWarning(problem);
                }
            }

            await LoadAsync();
        }
        finally
        {
            _syncing = false;
        }
    }

    protected override async Task OnInitializedAsync()
    {
        if (!string.IsNullOrWhiteSpace(Query)) _search = Query.Trim();
        await LoadAsync();
    }

    private async Task LoadAsync()
    {
        // Its own short-lived context rather than the circuit-scoped ApplicationDbContext:
        // sharing that one lets this race, or outlive, whatever else in the circuit is using
        // it -- surfacing as "A second operation was started on this context instance" or
        // "Cannot access a disposed context instance". Same reason NavMenu and TopBar do it.
        await using var db = await DbFactory.CreateDbContextAsync();

        _members = await db.Members.Include(m => m.Company).Include(m => m.Department).OrderBy(m => m.FullName).ToListAsync();
        _users = await UserManager.Users.OrderBy(u => u.Email).ToListAsync();
        _roles = await RoleManager.Roles.OrderBy(r => r.Name).ToListAsync();
        _companies = await db.Companies.OrderBy(c => c.Name).ToListAsync();
        _departments = await db.Departments.OrderBy(d => d.Name).ToListAsync();

        var roleNamesById = await db.Roles.AsNoTracking().ToDictionaryAsync(r => r.Id, r => r.Name ?? string.Empty);
        _userRoles = (await db.UserRoles.AsNoTracking().ToListAsync())
            .GroupBy(ur => ur.UserId)
            .ToDictionary(
                g => g.Key,
                g => g.Select(ur => roleNamesById.TryGetValue(ur.RoleId, out var n) ? n : string.Empty)
                      .Where(n => n.Length > 0)
                      .OrderBy(n => n)
                      .ToList());
    }

    /// <summary>System roles held by the member's linked login account, if it has one.</summary>
    private List<string> RolesOf(Member m) =>
        m.ApplicationUserId is not null && _userRoles.TryGetValue(m.ApplicationUserId, out var r) ? r : [];

    private bool HasActiveFilters =>
        !string.IsNullOrWhiteSpace(_search) || _companyFilter != 0 || _departmentFilter != 0 || _roleFilter.Length > 0;

    private void ClearFilters()
    {
        _search = string.Empty;
        _companyFilter = 0;
        _departmentFilter = 0;
        _roleFilter = string.Empty;
        ResetToFirstPage();
    }

    private void SetDepartmentFilter(int departmentId)
    {
        _departmentFilter = departmentId;
        ResetToFirstPage();
    }

    private void SetRoleFilter(string role)
    {
        _roleFilter = role;
        ResetToFirstPage();
    }

    private void SetPageSize(int size)
    {
        _pageSize = size;
        ResetToFirstPage();
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

        if (_departmentFilter != 0)
        {
            query = query.Where(m => m.DepartmentId == _departmentFilter);
        }

        if (_roleFilter.Length > 0)
        {
            query = query.Where(m => RolesOf(m).Contains(_roleFilter));
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
            // Sorts on what the column shows -- it used to show the short code.
            "Entity" => query.OrderBy(m => m.Company?.Name),
            "Department" => query.OrderBy(m => m.Department?.Name),
            "CreatedOn" => query.OrderBy(m => m.CreatedAtUtc),
            "ModifiedOn" => query.OrderBy(m => m.ModifiedAtUtc),
            "Active" => query.OrderBy(m => m.Active),
            _ => query.OrderBy(m => m.FullName),
        };
        return (_sortAscending ? sorted : sorted.Reverse()).ToList();
    }

    private List<Member> PagedMembers() =>
        FilteredMembers().Skip((_page - 1) * _pageSize).Take(_pageSize).ToList();

    private int TotalPages => Math.Max(1, (int)Math.Ceiling(FilteredMembers().Count / (double)_pageSize));

    private int FirstRowOnPage => FilteredMembers().Count == 0 ? 0 : ((_page - 1) * _pageSize) + 1;

    private int LastRowOnPage => Math.Min(_page * _pageSize, FilteredMembers().Count);

    /// <summary>At most seven page buttons, centred on the current page, so the pager stays a fixed
    /// width however many users there are.</summary>
    private IEnumerable<int> PageNumbers()
    {
        const int window = 7;
        var total = TotalPages;
        if (total <= window) return Enumerable.Range(1, total);

        var start = Math.Max(1, Math.Min(_page - window / 2, total - window + 1));
        return Enumerable.Range(start, window);
    }

    /// <summary>Stable tint per person so the same name always gets the same avatar colour.</summary>
    private static string AvatarTint(string name)
    {
        if (string.IsNullOrEmpty(name)) return "cg-av-1";
        var sum = name.Sum(c => (int)c);
        return $"cg-av-{(sum % 6) + 1}";
    }

    /// <summary>Administrator is the emphasised pill in the design; everything else cycles through
    /// the remaining tints so custom roles are still told apart at a glance.</summary>
    private static string RolePillClass(string? role) => role switch
    {
        null or "" => "cg-pill-neutral",
        GovernanceRoles.Administrator => "cg-pill-blue",
        GovernanceRoles.NormalUser => "cg-pill-neutral",
        _ => (role.Sum(c => (int)c) % 3) switch
        {
            0 => "cg-pill-violet",
            1 => "cg-pill-green",
            _ => "cg-pill-amber",
        },
    };

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

    /// <summary>The entities that hold this user as their approving or delegate authority. A user
    /// who is one cannot be removed while that is true: the Company row referring to them is a child
    /// record, the database refuses the delete outright (the foreign keys are Restrict), and taking
    /// the person out of service some other way would break RP Transaction approval for that entity
    /// without saying so -- the failure would only surface when someone tried to submit one.
    ///
    /// Reassign the authority on the entity first, then the user can be deactivated.</summary>
    private async Task<List<string>> AuthorityHoldingsAsync(int memberId)
    {
        await using var db = await DbFactory.CreateDbContextAsync();
        return await db.Companies
            .AsNoTracking()
            .Where(c => c.ApprovingAuthorityMemberId == memberId || c.DelegateAuthorityMemberId == memberId)
            .OrderBy(c => c.Name)
            .Select(c => c.Name)
            .ToListAsync();
    }

    private async Task BulkSetActiveAsync(string justification)
    {
        if (_bulkSetActive is not { } targetActive || _members is null) return;

        var changed = 0;
        var blocked = new List<string>();

        foreach (var id in _selectedIds.ToList())
        {
            var member = _members.FirstOrDefault(m => m.Id == id);
            if (member is null || member.Active == targetActive) continue;

            if (!targetActive && (await AuthorityHoldingsAsync(member.Id)).Count > 0)
            {
                blocked.Add(member.FullName);
                continue;
            }

            await SetMemberActiveAsync(member, targetActive, justification);
            changed++;
        }

        if (changed > 0) Toasts.ShowSuccess($"{changed} user(s) {(targetActive ? "reactivated" : "deactivated")}.");
        if (blocked.Count > 0)
        {
            Toasts.ShowError($"{string.Join(", ", blocked)} {(blocked.Count == 1 ? "is an" : "are an")} approving or delegate authority on an entity. Reassign it on the entity first.");
        }

        _bulkSetActive = null;
        _selectedIds.Clear();
        await LoadAsync();
    }

    private async Task ToggleActiveAsync(string justification)
    {
        if (_pendingJustify is null) return;

        var member = _pendingJustify;

        if (member.Active)
        {
            var holdings = await AuthorityHoldingsAsync(member.Id);
            if (holdings.Count > 0)
            {
                Toasts.ShowError($"{member.FullName} holds approving or delegate authority for {string.Join(", ", holdings)}. Reassign it on the entity before deactivating them.");
                _pendingJustify = null;
                return;
            }
        }

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
