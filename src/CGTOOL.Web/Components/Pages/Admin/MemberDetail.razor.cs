using System.Security.Claims;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.JSInterop;
using CGTOOL.Web.Data;
using CGTOOL.Web.Data.Governance;

namespace CGTOOL.Web.Components.Pages.Admin;

public partial class MemberDetail
{
    [Parameter] public int Id { get; set; }

    private List<Company>? _companies;
    private List<Department>? _departments;
    private List<Member>? _members;
    private List<ApplicationUser>? _users;
    private List<IdentityRole>? _roles;
    private Member? _editing;
    private bool _adSynced;
    private bool _showDeactivateJustify;
    private bool _showNotifyPrompt;
    private HashSet<int> _approvedImpersonatorIds = [];
    private HashSet<string> _selectedUserRoles = [];
    private string _systemRoleSelection = string.Empty;
    private List<AuditLogEntry> _auditEntries = [];
    private string? _currentUserId;
    private string? _profilePicturePath;
    private bool _showPrintPreview;
    private bool _saving;
    private List<DeclarationDocumentRow> _documents = [];
    private string _documentsTab = "latest";

    // Every Emirates ID / Passport / Trade Licence / Other Document a member has ever uploaded through
    // an Insider Trading declaration, surfaced here so an admin doesn't have to hunt through individual
    // declaration submissions to find a document on file for this user.
    private record DeclarationDocumentRow(string DocumentType, string Path, string? Number, DateTime? ExpiryDate, string PeriodLabel);

    // _documents is built newest-declaration-first (LoadDocumentsAsync), so within each DocumentType
    // group the first row is inherently the most recent one on file -- everything after it is
    // superseded history.
    private List<DeclarationDocumentRow> LatestDocuments() => _documents
        .GroupBy(d => d.DocumentType)
        .Select(g => g.First())
        .ToList();

    private List<DeclarationDocumentRow> PreviousDocuments() => _documents
        .GroupBy(d => d.DocumentType)
        .SelectMany(g => g.Skip(1))
        .ToList();

    private List<DeclarationDocumentRow> DisplayedDocuments() => _documentsTab == "previous" ? PreviousDocuments() : LatestDocuments();

    private void OnRoleAssignedChanged(ChangeEventArgs e)
    {
        if (_editing is null) return;
        var selected = ((string[])e.Value!).ToHashSet();
        _editing.InsiderTradingAccess = selected.Contains("InsiderTrading");
        _editing.ConflictOfInterestAccess = selected.Contains("ConflictOfInterest");
        _editing.RelatedPartyRegisterAccess = selected.Contains("RelatedPartyRegister");
        _editing.RelatedPartyTransactionAccess = selected.Contains("RelatedPartyTransaction");
    }

    private string RoleAssignedSummary
    {
        get
        {
            if (_editing is null) return "—";
            var roles = new List<string>();
            if (_editing.InsiderTradingAccess) roles.Add("Insider Trading");
            if (_editing.ConflictOfInterestAccess) roles.Add("Conflict of Interest");
            if (_editing.RelatedPartyRegisterAccess) roles.Add("Related Party Register");
            if (_editing.RelatedPartyTransactionAccess) roles.Add("Related Party Transaction");
            return roles.Count > 0 ? string.Join(", ", roles) : "None";
        }
    }

    private void ShowNotifyPrompt()
    {
        if (_editing is null || _editing.Id == 0 || string.IsNullOrWhiteSpace(_editing.Email)) return;
        _showNotifyPrompt = true;
    }

    private async Task PrintAsync() => await JS.InvokeVoidAsync("print");

    private const long MaxProfilePictureBytes = 2 * 1024 * 1024;

