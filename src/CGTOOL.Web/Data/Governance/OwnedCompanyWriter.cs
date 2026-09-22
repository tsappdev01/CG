using Microsoft.Data.SqlClient;

namespace CGTOOL.Web.Data.Governance;

public interface IOwnedCompanyWriter
{
    Task<int> InsertAsync(OwnedCompany company);
    Task UpdateAsync(OwnedCompany company);
    Task DeleteAsync(int id);
}

public class OwnedCompanyWriter(IStoredProcedureExecutor sp) : IOwnedCompanyWriter
{
    public Task<int> InsertAsync(OwnedCompany company) => sp.InsertAsync("dbo.usp_OwnedCompany_Insert",
        new SqlParameter("@MemberId", company.MemberId),
        new SqlParameter("@CompanyName", company.CompanyName),
        new SqlParameter("@TradeLicenseDetails", (object?)company.TradeLicenseDetails ?? DBNull.Value),
        new SqlParameter("@OwnershipPercentage", (object?)company.OwnershipPercentage ?? DBNull.Value));

    public Task UpdateAsync(OwnedCompany company) => sp.ExecuteAsync("dbo.usp_OwnedCompany_Update",
        new SqlParameter("@Id", company.Id),
        new SqlParameter("@CompanyName", company.CompanyName),
        new SqlParameter("@TradeLicenseDetails", (object?)company.TradeLicenseDetails ?? DBNull.Value),
        new SqlParameter("@OwnershipPercentage", (object?)company.OwnershipPercentage ?? DBNull.Value),
        new SqlParameter("@TradeLicensePath", (object?)company.TradeLicensePath ?? DBNull.Value),
        new SqlParameter("@MoaPath", (object?)company.MoaPath ?? DBNull.Value),
        new SqlParameter("@PoaPath", (object?)company.PoaPath ?? DBNull.Value));

    public Task DeleteAsync(int id) => sp.ExecuteAsync("dbo.usp_OwnedCompany_Delete", new SqlParameter("@Id", id));
}
