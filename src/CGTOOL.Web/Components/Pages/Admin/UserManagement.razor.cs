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

    /// <summary>Onboarding state by member, so the list can show where each person has got to
    /// without a query per row.</summary>
    private Dictionary<int, MemberOnboarding> _onboarding = [];
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

    private bool _bulkNotify;
    private bool _notifying;
    private string? _notifyProgress;

    /// <summary>The selected members who can actually be emailed. Counted up front so the dialog
    /// can say how many will be sent rather than discovering it halfway through.</summary>
    private List<Member> _notifiable =>
        _members is null
            ? []
            : [.. _members.Where(m => _selectedIds.Contains(m.Id) && !string.IsNullOrWhiteSpace(m.Email))];

    private bool _bulkDeclarations;
    private bool _bulkInsider;
    private bool _bulkCoi;
    private bool _bulkRpRegister;
    private bool _bulkRpTransactions;
    private Member? _printSingleRecord;

    /// <summary>The member an administrator is about to restart onboarding for. Confirmed first:
    /// it clears answers the member gave and cannot be undone from this screen.</summary>
    private Member? _restarting;
    private bool _restartBusy;
    private string? _restartNote;
    private bool _syncing;
    private bool _showUpload;
    private bool _importing;

    /// <summary>Which part of the upload is running, so the bar can say what it is waiting on
    /// rather than just spinning: the three phases have very different durations and only two of
    /// them can be measured.</summary>
    private enum ImportPhase { Idle, Uploading, Reading, LookingUp, Importing }

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
        ImportPhase.LookingUp => $"Looking up {_progressDone} of {_progressTotal} people in the directory",
        ImportPhase.Importing => $"Importing {_progressDone} of {_progressTotal} people",
        _ => string.Empty,
    };

    /// <summary>Reading the spreadsheet is one synchronous parse with nothing to count, so its bar
    /// is striped rather than claiming a position it does not know.</summary>
    private bool ProgressIsIndeterminate => _progressPhase == ImportPhase.Reading;

    /// <summary>A render per 64 KB chunk would be thousands of round trips over the circuit for one
    /// file and would itself slow the upload down. Ten a second is smooth to watch and cheap.</summary>
    private MemberOnboarding? OnboardingOf(Member member) =>
        _onboarding.TryGetValue(member.Id, out var row) ? row : null;

    /// <summary>What the list says about a person's onboarding.
    ///
    /// "Not required" is a real answer rather than a blank: somebody turned the flag off for that
    /// member, and a blank cell reads as "nobody has looked at this yet", which is the opposite.</summary>
    private string OnboardingLabel(MemberOnboarding? row)
    {
        if (row is null) return "Not started";

        return row.Status switch
        {
            OnboardingStatus.Completed when NeedsVerification(row) => "Needs checking",
            OnboardingStatus.Completed => "Done",
            OnboardingStatus.InProgress => "In progress",
            _ => "Not started",
        };
    }

    private string OnboardingPillClass(MemberOnboarding? row) => row?.Status switch
    {
        OnboardingStatus.Completed when NeedsVerification(row) => "cg-pill-warn",
        OnboardingStatus.Completed => "cg-pill-done",
        OnboardingStatus.InProgress => "cg-pill-status",
        _ => "",
    };

    /// <summary>The hover text, which is where the detail goes: the pill has room for two words
    /// and the thing an administrator actually wants to know is what is outstanding.</summary>
    private string OnboardingTitle(MemberOnboarding? row)
    {
        if (row is null) return "This member has not started onboarding.";

        var parts = new List<string>();
        if (row.EmiratesIdConfirmedAtUtc is { } eid) parts.Add($"Emirates ID confirmed {eid.ToLocalDisplay("dd/MM/yyyy HH:mm")}");
        if (row.EmiratesIdStatus == EmiratesIdOnboardingStatus.PendingVerification) parts.Add("Emirates ID supplied by the member, not yet checked");
        if (row.EmiratesIdReportedAtUtc is not null) parts.Add("Reported a problem with the Emirates ID on file");
        if (row.DfmConfirmedAtUtc is { } dfm) parts.Add($"DFM confirmed {dfm.ToLocalDisplay("dd/MM/yyyy HH:mm")}");
        if (row.DfmStatus == DfmOnboardingStatus.PendingVerification) parts.Add("NIN supplied by the member, not yet checked");
        if (row.NinResponse == NinResponse.NoNin) parts.Add("States they have no NIN");
        if (row.DfmReportedAtUtc is not null) parts.Add("Reported a problem with the DFM details on file");
        if (row.CompletedAtUtc is { } done) parts.Add($"Finished {done.ToLocalDisplay("dd/MM/yyyy HH:mm")}");

        return parts.Count == 0 ? "Onboarding started but nothing answered yet." : string.Join(". ", parts) + ".";
    }

    /// <summary>Whether anything the member supplied is still waiting for somebody to check it.
    /// Confirming what was already on file is not that; supplying something new is.</summary>
    private static bool NeedsVerification(MemberOnboarding row) =>
        (row.EmiratesIdStatus == EmiratesIdOnboardingStatus.PendingVerification && row.EmiratesIdVerifiedAtUtc is null)
        || (row.DfmStatus == DfmOnboardingStatus.PendingVerification && row.DfmVerifiedAtUtc is null)
        || (row.EmiratesIdReportedAtUtc is not null && row.EmiratesIdVerifiedAtUtc is null)
        || (row.DfmReportedAtUtc is not null && row.DfmVerifiedAtUtc is null);

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

                parsed = UserListWorkbookParser.Parse(buffer, file.Name, IdentityImporter.ExcludedDomains);
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

            // Before importing, not after: the directory's job title and department have to be on
            // the row for DirectoryImporter to write them, and its department is what creates the
            // department record. Filling afterwards would mean a second pass over everyone.
            var (people, enriched) = await FillFromDirectoryAsync(parsed.People);

            _progressPhase = ImportPhase.Importing;
            _progressDone = 0;
            _progressTotal = parsed.People.Count;
            await RenderNowAsync();

            var result = await DirectoryImporter.ImportAsync(
                people,
                state.User.Identity?.Name ?? "unknown",
                $"User list upload ({file.Name})",
                progress: new Progress<int>(n =>
                {
                    _progressDone = n;
                    _ = ThrottledRenderAsync();
                }),
                afterAccount: StoreDirectoryPhotoAsync);

            // The same file's Emirates ID and DFM columns, if it has them. One upload rather than
            // two screens: the spreadsheet that says who these people are is the spreadsheet that
            // says what we hold about them, and asking for it twice invites the two to drift.
            var identityNote = string.Empty;
            var identityProblems = new List<string>();
            if (parsed.Identities.Count > 0)
            {
                var identity = await IdentityImporter.ImportAsync(
                    parsed.Identities,
                    file.Name,
                    dryRun: false,
                    createMissingMembers: false,
                    state.User.Identity?.Name ?? "unknown");

                identityProblems = [.. identity.Problems];
                identityNote =
                    $" Emirates ID / NIN details: {identity.ReferencesInserted} added, {identity.ReferencesUpdated} updated.";
            }

            var directoryNote = enriched > 0
                ? $" {enriched} row(s) completed from the company directory."
                : string.Empty;
            var photoNote = result.PhotosStored > 0 ? $" {result.PhotosStored} profile picture(s) taken from the directory." : string.Empty;

            _importProblems = [.. parsed.Problems, .. result.Problems, .. identityProblems];
            _importResult =
                $"{file.Name}: {result.Created} added, {result.Updated} updated, {result.Skipped} skipped.{identityNote}{directoryNote}{photoNote}";

            Toasts.ShowSuccess(_importResult);
            foreach (var problem in _importProblems.Take(3)) Toasts.ShowWarning(problem);

            await LoadAsync();
        }
        catch (Exception ex)
        {
            // Without this the import ran inside a try/finally with no catch, so anything that
            // threw escaped the event handler, Blazor tore the circuit down, and the screen
            // simply stopped: the bar frozen at "0 of 2", the button still saying Importing, and
            // nothing anywhere saying what had gone wrong. A failed import has to say so.
            //
            // The message is shown rather than summarised. The ones that actually happen name
            // the thing to fix -- a stored procedure the database has not had applied yet says
            // so almost in those words -- and hiding that behind "the import failed" turns a
            // two-minute fix into an afternoon.
            Logger.LogError(ex, "User list import from {File} failed.", file.Name);
            _importResult = $"{file.Name} could not be imported: {ex.Message}";
            _importProblems = [_importResult];
            Toasts.ShowError(_importResult);
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

        // One query for the whole page rather than one per row.
        _onboarding = await db.MemberOnboardings.AsNoTracking().ToDictionaryAsync(o => o.MemberId);
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

    /// <summary>Turns declarations on for every selected member.
    ///
    /// Additive on purpose: a tick switches that declaration on, and an empty box leaves each
    /// record as it was. The alternative -- applying the dialog's state wholesale -- means one
    /// person opening it to add Insider Trading silently strips Conflict of Interest from
    /// everybody who already had it, and nothing on screen would say so.</summary>
    /// <summary>Sends the declaration email to everyone selected.
    ///
    /// One at a time with the count on screen, not a fan-out: Database Mail takes these one call
    /// at a time anyway, and a silent pause on a few thousand people reads as a hung page.</summary>
    private async Task BulkNotifyAsync()
    {
        if (_notifying) return;

        _notifying = true;
        var targets = _notifiable;
        var sent = 0;
        var failed = new List<string>();

        try
        {
            foreach (var member in targets)
            {
                _notifyProgress = $"Sending {sent + failed.Count + 1} of {targets.Count}: {member.FullName}";
                StateHasChanged();
                await Task.Yield();

                try
                {
                    if (await WelcomeEmailSender.SendWelcomeEmailAsync(member)) sent++;
                    else failed.Add(member.FullName);
                }
                catch (Exception)
                {
                    // One address that bounces must not stop the rest of the run. The name goes
                    // on screen afterwards, which is where whoever pressed Send is looking.
                    failed.Add(member.FullName);
                }
            }

            await AuditLog.LogAsync(await CurrentActorAsync(), AuditAction.Update, nameof(Member), null,
                $"Declaration email sent to {sent} member(s) in bulk" +
                (failed.Count > 0 ? $"; {failed.Count} could not be sent." : "."));

            Toasts.ShowSuccess($"{sent} email(s) sent.");
            if (failed.Count > 0)
            {
                Toasts.ShowWarning($"{failed.Count} could not be sent: {string.Join(", ", failed.Take(5))}" +
                    (failed.Count > 5 ? "…" : ""));
            }
        }
        finally
        {
            _notifying = false;
            _notifyProgress = null;
            _bulkNotify = false;
            _selectedIds.Clear();
        }
    }

    private async Task BulkSetDeclarationsAsync()
    {
        if (_members is null) return;

        var changed = 0;
        foreach (var id in _selectedIds.ToList())
        {
            var member = _members.FirstOrDefault(m => m.Id == id);
            if (member is null) continue;

            var before = (member.InsiderTradingAccess, member.ConflictOfInterestAccess,
                          member.RelatedPartyRegisterAccess, member.RelatedPartyTransactionAccess);

            if (_bulkInsider) member.InsiderTradingAccess = true;
            if (_bulkCoi) member.ConflictOfInterestAccess = true;
            if (_bulkRpRegister) member.RelatedPartyRegisterAccess = true;
            if (_bulkRpTransactions) member.RelatedPartyTransactionAccess = true;

            var after = (member.InsiderTradingAccess, member.ConflictOfInterestAccess,
                         member.RelatedPartyRegisterAccess, member.RelatedPartyTransactionAccess);
            if (before == after) continue;

            await MemberWriter.UpdateAsync(member);
            changed++;
        }

        var ticked = new List<string>();
        if (_bulkInsider) ticked.Add("Insider Trading");
        if (_bulkCoi) ticked.Add("Conflict of Interest");
        if (_bulkRpRegister) ticked.Add("Related Party Register");
        if (_bulkRpTransactions) ticked.Add("Related Party Transactions");

        await AuditLog.LogAsync(await CurrentActorAsync(), AuditAction.Update, nameof(Member), null,
            $"Set {string.Join(" and ", ticked)} on {changed} member(s) in bulk.");

        Toasts.ShowSuccess(changed == 0
            ? "Every selected member already had those."
            : $"{changed} member(s) updated.");

        _bulkDeclarations = false;
        _bulkInsider = _bulkCoi = _bulkRpRegister = _bulkRpTransactions = false;
        _selectedIds.Clear();
        await LoadAsync();
    }

    /// <summary>Puts a member back to the start of onboarding.
    ///
    /// Here rather than only as a SQL script because it is an ordinary administrative act -- a
    /// member whose card was replaced, or who was walked through it with the wrong details on
    /// file -- and asking an administrator for a database session to do it means it does not get
    /// done, or gets done with the flag and the record out of step.</summary>
    private async Task RestartOnboardingAsync()
    {
        if (_restarting is null || _restartBusy) return;

        _restartBusy = true;
        _restartNote = null;
        var member = _restarting;
        try
        {
            await Onboarding.RestartAsync(member.Id, await CurrentActorAsync());
            Toasts.ShowSuccess($"{member.FullName} will be asked to onboard again.");
            _restarting = null;
            await LoadAsync();
        }
        catch (Exception ex)
        {
            // Kept on the dialog rather than thrown: the administrator is standing in front of a
            // question they just answered, and a crash page loses which member it was about.
            _restartNote = $"That could not be done: {ex.Message}";
        }
        finally
        {
            _restartBusy = false;
        }
    }

    /// <summary>Switches every selected member active or inactive.
    ///
    /// No typed reason and no confirmation step: the button names the action, the bar beside it
    /// says how many are selected, and the opposite button undoes it. The audit entry is still
    /// written for each one.</summary>
    private async Task BulkSetActiveAsync(bool targetActive)
    {
        if (_members is null) return;

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

            await SetMemberActiveAsync(member, targetActive, justification: string.Empty);
            changed++;
        }

        if (changed > 0) Toasts.ShowSuccess($"{changed} user(s) {(targetActive ? "reactivated" : "deactivated")}.");
        if (blocked.Count > 0)
        {
            Toasts.ShowError($"{string.Join(", ", blocked)} {(blocked.Count == 1 ? "is an" : "are an")} approving or delegate authority on an entity. Reassign it on the entity first.");
        }

        _selectedIds.Clear();
        await LoadAsync();
    }

    /// <summary>Switches a member active or inactive straight from the list.
    ///
    /// No typed reason is asked for any more. The audit entry is still written, with who did it
    /// and when -- what has gone is the sentence explaining why, which was required on every
    /// flick of the toggle.
    ///
    /// The authority check below is not part of that and stays: a member who approves or holds
    /// delegate authority on an entity cannot be deactivated while they do, because the entity
    /// would be left with nobody able to approve and the first person to notice would be whoever
    /// could not submit.</summary>
    private async Task ToggleActiveAsync(Member member)
    {
        ArgumentNullException.ThrowIfNull(member);

        if (member.Active)
        {
            var holdings = await AuthorityHoldingsAsync(member.Id);
            if (holdings.Count > 0)
            {
                Toasts.ShowError($"{member.FullName} holds approving or delegate authority for {string.Join(", ", holdings)}. Reassign it on the entity before deactivating them.");
                return;
            }
        }

        await SetMemberActiveAsync(member, !member.Active, justification: string.Empty);

        Toasts.ShowSuccess($"{member.FullName} {(member.Active ? "reactivated" : "deactivated")}.");
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
                // Lockout stops the next sign-in; it does nothing to the cookie someone is already
                // holding. Rolling the security stamp is what ends a session in progress, at the
                // next stamp validation.
                await UserManager.UpdateSecurityStampAsync(linkedUser);
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

    /// <summary>Asks the directory about every row in the uploaded file and fills in what the
    /// spreadsheet left out, before any of it is written.
    ///
    /// The template is a list of people, not a personnel record, so rows arrive as a first name
    /// and an address: every person imported from one had no job title and no department until
    /// somebody opened their record and pressed "Fill from directory" by hand.
    ///
    /// Matched on identity only -- the Entra object id if the file somehow has one, otherwise the
    /// address. The name is deliberately not offered as a fallback: a bare first name matching one
    /// of several people who share it is exactly how one person's job title lands on another's
    /// record.</summary>
    private async Task<(IReadOnlyList<DirectoryPerson> People, int Enriched)> FillFromDirectoryAsync(
        IReadOnlyList<DirectoryPerson> people)
    {
        _progressPhase = ImportPhase.LookingUp;
        _progressDone = 0;
        _progressTotal = people.Count;
        await RenderNowAsync();

        var filled = new List<DirectoryPerson>(people.Count);
        var enriched = 0;

        foreach (var person in people)
        {
            _progressDone++;
            await ThrottledRenderAsync();

            try
            {
                var record = await DirectoryProvider.FindEmployeeAsync(
                    person.CompanyName, person.ObjectId, person.Address, fullName: null);

                if (record is null)
                {
                    filled.Add(person);
                    continue;
                }

                var merged = DirectoryEnrichment.Merge(person, record);
                if (DirectoryEnrichment.Changed(person, merged)) enriched++;
                filled.Add(merged);
            }
            catch (Exception ex)
            {
                // A directory that cannot answer must not stop the upload: the spreadsheet is
                // still a complete enough record to create the person from.
                Logger.LogWarning(ex, "Could not look up {DisplayName} in the directory.", person.DisplayName);
                filled.Add(person);
            }
        }

        return (filled, enriched);
    }

    /// <summary>Takes the person's photograph from the company directory after their sign-in
    /// account exists, so an uploaded list produces the same records a tenant sync would.
    ///
    /// A spreadsheet carries no object id, so the directory is asked who this row is before the
    /// photo can be asked for. Choosing a file on their record still replaces it afterwards --
    /// this writes a blob exactly as that does, so whichever happened last is what shows.</summary>
    private async Task<bool> StoreDirectoryPhotoAsync(DirectoryPerson person, ApplicationUser appUser, CancellationToken ct)
    {
        try
        {
            var objectId = person.ObjectId is { Length: > 0 } id
                ? id
                : (await DirectoryProvider.FindEmployeeAsync(
                    person.CompanyName, null, person.Mail ?? person.UserPrincipalName, person.DisplayName, ct))?.SystemId;

            if (string.IsNullOrWhiteSpace(objectId)) return false;

            var photo = await DirectoryProvider.GetPhotoAsync(objectId, ct);
            if (!photo.HasContent) return false;

            var stored = await DirectoryPhotos.StoreAsync(
                photo.Content!, photo.ContentType!, appUser, Documents, UserManager, ct);

            return stored != DirectoryPhotoResult.None;
        }
        catch (Exception ex)
        {
            // A directory that cannot answer must not fail the import: the person is already
            // created by this point, and a missing picture is not a reason to lose the row.
            Logger.LogWarning(ex, "Could not fetch the directory photo for {DisplayName}.", person.DisplayName);
            return false;
        }
    }
}