    protected override async Task OnInitializedAsync()
    {
        var state = await AuthState.GetAuthenticationStateAsync();
        _currentUserId = state.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        _companies = await Db.Companies.OrderBy(c => c.Name).ToListAsync();
        _departments = await Db.Departments.OrderBy(d => d.Name).ToListAsync();
        _members = await Db.Members.Include(m => m.Company).Include(m => m.Department).OrderBy(m => m.FullName).ToListAsync();
        _users = await UserManager.Users.OrderBy(u => u.Email).ToListAsync();
        _roles = await RoleManager.Roles.OrderBy(r => r.Name).ToListAsync();

        if (Id == 0)
        {
            _editing = new Member { Active = true };
            _adSynced = false;
            _approvedImpersonatorIds = [];
            _selectedUserRoles = [];
            _auditEntries = [];
            _documents = [];
            return;
        }

        var member = _members.FirstOrDefault(m => m.Id == Id);
        if (member is null)
        {
            Toasts.ShowError("That user could not be found.");
            Nav.NavigateTo("/admin/user-management");
            return;
        }

        _editing = member;
        _approvedImpersonatorIds = (await Db.MemberImpersonationApprovals
            .Where(a => a.MemberId == member.Id)
            .Select(a => a.ImpersonatorId)
            .ToListAsync()).ToHashSet();

        if (member.CompanyId != 0)
        {
            var company = _companies.First(c => c.Id == member.CompanyId);
            _adSynced = await DirectoryProvider.IsSyncedAsync(company);
        }

        await LoadAuditAsync();
        await LoadDocumentsAsync();
    }

    private void GoBack() => Nav.NavigateTo("/admin/user-management");

    private async Task LoadDocumentsAsync()
    {
        _documents = [];
        if (_editing is null || _editing.Id == 0) return;

        var declarations = await Db.InsiderDeclarations
            .AsNoTracking()
            .Include(d => d.DeclarationCycleRun)
            .Where(d => d.MemberId == _editing.Id)
            .OrderByDescending(d => d.SubmittedAtUtc)
            .ToListAsync();

        foreach (var d in declarations)
        {
            var period = d.DeclarationCycleRun is not null ? $"Q{d.DeclarationCycleRun.PeriodQuarter} {d.DeclarationCycleRun.PeriodYear}" : "—";

            if (!string.IsNullOrEmpty(d.EmiratesIdPath))
                _documents.Add(new("Emirates ID", d.EmiratesIdPath, d.EmiratesIdNumber, d.EmiratesIdExpiryDate, period));
            if (!string.IsNullOrEmpty(d.PassportPath))
                _documents.Add(new("Passport", d.PassportPath, d.PassportNumber, d.PassportExpiryDate, period));
            if (!string.IsNullOrEmpty(d.TradeLicencePath))
                _documents.Add(new("Trade Licence", d.TradeLicencePath, d.TradeLicenceNumber, d.TradeLicenceExpiryDate, period));
            if (!string.IsNullOrEmpty(d.OtherDocumentPath))
                _documents.Add(new("Other Document", d.OtherDocumentPath, null, null, period));
        }
    }

    private async Task LoadAuditAsync()
    {
        _selectedUserRoles = [];
        _systemRoleSelection = string.Empty;
        _profilePicturePath = null;
        if (_editing is not null && !string.IsNullOrEmpty(_editing.ApplicationUserId))
        {
            var user = _users!.FirstOrDefault(u => u.Id == _editing.ApplicationUserId);
            if (user is not null)
            {
                _selectedUserRoles = (await UserManager.GetRolesAsync(user)).ToHashSet();
                _systemRoleSelection = _selectedUserRoles.FirstOrDefault() ?? string.Empty;
                _profilePicturePath = user.ProfilePicturePath;
            }
        }

        if (_editing is null || _editing.Id == 0)
        {
            _auditEntries = [];
            return;
        }

        var memberId = _editing.Id.ToString();
        var linkedUserId = _editing.ApplicationUserId;

        _auditEntries = await Db.AuditLogEntries
            .Where(a => (a.EntityType == nameof(Member) && a.EntityId == memberId)
                     || (linkedUserId != null && a.EntityType == nameof(ApplicationUser) && a.EntityId == linkedUserId))
            .OrderByDescending(a => a.OccurredAtUtc)
            .Take(50)
            .ToListAsync();
    }

