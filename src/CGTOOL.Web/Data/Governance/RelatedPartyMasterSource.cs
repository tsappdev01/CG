using Microsoft.EntityFrameworkCore;

namespace CGTOOL.Web.Data.Governance;

/// <summary>One selectable counter-party, and what makes it one. The group is what the dropdown
/// puts it under; the description is the line that says why this name is a related party at all,
/// which a bare list of names never did.</summary>
public record RelatedPartyOption(string Name, string Group, string? Description);

/// <summary>The counter-party list for the RP Transaction form: the standing Related Party Master,
/// which is what §3.2 of the Dec 2018 spec names
/// (scripts/rp-transaction-field-rules.md).
///
/// It used to read the names out of submitted Related Party &amp; COI declarations instead. That had
/// two consequences worth remembering, because they are why this changed: a deployment with no
/// submitted declarations had an empty dropdown and nobody could raise a transaction at all, and a
/// related party maintained in My Register but not yet declared for the current quarter could not be
/// transacted with. The master has neither problem -- it is reference data, maintained directly, and
/// does not wait for a declaration cycle.
///
/// Declarations are deliberately NOT a source. A name declared once and never added to My Register
/// is not on the master, and offering it here would say it is. Where that name is the counter-party
/// of a transaction already raised, the amend form keeps it selectable for that transaction alone
/// (see RpTransactionsPage.AmendOptions) so amending something else cannot silently change who it
/// was with -- but nothing puts it back in the list for new transactions. The fix for a name that
/// should be there is to add it to My Register.</summary>
public static class RelatedPartyMasterSource
{
    public const string Entities = "Group entities";
    public const string Users = "Users";
    public const string Relatives = "Relatives";
    public const string MemberCompanies = "Members' companies";
    public const string RelativeCompanies = "Relatives' companies";

    /// <summary>The order the groups appear in the dropdown: the company's own entities first, then
    /// people, then the companies behind them.</summary>
    private static readonly string[] GroupOrder =
        [Entities, Users, Relatives, MemberCompanies, RelativeCompanies];

    /// <summary>Every related party, grouped. Names are distinct across the whole list -- a company
    /// held by two relatives is one option, described by the first relationship found, not two
    /// identical lines the requestor has to choose between.</summary>
    public static async Task<List<RelatedPartyOption>> GetOptionsAsync(ApplicationDbContext db)
    {
        var options = new List<RelatedPartyOption>();

        options.AddRange(await db.Companies
            .AsNoTracking()
            .Where(c => c.Active)
            .Select(c => new RelatedPartyOption(c.Name, Entities, c.ShortCode))
            .ToListAsync());

        options.AddRange(await db.Members
            .AsNoTracking()
            .Where(m => m.Active)
            .Select(m => new RelatedPartyOption(m.FullName, Users, m.Company!.Name))
            .ToListAsync());

        options.AddRange(await db.FamilyMembers
            .AsNoTracking()
            .Select(f => new RelatedPartyOption(
                f.Name, Relatives, f.Member!.FullName + "'s " + f.Relationship.ToString()))
            .ToListAsync());

        options.AddRange(await db.OwnedCompanies
            .AsNoTracking()
            .Select(o => new RelatedPartyOption(
                o.CompanyName, MemberCompanies, o.Member!.FullName))
            .ToListAsync());

        options.AddRange(await db.FamilyMemberHoldings
            .AsNoTracking()
            .Select(h => new RelatedPartyOption(
                h.CompanyName, RelativeCompanies,
                h.FamilyMember!.Name + " — " + h.FamilyMember.Member!.FullName + "'s " + h.FamilyMember.Relationship.ToString()))
            .ToListAsync());

        return options
            .Where(o => !string.IsNullOrWhiteSpace(o.Name))
            .GroupBy(o => o.Name.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First() with { Name = g.Key })
            .OrderBy(o => Array.IndexOf(GroupOrder, o.Group))
            .ThenBy(o => o.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>The groups in display order, with their options. Empty groups are left out. A group
    /// this class does not define -- the amend form's "On this transaction" -- sorts first, which is
    /// where the transaction's own counter-party belongs.</summary>
    public static IEnumerable<IGrouping<string, RelatedPartyOption>> Grouped(IEnumerable<RelatedPartyOption> options) =>
        options
            .GroupBy(o => o.Group)
            .OrderBy(g => Array.IndexOf(GroupOrder, g.Key));
}
