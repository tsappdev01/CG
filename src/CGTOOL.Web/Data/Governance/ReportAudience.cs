using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using CGTOOL.Web.Data;

namespace CGTOOL.Web.Data.Governance;

/// <summary>Whose rows a signed-in user's reports may show.</summary>
public interface IReportAudience
{
    /// <summary>The member ids this user may see, or null when they may see everyone.
    ///
    /// Null rather than "all the ids" on purpose: a report that may show everyone should not be
    /// filtered against a list that has to be kept in step with the membership, and a caller that
    /// forgets to handle null gets a compile error rather than a quietly empty report.</summary>
    Task<HashSet<int>?> VisibleMemberIdsAsync(ClaimsPrincipal user, CancellationToken ct = default);
}

/// <summary>
/// Reports are shared between people who may see the whole company and people who may see only
/// their own corner of it, so who may see what is decided here rather than in each report. Ten
/// reports each deciding for themselves is ten chances to get it wrong, and the one that gets it
/// wrong leaks other people's declarations.
/// </summary>
public class ReportAudience(IDbContextFactory<ApplicationDbContext> dbFactory) : IReportAudience
{
    public async Task<HashSet<int>?> VisibleMemberIdsAsync(ClaimsPrincipal user, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(user);

        // Both administrator roles see the whole company. The Application Administrator's job is
        // to run the governance process across it; a view limited to their own reports would not
        // let them do it.
        if (GovernanceRoles.IsAnyAdministrator(user)) return null;

        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var userId = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (userId is null) return [];

        var me = await db.Members.AsNoTracking()
            .Where(m => m.ApplicationUserId == userId)
            .Select(m => (int?)m.Id)
            .FirstOrDefaultAsync(ct);

        // Signed in but not a member of the governance register: no rows rather than all rows.
        if (me is not { } memberId) return [];

        // Themselves, and the people whose Reporting Manager is set to them in User Management.
        // Direct reports only, which is what the requirement says in as many words -- "who are
        // reporting to the employee as defined in User management". Widening this to the whole
        // tree beneath them is a change to the Where below and nothing else, but it is a decision
        // about who may read whose declarations, so it is not one to make in passing.
        var reports = await db.Members.AsNoTracking()
            .Where(m => m.ReportingManagerId == memberId)
            .Select(m => m.Id)
            .ToListAsync(ct);

        return [memberId, .. reports];
    }
}

public static class ReportAudienceExtensions
{
    /// <summary>Drops the rows this user may not see.
    ///
    /// Applied to the finished rows rather than woven into each report's query: the four shared
    /// reports are built three different ways, and a filter that looks the same in all of them is
    /// one that can be checked by reading. A null audience means no limit, so an administrator
    /// pays nothing for it.</summary>
    public static List<T> LimitTo<T>(this IEnumerable<T> rows, HashSet<int>? visibleMemberIds, Func<T, int> memberIdOf)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(memberIdOf);

        return visibleMemberIds is null
            ? [.. rows]
            : [.. rows.Where(row => visibleMemberIds.Contains(memberIdOf(row)))];
    }
}
