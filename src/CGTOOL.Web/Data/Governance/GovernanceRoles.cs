using System.Security.Claims;

namespace CGTOOL.Web.Data.Governance;

public static class GovernanceRoles
{
    public const string Administrator = "Administrator";

    /// <summary>Default low-privilege role: can only submit their own Insider Declaration and Related Party &amp; COI declaration.</summary>
    public const string NormalUser = "Normal User";

    public static readonly string[] All = [Administrator, NormalUser];

    /// <summary>True when the user's only role is Normal User (no Administrator, and no other custom role granting broader access).</summary>
    public static bool IsNormalStaffOnly(ClaimsPrincipal user) =>
        user.IsInRole(NormalUser)
        && !user.IsInRole(Administrator)
        && user.Claims.Count(c => c.Type == ClaimTypes.Role) <= 1;
}
