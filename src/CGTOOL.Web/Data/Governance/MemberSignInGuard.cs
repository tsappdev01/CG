using Microsoft.EntityFrameworkCore;
using CGTOOL.Web.Data;

namespace CGTOOL.Web.Data.Governance;

/// <summary>Whether the person signing in is still allowed in, judged on the member record an
/// administrator actually looks at.
///
/// Deactivating a member already locks their Identity login, but that mirror is only written when
/// the member has a linked ApplicationUser, and only by the two admin screens that do it. A member
/// deactivated before their login was ever linked, one created by the directory sync or the user
/// list upload, or one changed in the database directly, has an Active flag saying no and a lockout
/// saying nothing. This asks the flag.</summary>
public interface IMemberSignInGuard
{
    /// <summary>The deactivated member blocking this sign-in, or null to let it through. Null also
    /// when nobody matches: an account with no member record -- the bootstrap administrator, for
    /// one -- is not something this can judge, and refusing it would lock the application.</summary>
    Task<Member?> BlockingMemberAsync(string? userName, string? email, string? applicationUserId);
}

public class MemberSignInGuard(IDbContextFactory<ApplicationDbContext> dbFactory) : IMemberSignInGuard
{
    public async Task<Member?> BlockingMemberAsync(string? userName, string? email, string? applicationUserId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();

        // Matched the way the sign-in pages themselves match an account: by the link where there is
        // one, and otherwise by the address, since someone can sign in with either.
        var member = await db.Members
            .AsNoTracking()
            .Include(m => m.Company)
            .Include(m => m.Department)
            .Where(m => (applicationUserId != null && m.ApplicationUserId == applicationUserId)
                        || (email != null && m.Email == email)
                        || (userName != null && m.Email == userName))
            .OrderByDescending(m => m.ApplicationUserId == applicationUserId)
            .FirstOrDefaultAsync();

        return member is { Active: false } ? member : null;
    }
}

/// <summary>What the audit trail records when a sign-in is refused, written where the refusal
/// happens so every entry says the same things about it.</summary>
public static class SignInDenial
{
    public static string Details(Member member, string method, string attemptedIdentifier, string? ipAddress, string? userAgent)
    {
        var parts = new List<string>
        {
            $"Sign-in denied: {member.FullName} is deactivated.",
            $"Attempted as: {attemptedIdentifier}",
            $"Method: {method}",
            $"Entity: {member.Company?.Name ?? "not set"}",
            $"Department: {member.Department?.Name ?? "not set"}",
            $"Member email on file: {member.Email ?? "not set"}",
        };

        if (!string.IsNullOrWhiteSpace(ipAddress)) parts.Add($"IP: {ipAddress}");
        if (!string.IsNullOrWhiteSpace(userAgent)) parts.Add($"User agent: {userAgent}");

        return string.Join(" | ", parts);
    }
}
