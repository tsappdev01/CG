using Microsoft.Data.SqlClient;

namespace CGTOOL.Web.Data.Governance;

public interface ICompanyWriter
{
    Task<int> InsertAsync(Company company);
    Task UpdateAsync(Company company);
    Task SetActiveAsync(int id, bool active);
    Task SetLogoPathAsync(int id, string? logoPath);
}

public class CompanyWriter(IStoredProcedureExecutor sp) : ICompanyWriter
{
    public Task<int> InsertAsync(Company company) => sp.InsertAsync("dbo.usp_Company_Insert", Params(company));

    public Task UpdateAsync(Company company) => sp.ExecuteAsync("dbo.usp_Company_Update",
        [new SqlParameter("@Id", company.Id), .. Params(company)]);

    public Task SetActiveAsync(int id, bool active) => sp.ExecuteAsync("dbo.usp_Company_SetActive",
        new SqlParameter("@Id", id), new SqlParameter("@Active", active));

    public Task SetLogoPathAsync(int id, string? logoPath) => sp.ExecuteAsync("dbo.usp_Company_SetLogoPath",
        new SqlParameter("@Id", id), new SqlParameter("@LogoPath", (object?)logoPath ?? DBNull.Value));

    private static SqlParameter[] Params(Company c) =>
    [
        new SqlParameter("@Name", c.Name),
        new SqlParameter("@ShortCode", c.ShortCode),
        new SqlParameter("@Address", (object?)c.Address ?? DBNull.Value),
        new SqlParameter("@City", (object?)c.City ?? DBNull.Value),
        new SqlParameter("@Country", (object?)c.Country ?? DBNull.Value),
        new SqlParameter("@Sector", (object?)c.Sector ?? DBNull.Value),
        new SqlParameter("@GroupName", (object?)c.GroupName ?? DBNull.Value),
        new SqlParameter("@ApprovingAuthorityMemberId", (object?)c.ApprovingAuthorityMemberId ?? DBNull.Value),
        new SqlParameter("@DelegateAuthorityMemberId", (object?)c.DelegateAuthorityMemberId ?? DBNull.Value),
        new SqlParameter("@Active", c.Active),
    ];
}