    private async Task OnProfilePictureSelectedAsync(InputFileChangeEventArgs e)
    {
        if (_editing is null || string.IsNullOrEmpty(_editing.ApplicationUserId)) return;

        var file = e.File;
        var extension = file.ContentType switch
        {
            "image/png" => ".png",
            "image/webp" => ".webp",
            "image/jpeg" => ".jpg",
            _ => null,
        };
        if (extension is null)
        {
            Toasts.ShowError("Only JPEG, PNG, or WebP images are supported.");
            return;
        }

        if (file.Size > MaxProfilePictureBytes)
        {
            Toasts.ShowError("Profile picture must be 2 MB or smaller.");
            return;
        }

        // Re-fetch rather than reuse the _users list's snapshot (loaded once, at page load) -- a
        // stale ConcurrencyStamp on that in-memory copy would make UpdateAsync below fail every time,
        // and since its result was never checked, that failure was silently swallowed while the code
        // still showed a "Profile picture updated" success toast regardless.
        var user = await UserManager.FindByIdAsync(_editing.ApplicationUserId);
        if (user is null) return;

        var uploadsDir = Path.Combine(Env.WebRootPath, "uploads", "profile-pictures");
        Directory.CreateDirectory(uploadsDir);

        foreach (var existing in Directory.GetFiles(uploadsDir, $"{user.Id}.*"))
        {
            File.Delete(existing);
        }

        var fileName = $"{user.Id}{extension}";
        var filePath = Path.Combine(uploadsDir, fileName);

        await using (var stream = file.OpenReadStream(MaxProfilePictureBytes))
        await using (var target = File.Create(filePath))
        {
            await stream.CopyToAsync(target);
        }

        user.ProfilePicturePath = $"/uploads/profile-pictures/{fileName}";
        var result = await UserManager.UpdateAsync(user);
        if (!result.Succeeded)
        {
            Toasts.ShowError($"Could not save the profile picture: {string.Join(" ", result.Errors.Select(e => e.Description))}");
            return;
        }

        // Keep the page's in-memory snapshot consistent with what was just persisted, in case
        // anything else on this page re-reads _users before the next full reload.
        var cachedUser = _users!.FirstOrDefault(u => u.Id == user.Id);
        if (cachedUser is not null) cachedUser.ProfilePicturePath = user.ProfilePicturePath;

        _profilePicturePath = $"{user.ProfilePicturePath}?v={DateTime.UtcNow.Ticks}";

        await AuditLog.LogAsync(await CurrentActorAsync(), AuditAction.Update, nameof(ApplicationUser), user.Id, "Profile picture updated");
        Toasts.ShowSuccess("Profile picture updated.");
    }

    private async Task OnCompanyChangedAsync()
    {
        if (_editing is null || _editing.CompanyId == 0) return;
        var company = _companies!.First(c => c.Id == _editing.CompanyId);
        _adSynced = await DirectoryProvider.IsSyncedAsync(company);
    }

    private async Task LoadFromDirectoryAsync()
    {
        if (_editing is null || _editing.CompanyId == 0) return;
        var company = _companies!.First(c => c.Id == _editing.CompanyId);
        var records = await DirectoryProvider.GetEmployeesAsync(company);
        var record = records.FirstOrDefault();
        if (record is null)
        {
            Toasts.ShowError("No Azure AD records were returned for this entity.");
            return;
        }

        _editing.FullName = record.FullName;
        _editing.JobTitle = record.JobTitle;
        _editing.Email = record.Email;
        _editing.Signature = record.Signature;
        _editing.AzureAdObjectId = record.SystemId;
        if (record.DepartmentName is not null)
        {
            var dept = _departments!.FirstOrDefault(d => d.Name.Equals(record.DepartmentName, StringComparison.OrdinalIgnoreCase));
            if (dept is not null) _editing.DepartmentId = dept.Id;
        }
    }

