using Microsoft.EntityFrameworkCore;

namespace CGTOOL.Web.Data.Governance;

public enum PreCheckState { Pass, Warn, Fail }

/// <summary>One thing the system checked, said in words, with what follows from it. A rule that
/// silently routes or silently blocks is a rule nobody trusts, so every check states itself --
/// including the ones that passed.</summary>
public record PreCheck(string Label, PreCheckState State);

/// <summary>Where a transaction of this size goes, and why.</summary>
public record PreCheckRouting(
    string ApproverName,
    RpApprovalAuthority? Authority,
    bool OverApproverLimit,
    string Summary);

/// <summary>Everything the stage-1 "system pre-checks" box in the workflow stands for: is the
/// counter-party actually a related party, is the approver conflicted, is the value sane, and where
/// does this route. Previously none of it happened -- the counter-party was a free dropdown and the
/// requestor submitted into silence.</summary>
public record PreCheckResult(IReadOnlyList<PreCheck> Checks, PreCheckRouting Routing)
{
    public bool AnyFailed => Checks.Any(c => c.State == PreCheckState.Fail);
    public bool AnyFlagged => Checks.Any(c => c.State != PreCheckState.Pass);
}

public interface IRpTransactionPreCheckService
{
    Task<PreCheckResult> RunAsync(int memberId, string? counterPartyName, decimal? transactionValue);
}

public class RpTransactionPreCheckService(IDbContextFactory<ApplicationDbContext> dbFactory) : IRpTransactionPreCheckService
{
    public async Task<PreCheckResult> RunAsync(int memberId, string? counterPartyName, decimal? transactionValue)
    {
        await using var db = await dbFactory.CreateDbContextAsync();

        var member = await db.Members
            .AsNoTracking()
            .Include(m => m.Company)
            .FirstOrDefaultAsync(m => m.Id == memberId);

        var checks = new List<PreCheck>();

        var match = counterPartyName is { Length: > 0 }
            ? await FindOnMasterAsync(db, counterPartyName.Trim())
            : null;

        checks.Add(counterPartyName is not { Length: > 0 }
            ? new PreCheck("Choose a counter-party", PreCheckState.Fail)
            : match is null
                // A warning rather than a failure: the master is maintained by people, and a
                // transaction with a counter-party nobody has registered yet is exactly the one an
                // approver should be told about rather than one the system should refuse.
                ? new PreCheck($"“{counterPartyName.Trim()}” is not on the Related Party Master", PreCheckState.Warn)
                : new PreCheck($"Counter-party is a related party — {match.Why}", PreCheckState.Pass));

        // The requestor's own register is what makes a counter-party theirs; anyone else's makes it
        // a related party of the company but not of them, which is a different thing to declare.
        if (match is not null)
        {
            checks.Add(match.MemberId == memberId
                ? new PreCheck("The relationship is yours to declare", PreCheckState.Pass)
                : new PreCheck($"Related through {match.OwnerName}, not you", PreCheckState.Warn));
        }

        checks.Add(transactionValue is > 0
            ? new PreCheck("Value is above zero", PreCheckState.Pass)
            : new PreCheck("Value must be above zero", PreCheckState.Fail));

        var routing = await RouteAsync(db, member, transactionValue);

        // The approver being the related party is the conflict the workflow escalates for, and the
        // one nobody can be expected to notice by eye.
        if (match is not null && member?.CompanyId is { } companyId)
        {
            var approverId = await db.Companies
                .AsNoTracking()
                .Where(c => c.Id == companyId)
                .Select(c => c.ApprovingAuthorityMemberId)
                .FirstOrDefaultAsync();

            if (approverId is { } id && id == match.MemberId)
            {
                checks.Add(new PreCheck($"{routing.ApproverName} is the related party here — this escalates to the CCAO", PreCheckState.Warn));
            }
        }

        if (match?.TradeLicenceExpiry is { } expiry && expiry.Date < DateTime.Today)
        {
            checks.Add(new PreCheck($"Trade licence expired {expiry:dd/MM/yyyy}", PreCheckState.Warn));
        }

        return new PreCheckResult(checks, routing);
    }

