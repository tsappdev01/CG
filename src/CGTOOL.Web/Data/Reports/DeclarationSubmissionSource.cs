using CGTOOL.Web.Data.Governance;
using Microsoft.EntityFrameworkCore;

namespace CGTOOL.Web.Data.Reports;

/// <summary>One person who was notified of a declaration for a period, and what they filed.
///
/// The row exists whether or not they declared -- that is the point of a submission return, which
/// has to show who was asked as well as who answered. Declaration is null until they submit.</summary>
public sealed class DeclarationSubmissionRow<TDeclaration> where TDeclaration : class
{
    public required int MemberId { get; init; }
    public required string MemberName { get; init; }
    public int? CompanyId { get; init; }
    public string? CompanyName { get; init; }
    public string? DepartmentName { get; init; }
    public required int Year { get; init; }
    public required int Quarter { get; init; }
    public required bool Submitted { get; init; }
    public TDeclaration? Declaration { get; init; }
}

/// <summary>Loads the recipient-and-submission rows the four submission reports are built from.
///
/// Shared because the Insider and RP &amp; COI returns ask the same question of different tables,
/// and the two reports and their two detail reports had otherwise copied the same join four times
/// -- at which point the "a draft is not a submission" rule lives in four places and eventually
/// stops being the same rule in all of them.</summary>
public static class DeclarationSubmissionSource
{
    public static async Task<List<DeclarationSubmissionRow<InsiderDeclaration>>> InsiderAsync(ApplicationDbContext db)
    {
        var declarations = await db.InsiderDeclarations
            .AsNoTracking()
            .Include(d => d.Relatives)
            .Include(d => d.NinHolders)
            .ToListAsync();

        return await BuildAsync(db, DeclarationCycleType.InsiderTrading, declarations,
            d => (d.MemberId, d.DeclarationCycleRunId), d => d.IsDraft);
    }

    public static async Task<List<DeclarationSubmissionRow<RelatedPartyCoiDeclaration>>> RelatedPartyCoiAsync(ApplicationDbContext db)
    {
        var declarations = await db.RelatedPartyCoiDeclarations
            .AsNoTracking()
            .Include(d => d.Relatives)
            .Include(d => d.Companies)
            .Include(d => d.Conflicts)
            .ToListAsync();

        return await BuildAsync(db, DeclarationCycleType.ConflictOfInterest, declarations,
            d => (d.MemberId, d.DeclarationCycleRunId), d => d.IsDraft);
    }

    private static async Task<List<DeclarationSubmissionRow<T>>> BuildAsync<T>(
        ApplicationDbContext db,
        DeclarationCycleType type,
        List<T> declarations,
        Func<T, (int MemberId, int RunId)> key,
        Func<T, bool> isDraft) where T : class
    {
        var recipients = await db.DeclarationCycleRunRecipients
            .AsNoTracking()
            .Include(r => r.DeclarationCycleRun)
            .Where(r => r.DeclarationCycleRun!.Type == type)
            .ToListAsync();

        var members = await db.Members
            .AsNoTracking()
            .Include(m => m.Company)
            .Include(m => m.Department)
            .ToDictionaryAsync(m => m.Id);

        var lookup = declarations.ToDictionary(key);

        return [.. recipients
            .Select(r =>
            {
                var run = r.DeclarationCycleRun!;
                members.TryGetValue(r.MemberId, out var member);
                lookup.TryGetValue((r.MemberId, run.Id), out var declaration);

                return new DeclarationSubmissionRow<T>
                {
                    MemberId = r.MemberId,
                    MemberName = r.MemberName,
                    CompanyId = member?.CompanyId,
                    // The name the recipient list recorded is the fallback: a member removed since
                    // the notification went out still belongs on that period's return.
                    CompanyName = member?.Company?.Name ?? r.CompanyName,
                    DepartmentName = member?.Department?.Name,
                    // The period is chosen when the cycle is set up, not derived from the send date.
                    Year = run.PeriodYear,
                    Quarter = run.PeriodQuarter,
                    // A saved draft is not a completed declaration: the person is still outstanding
                    // until they submit.
                    Submitted = declaration is not null && !isDraft(declaration),
                    Declaration = declaration,
                };
            })
            .OrderBy(r => r.CompanyName).ThenBy(r => r.MemberName)];
    }
}
