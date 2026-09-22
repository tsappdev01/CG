using Microsoft.AspNetCore.Identity;
using CGTOOL.Web.Data;

namespace CGTOOL.Web.Data.Governance;

/// <summary>
/// Development-only fallback: validates against the local ASP.NET Core Identity password hash so
/// the login flow can be tested where the real corporate directory isn't reachable. Production
/// deployments (on the corporate network) use <see cref="LdapActiveDirectoryAuthenticator"/> instead.
/// </summary>
public class LocalIdentityAuthenticator(UserManager<ApplicationUser> userManager) : IActiveDirectoryAuthenticator
{
    public async Task<AdAuthResult> AuthenticateAsync(string username, string password, CancellationToken ct = default)
    {
        var user = await userManager.FindByNameAsync(username) ?? await userManager.FindByEmailAsync(username);
        if (user is null)
        {
            return new AdAuthResult(false, null, null, "Invalid corporate credentials.");
        }

        var valid = await userManager.CheckPasswordAsync(user, password);
        return valid
            ? new AdAuthResult(true, user.UserName, user.Email, null)
            : new AdAuthResult(false, null, null, "Invalid corporate credentials.");
    }
}
