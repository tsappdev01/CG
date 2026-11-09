using System.Security.Claims;
using CGTOOL.Web.Data;
using CGTOOL.Web.Data.Governance;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace CGTOOL.Checks;

/// <summary>
/// Who may see whose rows. The reports are shared between people who may see the whole company
/// and people who may see only their own line, so this is the part that leaks other people's
/// declarations if it is wrong.
/// </summary>
public static class ReportAccessChecks
{
    private static ClaimsPrincipal User(params string[] roles) =>
        new(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, "u1"), .. roles.Select(r => new Claim(ClaimTypes.Role, r))],
            "check", ClaimTypes.Name, ClaimTypes.Role));

    public static void Run()
    {
        Check.Section("Roles");
        Check.That("Administrator is an administrator", GovernanceRoles.IsAnyAdministrator(User(GovernanceRoles.Administrator)));
        Check.That("Application Administrator is an administrator", GovernanceRoles.IsAnyAdministrator(User(GovernanceRoles.ApplicationAdministrator)));
        Check.That("Normal User is not", !GovernanceRoles.IsAnyAdministrator(User(GovernanceRoles.NormalUser)));
        Check.That("Normal User alone counts as normal staff", GovernanceRoles.IsNormalStaffOnly(User(GovernanceRoles.NormalUser)));
        Check.That("Normal User who is also an Application Administrator does not",
                   !GovernanceRoles.IsNormalStaffOnly(User(GovernanceRoles.NormalUser, GovernanceRoles.ApplicationAdministrator)));
        Check.That("every role is seeded at startup", GovernanceRoles.All.Length == 3);
        Check.That("the administrators-only list excludes Normal User",
                   !GovernanceRoles.AnyAdministrator.Contains(GovernanceRoles.NormalUser, StringComparison.Ordinal));
        Check.That("the shared-report list includes Normal User",
                   GovernanceRoles.SharedReportViewers.Contains(GovernanceRoles.NormalUser, StringComparison.Ordinal));

        Check.Section("Report audience");
        // The factory throws, so reaching the database at all fails the check: an administrator
        // must resolve to "no limit" without one.
        var audience = new ReportAudience(new ExplodingFactory());
        foreach (var role in new[] { GovernanceRoles.Administrator, GovernanceRoles.ApplicationAdministrator })
        {
            var ids = audience.VisibleMemberIdsAsync(User(role)).GetAwaiter().GetResult();
            Check.That($"{role} sees everyone, without a database round trip", ids is null);
        }

        var rows = new[] { (Id: 1, Who: "me"), (Id: 2, Who: "my report"), (Id: 3, Who: "a stranger") };
        Check.That("no limit keeps every row", rows.LimitTo(null, r => r.Id).Count == 3);
        Check.That("a limit keeps only its own", rows.LimitTo([1, 2], r => r.Id) is [{ Id: 1 }, { Id: 2 }]);
        Check.That("an empty audience keeps nothing", rows.LimitTo([], r => r.Id).Count == 0);

        Check.Section("Reporting line");
        // Alice -> Bob -> Carol -> Dan, Alice -> Eve, and Frank reporting elsewhere.
        ReportAudience.ReportingLink[] org =
        [
            new(MemberId: 2, ManagerId: 1), new(MemberId: 3, ManagerId: 2),
            new(MemberId: 4, ManagerId: 3), new(MemberId: 5, ManagerId: 1),
            new(MemberId: 6, ManagerId: 99),
        ];

        Check.That("a manager sees their whole line, however deep",
                   ReportAudience.TreeFrom(1, org).SetEquals([1, 2, 3, 4, 5]));
        Check.That("and nobody outside it", !ReportAudience.TreeFrom(1, org).Contains(6));
        Check.That("someone mid-line sees down, not up or sideways",
                   ReportAudience.TreeFrom(2, org).SetEquals([2, 3, 4]));
        Check.That("someone with nobody under them sees only themselves",
                   ReportAudience.TreeFrom(4, org).SetEquals([4]));
        Check.That("a member who manages nobody is still included",
                   ReportAudience.TreeFrom(77, org).SetEquals([77]));

        // Nothing stops two people being made each other's manager in User Management.
        ReportAudience.ReportingLink[] cycle = [new(10, 11), new(11, 10), new(12, 11)];
        var walked = Task.Run(() => ReportAudience.TreeFrom(10, cycle));
        Check.That("a cycle in the reporting line terminates instead of hanging",
                   walked.Wait(TimeSpan.FromSeconds(5)));
        Check.That("and still returns the right people", walked.Result.SetEquals([10, 11, 12]));
    }

    private sealed class ExplodingFactory : IDbContextFactory<ApplicationDbContext>
    {
        public ApplicationDbContext CreateDbContext() =>
            throw new InvalidOperationException("An administrator must not need the database to be told they may see everyone.");
    }
    /// <summary>A reporting manager sees whether their staff declared; the sheet of what they
    /// declared is the compliance function's. The submission reports withhold the link, and the
    /// detail pages refuse the address, so neither stands alone.</summary>
    public static void DetailSheetsAreAdministrators()
    {
        Check.Section("Who can open a declaration's detail sheet");

        var pages = new (string Name, Type? Page)[]
        {
            ("Insider Detail", Type.GetType("CGTOOL.Web.Components.Pages.Admin.InsiderDetailReport, CGTOOL.Web")),
            ("RP & COI Detail", Type.GetType("CGTOOL.Web.Components.Pages.Admin.RpCoiDetailReport, CGTOOL.Web")),
        };

        foreach (var (name, page) in pages)
        {
            Check.That($"{name} is still a page", page is not null);
            if (page is null) continue;

            var roles = page.GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true)
                .Cast<AuthorizeAttribute>()
                .Select(a => a.Roles ?? string.Empty)
                .ToList();

            Check.That($"{name} is restricted to administrators",
                       roles.Any(r => r == GovernanceRoles.AnyAdministrator));
            Check.That($"{name} is not open to every report viewer",
                       !roles.Any(r => r == GovernanceRoles.SharedReportViewers));
        }

        Check.Section("Who the roles cover");

        // The guard the pages use. A manager is an ordinary user with people reporting to them --
        // no role of their own -- so "administrator" is the whole of the distinction.
        Check.That("the administrator roles are the two administrator roles",
                   GovernanceRoles.AnyAdministrator
                       == $"{GovernanceRoles.Administrator},{GovernanceRoles.ApplicationAdministrator}");
        Check.That("report viewers still include ordinary users, so managers keep the summaries",
                   GovernanceRoles.SharedReportViewers.Contains(GovernanceRoles.NormalUser, StringComparison.Ordinal));
    }

}