    // A <select multiple> firing several change events in quick succession (rapid ctrl/shift-click, or
    // a slow round-trip prompting a second click before the first finishes) can dispatch a second call
    // into these handlers while the first is still mid-await -- both would then share this circuit's
    // one DbContext concurrently and EF Core's ConcurrencyDetector throws "A second operation was
    // started on this context instance before a previous operation completed." Same guard pattern as
    // SaveAsync's _saving flag.
    private bool _updatingImpersonators;
    private bool _updatingSystemRole;

    private async Task OnApprovedImpersonatorsChangedAsync(ChangeEventArgs e)
    {
        if (_updatingImpersonators) return;
        _updatingImpersonators = true;
        try
        {
            var selectedIds = ((string[])e.Value!).Select(int.Parse).ToHashSet();
            foreach (var id in selectedIds.Except(_approvedImpersonatorIds).ToList())
            {
                await ToggleImpersonator(id, true);
            }
            foreach (var id in _approvedImpersonatorIds.Except(selectedIds).ToList())
            {
                await ToggleImpersonator(id, false);
            }
        }
        finally
        {
            _updatingImpersonators = false;
        }
    }

    private async Task ApplySystemRoleAsync()
    {
        if (_updatingSystemRole) return;
        _updatingSystemRole = true;
        try
        {
            await ApplySystemRoleCoreAsync();
        }
        finally
        {
            _updatingSystemRole = false;
        }
    }

    private async Task ApplySystemRoleCoreAsync()
    {
        // Snapshot the target before any awaited mutation -- ToggleUserRoleAsync calls LoadAuditAsync,
        // which recomputes both _selectedUserRoles and _systemRoleSelection from the database, so
        // reading either field again partway through this method would see stale/overwritten state.
        var targetRole = _systemRoleSelection;
        var previousRoles = _selectedUserRoles.ToList();

        foreach (var role in previousRoles.Where(r => r != targetRole))
        {
            await ToggleUserRoleAsync(role, false);
        }
        if (!string.IsNullOrEmpty(targetRole) && !_selectedUserRoles.Contains(targetRole))
        {
            await ToggleUserRoleAsync(targetRole, true);
        }
    }

    private async Task ToggleImpersonator(int impersonatorId, bool isChecked)
    {
        if (_editing is null || _editing.Id == 0) return;

        var impersonatorName = _members!.First(m => m.Id == impersonatorId).FullName;

        if (isChecked)
        {
            await ImpersonationWriter.GrantAsync(_editing.Id, impersonatorId);
            _approvedImpersonatorIds.Add(impersonatorId);
        }
        else
        {
            await ImpersonationWriter.RevokeAsync(_editing.Id, impersonatorId);
            _approvedImpersonatorIds.Remove(impersonatorId);
        }

        await AuditLog.LogAsync(
            await CurrentActorAsync(),
            AuditAction.RoleChange,
            nameof(Member),
            _editing.Id.ToString(),
            $"Impersonation approval for {impersonatorName} {(isChecked ? "granted" : "revoked")} on {_editing.FullName}");
        await LoadAuditAsync();
    }

    private async Task ToggleUserRoleAsync(string role, bool isChecked)
    {
        if (_editing is null || string.IsNullOrEmpty(_editing.ApplicationUserId)) return;
        var user = _users!.FirstOrDefault(u => u.Id == _editing.ApplicationUserId);
        if (user is null) return;

        if (isChecked)
        {
            await UserManager.AddToRoleAsync(user, role);
            _selectedUserRoles.Add(role);
        }
        else
        {
            await UserManager.RemoveFromRoleAsync(user, role);
            _selectedUserRoles.Remove(role);
        }

        // Granting or removing Administrator changes whether the deployment still needs its
        // bootstrap account, so re-apply that rule now rather than at the next restart.
        if (role == GovernanceRoles.Administrator)
        {
            await DefaultAdminProvisioner.RetireIfSupersededAsync(UserManager, Logger);
        }

        await AuditLog.LogAsync(
            await CurrentActorAsync(),
            AuditAction.RoleChange,
            nameof(ApplicationUser),
            user.Id,
            $"{role} {(isChecked ? "granted" : "revoked")} for {user.Email}");
        await LoadAuditAsync();
    }

