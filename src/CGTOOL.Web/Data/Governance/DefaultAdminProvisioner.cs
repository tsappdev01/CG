using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using CGTOOL.Web.Data;

namespace CGTOOL.Web.Data.Governance;

/// <summary>
/// Creates and retires the bootstrap administrator -- the account that makes a brand new deployment
/// signable-in at all, before any real user exists.
///
/// The rule is a single invariant, applied at startup and again whenever an administrator is
/// granted: <b>the default admin is enabled only while the deployment has no other administrator.</b>
/// Expressing it that way (rather than "disable it once, the first time") means the same code both
/// retires the account when the first real administrator appears and restores it if every real
/// administrator is later removed, which would otherwise leave nobody able to sign in.
///
/// Credentials come from configuration (<c>DefaultAdmin:UserName</c> / <c>DefaultAdmin:Password</c>)
/// so a deployment can set its own without a rebuild; the fallbacks exist purely so that a first run
/// works out of the box, and their use is logged as a warning.
/// </summary>
public static class DefaultAdminProvisioner
{
    public const string SectionName = "DefaultAdmin";

    private const string FallbackUserName = "admin@cgtool.local";
    private const string FallbackPassword = "Admin@12345";

    /// <summary>Applies the invariant at startup, creating the account when it is needed.</summary>
    public static async Task EnsureAsync(
        UserManager<ApplicationUser> userManager,
        IConfiguration configuration,
        ILogger logger,
        CancellationToken ct = default)
    {
        var section = configuration.GetSection(SectionName);
        if (!section.GetValue("Enabled", true))
        {
            logger.LogInformation("DefaultAdmin:Enabled is false -- no bootstrap administrator will be provisioned.");
            return;
        }

        var userName = section["UserName"];
        var password = section["Password"];

        if (string.IsNullOrWhiteSpace(userName)) userName = FallbackUserName;
        if (string.IsNullOrWhiteSpace(password))
        {
            password = FallbackPassword;
            logger.LogWarning(
                "No DefaultAdmin:Password is configured, so the built-in fallback is in use. Set " +
                "DefaultAdmin:Password (user-secrets, an environment variable, or Key Vault) before " +
                "this deployment is reachable by anyone else.");
        }

        var existing = await FindAsync(userManager, ct);

        if (await HasRealAdministratorAsync(userManager, ct))
        {
            // A real administrator is already in place: the bootstrap account is not needed. Retire
            // it if it exists, and do not create one if it never did.
            if (existing is not null) await SetActiveAsync(userManager, existing, active: false, logger);
            return;
        }

        if (existing is null)
        {
            existing = new ApplicationUser
            {
                UserName = userName,
                Email = userName.Contains('@') ? userName : $"{userName}@cgtool.local",
                EmailConfirmed = true,
                IsDefaultAdmin = true,
            };

            var created = await userManager.CreateAsync(existing, password);
            if (!created.Succeeded)
            {
                logger.LogError(
                    "Could not create the bootstrap administrator '{UserName}': {Errors}",
                    userName,
                    string.Join("; ", created.Errors.Select(e => e.Description)));
                return;
            }

            await userManager.AddToRoleAsync(existing, GovernanceRoles.Administrator);
            logger.LogWarning(
                "Created the bootstrap administrator '{UserName}'. Sign in with it once, create a real " +
                "administrator from User Management, and this account disables itself.",
                userName);
        }

        // The account exists but the deployment has no other administrator -- make sure it can
        // actually be signed into (it may have been retired by an administrator who has since gone).
        if (!await userManager.IsInRoleAsync(existing, GovernanceRoles.Administrator))
        {
            await userManager.AddToRoleAsync(existing, GovernanceRoles.Administrator);
        }

        // Configuration is the source of truth for this account's password, not just at the moment
        // it is created. Without this, editing DefaultAdmin:Password on a deployment that already
        // has the account does nothing at all, and the only symptom is that the documented password
        // does not work -- indistinguishable from a broken login. It is a bootstrap account that
        // retires itself the moment a real administrator exists, so config winning is what anyone
        // editing that setting expects.
        if (!await userManager.CheckPasswordAsync(existing, password))
        {
            var token = await userManager.GeneratePasswordResetTokenAsync(existing);
            var reset = await userManager.ResetPasswordAsync(existing, token, password);
            if (reset.Succeeded)
            {
                logger.LogWarning(
                    "Reset the bootstrap administrator '{UserName}' to the configured " +
                    "DefaultAdmin:Password.", userName);
            }
            else
            {
                logger.LogError(
                    "Could not apply the configured DefaultAdmin:Password to '{UserName}': {Errors}. " +
                    "The account keeps its previous password.",
                    userName,
                    string.Join("; ", reset.Errors.Select(e => e.Description)));
            }
        }

        await SetActiveAsync(userManager, existing, active: true, logger);
    }

    /// <summary>
    /// Re-applies the invariant after a role change. Called wherever the Administrator role is
    /// granted, so the bootstrap account retires itself the moment a real administrator exists
    /// rather than waiting for the next restart.
    /// </summary>
    public static async Task RetireIfSupersededAsync(
        UserManager<ApplicationUser> userManager,
        ILogger logger,
        CancellationToken ct = default)
    {
        var existing = await FindAsync(userManager, ct);
        if (existing is null) return;
        if (!await HasRealAdministratorAsync(userManager, ct)) return;

        await SetActiveAsync(userManager, existing, active: false, logger);
    }

    /// <summary>True when the signed-in account is the bootstrap administrator.</summary>
    public static bool IsDefaultAdmin(ApplicationUser? user) => user?.IsDefaultAdmin == true;

    public static Task<ApplicationUser?> FindAsync(UserManager<ApplicationUser> userManager, CancellationToken ct = default) =>
        userManager.Users.FirstOrDefaultAsync(u => u.IsDefaultAdmin, ct);

    /// <summary>An administrator that is not the bootstrap account and is not locked out.</summary>
    private static async Task<bool> HasRealAdministratorAsync(UserManager<ApplicationUser> userManager, CancellationToken ct)
    {
        var admins = await userManager.GetUsersInRoleAsync(GovernanceRoles.Administrator);
        foreach (var admin in admins)
        {
            if (admin.IsDefaultAdmin) continue;
            // A deactivated administrator is locked out (see User Management), and an account nobody
            // can sign into does not count as cover -- otherwise deactivating the last real admin
            // would lock the deployment out completely.
            if (await userManager.IsLockedOutAsync(admin)) continue;
            return true;
        }

        return false;
    }

    /// <summary>Deactivation is the same lockout User Management applies to a deactivated member, so
    /// the sign-in paths already refuse it without any extra checks.</summary>
    private static async Task SetActiveAsync(
        UserManager<ApplicationUser> userManager, ApplicationUser user, bool active, ILogger logger)
    {
        var lockedOut = await userManager.IsLockedOutAsync(user);
        if (lockedOut != active) return;   // already in the wanted state

        await userManager.SetLockoutEnabledAsync(user, true);
        await userManager.SetLockoutEndDateAsync(user, active ? null : DateTimeOffset.MaxValue);

        logger.LogWarning(
            active
                ? "The bootstrap administrator '{UserName}' has been re-enabled because this deployment has no other administrator."
                : "The bootstrap administrator '{UserName}' has been disabled because a real administrator now exists.",
            user.UserName);
    }
}
