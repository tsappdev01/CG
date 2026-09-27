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
        new SqlParameter("@OwnershipPercentage", (object?)holding.OwnershipPercentage ?? DBNull.Value),
        new SqlParameter("@PrincipalBusinessActivity", (object?)holding.PrincipalBusinessActivity ?? DBNull.Value),
        new SqlParameter("@NatureOfInterest", (object?)holding.NatureOfInterest ?? DBNull.Value),
        new SqlParameter("@TradeLicencePath", (object?)holding.TradeLicencePath ?? DBNull.Value),
        new SqlParameter("@TradeLicenceFileName", (object?)holding.TradeLicenceFileName ?? DBNull.Value),
        new SqlParameter("@TradeLicenceNumber", (object?)holding.TradeLicenceNumber ?? DBNull.Value),
        new SqlParameter("@TradeLicenceExpiryDate", (object?)holding.TradeLicenceExpiryDate ?? DBNull.Value),
        new SqlParameter("@LicenceActivities", (object?)holding.LicenceActivities ?? DBNull.Value));

    public Task UpdateAsync(FamilyMemberHolding holding) => sp.ExecuteAsync("dbo.usp_FamilyMemberHolding_Update",
        new SqlParameter("@Id", holding.Id),
        new SqlParameter("@CompanyName", holding.CompanyName),
        new SqlParameter("@NatureOfHolding", (int)holding.NatureOfHolding),
        new SqlParameter("@OwnershipPercentage", (object?)holding.OwnershipPercentage ?? DBNull.Value),
        new SqlParameter("@PrincipalBusinessActivity", (object?)holding.PrincipalBusinessActivity ?? DBNull.Value),
        new SqlParameter("@NatureOfInterest", (object?)holding.NatureOfInterest ?? DBNull.Value),
        new SqlParameter("@TradeLicencePath", (object?)holding.TradeLicencePath ?? DBNull.Value),
        new SqlParameter("@TradeLicenceFileName", (object?)holding.TradeLicenceFileName ?? DBNull.Value),
        new SqlParameter("@TradeLicenceNumber", (object?)holding.TradeLicenceNumber ?? DBNull.Value),
        new SqlParameter("@TradeLicenceExpiryDate", (object?)holding.TradeLicenceExpiryDate ?? DBNull.Value),
        new SqlParameter("@LicenceActivities", (object?)holding.LicenceActivities ?? DBNull.Value));

    public Task DeleteAsync(int id) => sp.ExecuteAsync("dbo.usp_FamilyMemberHolding_Delete", new SqlParameter("@Id", id));
}
