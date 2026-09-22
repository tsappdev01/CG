namespace CGTOOL.Web.Data.Governance;

/// <summary>Claim types used to mark a session as an Administrator's "log in as" support session (UM-03).</summary>
public static class AdminImpersonationClaims
{
    public const string OriginalAdminId = "cgtool_original_admin_id";
    public const string OriginalAdminName = "cgtool_original_admin_name";
}
