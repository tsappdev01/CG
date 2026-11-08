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
    /// <summary>One "reports to" link: a member and the manager set on their record.</summary>
    public readonly record struct ReportingLink(int MemberId, int ManagerId);

    /// <summary>A member and everyone beneath them in the reporting line.
    ///
    /// Breadth-first from the member rather than by recursion, so depth costs memory it can see
    /// rather than stack it cannot. Nothing stops an administrator making two people each other's
    /// manager in User Management, by accident or through a swap done in the wrong order, and a
    /// walk that trusted the data to be a tree would hang the report rather than show it -- so
    /// the visited set is what both collects the answer and ends the walk.</summary>
    public static HashSet<int> TreeFrom(int rootMemberId, IEnumerable<ReportingLink> links)
    {
        ArgumentNullException.ThrowIfNull(links);

        var reportsTo = links
            .GroupBy(link => link.ManagerId)
            .ToDictionary(group => group.Key, group => group.Select(link => link.MemberId).ToArray());

        var seen = new HashSet<int> { rootMemberId };
        var pending = new Queue<int>([rootMemberId]);

        while (pending.Count > 0)
        {
            if (!reportsTo.TryGetValue(pending.Dequeue(), out var directReports)) continue;

            foreach (var id in directReports)
            {
                // Add is false for anyone already counted, which is what stops a cycle: a member
                // reachable twice is enqueued once.
                if (seen.Add(id)) pending.Enqueue(id);
            }
        }

        return seen;
    }

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

        // Themselves, and everyone below them in the reporting line however many levels down --
        // a department head sees their managers' people too, not only the managers.
        //
        // Every reporting link in one query and the walk done here, rather than a query per level
        // or a recursive CTE: the table is a few thousand rows, the shape is a tree nobody builds
        // deliberately deep, and one round trip beats an unbounded number of them.
        var edges = await db.Members.AsNoTracking()
            .Where(m => m.ReportingManagerId != null)
            .Select(m => new ReportingLink(m.Id, m.ReportingManagerId!.Value))
            .ToListAsync(ct);

        return TreeFrom(memberId, edges);
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