    /// <summary>Where this value routes, per the entity's Delegation of Authority. An entity with no
    /// matrix keeps what it had: its single approver, whatever the value.</summary>
    private static async Task<PreCheckRouting> RouteAsync(ApplicationDbContext db, Member? member, decimal? value)
    {
        var approverName = member?.CompanyId is { } companyId
            ? await db.Companies.AsNoTracking()
                .Where(c => c.Id == companyId)
                .Select(c => c.ApprovingAuthorityMember!.FullName)
                .FirstOrDefaultAsync() ?? "your approver"
            : "your approver";

        var matrix = member?.CompanyId is { } id
            ? await db.DelegationsOfAuthority.AsNoTracking()
                .Include(d => d.Bands)
                .FirstOrDefaultAsync(d => d.CompanyId == id)
            : null;

        if (matrix is null)
        {
            return new PreCheckRouting(approverName, null, false,
                $"{approverName}. No Delegation of Authority is configured for this entity, so value does not change the route.");
        }

        if (value is not > 0)
        {
            return new PreCheckRouting(approverName, null, false,
                $"{approverName}. Enter a value to see which authority it needs.");
        }

        // Over the approver's limit the transaction does not go to them at all -- that is the
        // workflow's "escalate: over limit", and it is separate from which body must ultimately
        // approve, which the band says.
        var overLimit = matrix.ApproverLimit is { } limit && value.Value > limit;
        var band = matrix.BandFor(value.Value);

        var route = overLimit
            ? $"Straight to the CCAO — above {approverName}'s limit of AED {matrix.ApproverLimit!.Value:N0}"
            : approverName;

        return new PreCheckRouting(approverName, band?.Authority, overLimit,
            band is null
                ? $"{route}. No band covers this value, so no governance body is named."
                : $"{route}, then {RpApprovalAuthorities.Label(band.Authority)} ({band.Range})");
    }

    private record MasterMatch(int MemberId, string OwnerName, string Why, DateTime? TradeLicenceExpiry);

    /// <summary>Looks the counter-party up on the standing Related Party Master -- the entities, the
    /// members, their relatives and the companies either holds. This is what §3.2 means by "Related
    /// Party Master"; the transaction form's own dropdown is still fed from submitted declarations
    /// (RelatedPartyMasterSource), so a name can be selectable here and unknown to this check. That
    /// mismatch is exactly what the warning is for.</summary>
    private static async Task<MasterMatch?> FindOnMasterAsync(ApplicationDbContext db, string name)
    {
        var company = await db.Companies.AsNoTracking()
            .Where(c => c.Name == name)
            .Select(c => new { c.Name, c.EntityType })
            .FirstOrDefaultAsync();
        if (company is not null)
        {
            return new MasterMatch(0, company.Name, $"group entity ({CompanyEntityTypes.Label(company.EntityType)})", null);
        }

        var owned = await db.OwnedCompanies.AsNoTracking()
            .Include(o => o.Member)
            .Where(o => o.CompanyName == name)
            .FirstOrDefaultAsync();
        if (owned is not null)
        {
            var nature = owned.NatureOfHolding == RelatedPartyHoldingNature.None ? "held" : owned.NatureOfHolding.ToString();
            return new MasterMatch(owned.MemberId, owned.Member?.FullName ?? "a member",
                $"{owned.Member?.FullName ?? "a member"}'s company ({nature})", owned.TradeLicenceExpiryDate);
        }

        var holding = await db.FamilyMemberHoldings.AsNoTracking()
            .Include(h => h.FamilyMember).ThenInclude(f => f!.Member)
            .Where(h => h.CompanyName == name)
            .FirstOrDefaultAsync();
        if (holding?.FamilyMember is { } relative)
        {
            return new MasterMatch(relative.MemberId, relative.Member?.FullName ?? "a member",
                $"{relative.Name}'s company — {relative.Name} is {relative.Member?.FullName ?? "a member"}'s {relative.Relationship}",
                holding.TradeLicenceExpiryDate);
        }

        var person = await db.FamilyMembers.AsNoTracking()
            .Include(f => f.Member)
            .Where(f => f.Name == name)
            .FirstOrDefaultAsync();
        if (person is not null)
        {
            return new MasterMatch(person.MemberId, person.Member?.FullName ?? "a member",
                $"{person.Member?.FullName ?? "a member"}'s {person.Relationship}", null);
        }

        var self = await db.Members.AsNoTracking()
            .Where(m => m.FullName == name)
            .Select(m => new { m.Id, m.FullName })
            .FirstOrDefaultAsync();
        return self is null ? null : new MasterMatch(self.Id, self.FullName, "a user of this system", null);
    }
}
