using Microsoft.EntityFrameworkCore;

namespace CGTOOL.Web.Data.Governance;

/// <summary>Source for the RP Transaction form's "Name of the counter-party" dropdown -- every related
/// party in the company, i.e. the union of both Related Party Register tabs (RelatedPartyRegister.razor):
/// individuals (a declarant's declared relatives, or the declarant's own name as a fallback when they
/// declared none -- covering Normal User/BOD/Executive Member alike) plus companies (I.B/I.C/I.D
/// interests). Not a separate, independently-maintained master -- always mirrors what's actually
/// declared.</summary>
public static class RelatedPartyMasterSource
{
    public static async Task<List<string>> GetNamesAsync(ApplicationDbContext db)
    {
        var declarations = await db.RelatedPartyCoiDeclarations
            .Include(d => d.Member)
            .Include(d => d.Relatives)
            .Include(d => d.Companies)
            .Where(d => !d.IsDraft)
            .ToListAsync();

        var individualNames = declarations.SelectMany(d => d.Relatives.Count > 0
            ? d.Relatives.Select(r => r.Name)
            : [d.Member?.FullName ?? string.Empty]);

        var companyNames = declarations.SelectMany(d => d.Companies.Select(c => c.LegalCompanyName));

        return individualNames.Concat(companyNames)
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
