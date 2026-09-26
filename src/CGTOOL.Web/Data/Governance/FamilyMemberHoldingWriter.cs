using Microsoft.Data.SqlClient;

namespace CGTOOL.Web.Data.Governance;

public interface IFamilyMemberHoldingWriter
{
    Task<int> InsertAsync(FamilyMemberHolding holding);
    Task UpdateAsync(FamilyMemberHolding holding);
    Task DeleteAsync(int id);
}

public class FamilyMemberHoldingWriter(IStoredProcedureExecutor sp) : IFamilyMemberHoldingWriter
{
    public Task<int> InsertAsync(FamilyMemberHolding holding) => sp.InsertAsync("dbo.usp_FamilyMemberHolding_Insert",
        new SqlParameter("@FamilyMemberId", holding.FamilyMemberId),
        new SqlParameter("@CompanyName", holding.CompanyName),
        new SqlParameter("@NatureOfHolding", (int)holding.NatureOfHolding),
        new SqlParameter("@OwnershipPercentage", (object?)holding.OwnershipPercentage ?? DBNull.Value));

    public Task UpdateAsync(FamilyMemberHolding holding) => sp.ExecuteAsync("dbo.usp_FamilyMemberHolding_Update",
        new SqlParameter("@Id", holding.Id),
        new SqlParameter("@CompanyName", holding.CompanyName),
        new SqlParameter("@NatureOfHolding", (int)holding.NatureOfHolding),
        new SqlParameter("@OwnershipPercentage", (object?)holding.OwnershipPercentage ?? DBNull.Value));

    public Task DeleteAsync(int id) => sp.ExecuteAsync("dbo.usp_FamilyMemberHolding_Delete", new SqlParameter("@Id", id));
}
