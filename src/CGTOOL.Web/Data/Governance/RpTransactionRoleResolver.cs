using Microsoft.EntityFrameworkCore;

namespace CGTOOL.Web.Data.Governance;

/// <summary>Resolves notification recipients for the RP Transaction workflow's CCAO/CFO/COO/MD&amp;CEO
/// positions -- Member.RpTransactionRole is a dedicated field, entirely separate from ASP.NET Identity
/// system roles, so any number of active Members can hold a given position and this returns all of
/// their emails.</summary>
public static class RpTransactionRoleResolver
{
    /// <summary>The people holding a position, for naming them on screen. A position is not a
    /// person -- any number of active members can hold it -- so this returns all of them and the
    /// caller decides how to say that.</summary>
    public static Task<List<Member>> GetRoleMembersAsync(ApplicationDbContext db, RpTransactionRole role) =>
        db.Members
            .AsNoTracking()
            .Where(m => m.Active && m.RpTransactionRole == role)
            .OrderBy(m => m.FullName)
            .ToListAsync();

    public static async Task<List<string>> GetRoleEmailsAsync(ApplicationDbContext db, RpTransactionRole role)
    {
        return await db.Members
            .Where(m => m.Active && m.RpTransactionRole == role && m.Email != null)
            .Select(m => m.Email!)
            .Distinct()
            .ToListAsync();
    }
}