    private async Task SaveAsync()
    {
        // Guards against a double/rapid click re-entering Save while the first click's insert/update
        // is still awaiting the DB: both calls would otherwise share this circuit's one DbContext and
        // either race (throwing "a second operation was started on this context before a previous
        // operation completed") or, for a new member, insert the same record twice.
        if (_editing is null || _saving) return;
        _saving = true;

        try
        {
            if (_editing.ReportingManagerId is not null && CreatesReportingCycle(_editing.ReportingManagerId, _editing.Id))
            {
                Toasts.ShowError("That reporting manager would create a cycle in the reporting chain.");
                return;
            }

            if (!string.IsNullOrEmpty(_editing.ApplicationUserId))
            {
                var alreadyLinkedTo = _members!.FirstOrDefault(m => m.ApplicationUserId == _editing.ApplicationUserId && m.Id != _editing.Id);
                if (alreadyLinkedTo is not null)
                {
                    Toasts.ShowError($"That login account is already linked to {alreadyLinkedTo.FullName}.");
                    return;
                }
            }

            var isNew = _editing.Id == 0;

            try
            {
                if (isNew)
                {
                    _editing.Id = await MemberWriter.InsertAsync(_editing);
                }
                else
                {
                    await MemberWriter.UpdateAsync(_editing);
                }
            }
            catch (SqlException ex) when (ex.Number is 50003 or 50004)
            {
                Toasts.ShowError("Could not save: a related record conflicts (duplicate login link or reporting cycle).");
                return;
            }

            await AuditLog.LogAsync(await CurrentActorAsync(), isNew ? AuditAction.Create : AuditAction.Update, nameof(Member), _editing.Id.ToString(), _editing.FullName);
            Toasts.ShowSuccess($"{_editing.FullName} saved.");

            // FRD §2.2: on save of a NEW user record, ask whether/how to notify them, rather than
            // silently emailing — NotifyChoiceAsync navigates away once the admin picks an option.
            if (isNew && !string.IsNullOrWhiteSpace(_editing.Email))
            {
                _showNotifyPrompt = true;
                return;
            }

            if (isNew)
            {
                Nav.NavigateTo($"/admin/user-management/member/{_editing.Id}");
                return;
            }

            var editingId = _editing.Id;
            _members = await Db.Members.Include(m => m.Company).Include(m => m.Department).OrderBy(m => m.FullName).ToListAsync();
            _editing = _members.First(m => m.Id == editingId);
            await LoadAuditAsync();
        }
        finally
        {
            _saving = false;
        }
    }

    private async Task NotifyChoiceAsync(NotifyChoice choice)
    {
        if (_editing is null) return;
        _showNotifyPrompt = false;

        switch (choice)
        {
            case NotifyChoice.Now:
                var notificationId = await NotificationWriter.InsertAsync(_editing.Id, _editing.Email!, MemberNotificationStatus.Pending);
                bool sent;
                string? sendError = null;
                try
                {
                    sent = await WelcomeEmailSender.SendWelcomeEmailAsync(_editing);
                }
                catch (Exception ex)
                {
                    Logger.LogError(ex, "Failed to send welcome email to {Email}", _editing.Email);
                    sent = false;
                    sendError = ex.Message;
                }
                await NotificationWriter.SetStatusAsync(notificationId, sent ? MemberNotificationStatus.Sent : MemberNotificationStatus.Failed, sent ? DateTime.UtcNow : null);
                await AuditLog.LogAsync(await CurrentActorAsync(), AuditAction.Notify, nameof(Member), _editing.Id.ToString(),
                    sent ? $"Notification email sent to {_editing.Email}"
                         : $"Notification email to {_editing.Email} could not be sent{(sendError is null ? "" : $": {sendError}")}");
                if (sent)
                {
                    Toasts.ShowSuccess("Notification sent.");
                }
                else
                {
                    Toasts.ShowError(sendError is null
                        ? "Could not send the notification — check SMTP configuration or send it later from Pending Notifications."
                        : $"Could not send the notification: {sendError}");
                }
                break;

            case NotifyChoice.Later:
                await NotificationWriter.InsertAsync(_editing.Id, _editing.Email!, MemberNotificationStatus.Pending);
                await AuditLog.LogAsync(await CurrentActorAsync(), AuditAction.Notify, nameof(Member), _editing.Id.ToString(),
                    $"Notification to {_editing.Email} queued for later");
                Toasts.ShowSuccess("Queued — send it later from Pending Notifications.");
                break;

            case NotifyChoice.No:
                await AuditLog.LogAsync(await CurrentActorAsync(), AuditAction.Notify, nameof(Member), _editing.Id.ToString(),
                    $"Notification to {_editing.Email} skipped");
                break;
        }

        // Only navigate when we're still on the "new user" route (Id == 0) and need to move to the
        // now-assigned real ID -- an existing user notified via the mail icon is already on the
        // right URL, so just refresh the audit history in place instead of a pointless reload.
        if (Id == 0 && _editing.Id != 0)
        {
            Nav.NavigateTo($"/admin/user-management/member/{_editing.Id}");
        }
        else
        {
            await LoadAuditAsync();
        }
    }

