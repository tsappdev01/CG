using Microsoft.AspNetCore.Components.Forms;
using Microsoft.EntityFrameworkCore;
using CGTOOL.Web.Data;
using CGTOOL.Web.Data.Governance;

namespace CGTOOL.Web.Components.Pages.Admin;

/// <summary>
/// The corporate policy document and its version history. This used to be the first card on
/// Settings; it is its own screen, and its own nav entry, because it is a document people maintain
/// rather than a setting they configure.
/// </summary>
public partial class PolicyDocuments
{
    private const long MaxPolicyDocumentBytes = 20 * 1024 * 1024;
    private const string PolicyDocumentRelativePath = PolicyDocumentLink.RelativePath;
    private const string PolicyBackupRelativeDir = PolicyDocumentLink.BackupRelativeDir;

    private string? _currentUrl;

    private List<PolicyDocumentVersion>? _versions;
    private bool _currentDocumentExists;
    private bool _uploading;

    protected override async Task OnInitializedAsync() => await LoadAsync();

    private async Task LoadAsync()
    {
        _currentDocumentExists = PolicyDocumentLink.Exists(Env);

        // Re-read on every load so the link changes the moment the file does.
        _currentUrl = PolicyDocumentLink.CurrentUrl(Env);

        // Its own short-lived context rather than the circuit-scoped ApplicationDbContext: sharing
        // that one lets this race, or outlive, whatever else in the circuit is using it.
        await using var db = await DbFactory.CreateDbContextAsync();
        _versions = await db.PolicyDocumentVersions
            .OrderByDescending(v => v.UploadedAtUtc)
            .ToListAsync();
    }

    private async Task OnFileSelectedAsync(InputFileChangeEventArgs e)
    {
        var file = e.File;

        if (file.ContentType != "application/pdf" && !file.Name.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
        {
            Toasts.ShowError("Only PDF files are supported.");
            return;
        }
        if (file.Size > MaxPolicyDocumentBytes)
        {
            Toasts.ShowError("File must be 20 MB or smaller.");
            return;
        }

        _uploading = true;
        try
        {
            var assetsDir = Path.Combine(Env.WebRootPath, "assets");
            Directory.CreateDirectory(assetsDir);

            var policyPath = Path.Combine(Env.WebRootPath, PolicyDocumentRelativePath);
            var hadExisting = File.Exists(policyPath);
            string? backupRelativePath = null;

            if (hadExisting)
            {
                var backupDir = Path.Combine(Env.WebRootPath, PolicyBackupRelativeDir);
                Directory.CreateDirectory(backupDir);

                var backupFileName = $"policy-{DateTime.UtcNow:yyyyMMddHHmmss}.pdf";
                File.Copy(policyPath, Path.Combine(backupDir, backupFileName), overwrite: true);
                backupRelativePath = $"/{PolicyBackupRelativeDir}/{backupFileName}";
            }

            await using (var stream = file.OpenReadStream(MaxPolicyDocumentBytes))
            await using (var target = File.Create(policyPath))
            {
                await stream.CopyToAsync(target);
            }

            var actorName = await CurrentActorAsync();
            var actorFullName = await CurrentActorFullNameAsync(actorName);

            if (backupRelativePath is not null)
            {
                await VersionWriter.InsertAsync(new PolicyDocumentVersion
                {
                    FilePath = backupRelativePath,
                    OriginalFileName = file.Name,
                    UploadedByName = actorFullName,
                });
            }

            await AuditLog.LogAsync(actorName, AuditAction.Update, nameof(PolicyDocumentVersion), null,
                hadExisting
                    ? $"Policy document replaced with '{file.Name}' (previous version backed up to {backupRelativePath})"
                    : $"Policy document uploaded for the first time ('{file.Name}')");

            Toasts.ShowSuccess("Policy document updated.");
            await LoadAsync();
        }
        finally
        {
            _uploading = false;
        }
    }

    private async Task<string> CurrentActorAsync()
    {
        var state = await AuthState.GetAuthenticationStateAsync();
        return state.User.Identity?.Name ?? "unknown";
    }

    // "Replaced by" in the version history is meant for a compliance audience -- the login name
    // (often an email or AD account) isn't as readable as the person's actual name, so this resolves
    // it via the Member record linked to the current login, falling back to the login name for
    // logins with no linked Member (e.g. a bootstrap admin account).
    private async Task<string> CurrentActorFullNameAsync(string fallbackActorName)
    {
        var state = await AuthState.GetAuthenticationStateAsync();
        var userId = state.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (userId is null) return fallbackActorName;

        await using var db = await DbFactory.CreateDbContextAsync();
        var fullName = await db.Members
            .Where(m => m.ApplicationUserId == userId)
            .Select(m => m.FullName)
            .FirstOrDefaultAsync();

        return string.IsNullOrWhiteSpace(fullName) ? fallbackActorName : fullName;
    }
}
