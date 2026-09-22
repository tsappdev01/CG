using Microsoft.EntityFrameworkCore;

namespace CGTOOL.Web.Data.Governance;

/// <summary>Resolves notification recipients for the RP Transaction workflow's CCAO/CFO/COO/MD&amp;CEO
/// positions -- Member.RpTransactionRole is a dedicated field, entirely separate from ASP.NET Identity
/// system roles, so any number of active Members can hold a given position and this returns all of
/// their emails.</summary>
public static class RpTransactionRoleResolver
{
    public static async Task<List<string>> GetRoleEmailsAsync(ApplicationDbContext db, RpTransactionRole role)
    {
        return await db.Members
            .Where(m => m.Active && m.RpTransactionRole == role && m.Email != null)
            .Select(m => m.Email!)
            .Distinct()
            .ToListAsync();
    }
}