    private bool CreatesReportingCycle(int? managerId, int memberId)
    {
        var visited = new HashSet<int>();
        var currentId = managerId;
        while (currentId is not null)
        {
            if (currentId == memberId) return true;
            if (!visited.Add(currentId.Value)) break;
            currentId = _members!.FirstOrDefault(m => m.Id == currentId.Value)?.ReportingManagerId;
        }
        return false;
    }

    private async Task DeactivateAsync(string justification)
    {
        if (_editing is null || _editing.Id == 0) return;

        _editing.Active = !_editing.Active;
        await MemberWriter.SetActiveAsync(_editing.Id, _editing.Active);

        // Deactivating must actually block login, not just flip a cosmetic flag.
        if (!string.IsNullOrEmpty(_editing.ApplicationUserId))
        {
            var linkedUser = await UserManager.FindByIdAsync(_editing.ApplicationUserId);
            if (linkedUser is not null)
            {
                await UserManager.SetLockoutEnabledAsync(linkedUser, true);
                await UserManager.SetLockoutEndDateAsync(linkedUser, _editing.Active ? null : DateTimeOffset.MaxValue);
            }
        }

        await AuditLog.LogAsync(
            await CurrentActorAsync(),
            _editing.Active ? AuditAction.Reactivate : AuditAction.Deactivate,
            nameof(Member), _editing.Id.ToString(), _editing.FullName,
            justification: justification);

        Toasts.ShowSuccess($"{_editing.FullName} {(_editing.Active ? "reactivated" : "deactivated — login is now blocked")}.");
        _showDeactivateJustify = false;
        await LoadAuditAsync();
    }

    private async Task LogInAsAsync()
    {
        if (_editing is null || string.IsNullOrEmpty(_editing.ApplicationUserId)) return;
        if (_editing.ApplicationUserId == _currentUserId) return;

        var targetUser = _users!.FirstOrDefault(u => u.Id == _editing.ApplicationUserId);
        if (targetUser is null) return;

        var state = await AuthState.GetAuthenticationStateAsync();
        var adminId = state.User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "unknown";
        var adminName = state.User.Identity?.Name ?? "unknown";

        await AuditLog.LogAsync(adminName, AuditAction.ImpersonationStart, nameof(ApplicationUser), targetUser.Id,
            $"Administrator support session started: acting as {targetUser.Email}");

        await SignInManager.SignInWithClaimsAsync(targetUser, isPersistent: false,
        [
            new Claim(AdminImpersonationClaims.OriginalAdminId, adminId),
            new Claim(AdminImpersonationClaims.OriginalAdminName, adminName),
        ]);

        Nav.NavigateTo("/", forceLoad: true);
    }

    private async Task<string> CurrentActorAsync()
    {
        var state = await AuthState.GetAuthenticationStateAsync();
        return state.User.Identity?.Name ?? "unknown";
    }
}
