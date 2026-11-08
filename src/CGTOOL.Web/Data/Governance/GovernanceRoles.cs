using System.Security.Claims;

namespace CGTOOL.Web.Data.Governance;

public static class GovernanceRoles
{
    public const string Administrator = "Administrator";

    /// <summary>Runs the governance process without being able to change how the system is set
    /// up: the policy document, the reports and the investor relations screens, but none of the
    /// Admin Panel configuration and no declaration setup.</summary>
    public const string ApplicationAdministrator = "Application Administrator";

    /// <summary>Default low-privilege role: can only submit their own Insider Declaration and Related Party &amp; COI declaration.</summary>
    public const string NormalUser = "Normal User";

    public static readonly string[] All = [Administrator, ApplicationAdministrator, NormalUser];

    /// <summary>Both administrator roles, for a screen they share. A comma-separated list because
    /// that is the form AuthorizeView and [Authorize] both take.</summary>
    public const string AnyAdministrator = Administrator + "," + ApplicationAdministrator;

    /// <summary>Everyone who may open one of the reports a Normal User is allowed. What they see
    /// inside it still differs -- see IReportAudience.</summary>
    public const string SharedReportViewers = Administrator + "," + ApplicationAdministrator + "," + NormalUser;

    /// <summary>True for a user in either administrator role.</summary>
    public static bool IsAnyAdministrator(ClaimsPrincipal user) =>
        user.IsInRole(Administrator) || user.IsInRole(ApplicationAdministrator);

    /// <summary>True when the user's only role is Normal User (no Administrator, and no other custom role granting broader access).</summary>
    public static bool IsNormalStaffOnly(ClaimsPrincipal user) =>
        user.IsInRole(NormalUser)
        && !IsAnyAdministrator(user)
        && user.Claims.Count(c => c.Type == ClaimTypes.Role) <= 1;
}
