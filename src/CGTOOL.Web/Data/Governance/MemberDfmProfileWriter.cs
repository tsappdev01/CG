using Microsoft.Data.SqlClient;

namespace CGTOOL.Web.Data.Governance;

public interface IMemberDfmProfileWriter
{
    /// <summary>Inserts the member's row or updates the one that is there. An upsert because the
    /// screen is a form the member fills in, not a record they create: whether a row exists yet is
    /// not something they should have to think about.</summary>
    Task<int> SaveAsync(MemberDfmProfile profile);
}

public class MemberDfmProfileWriter(IStoredProcedureExecutor sp) : IMemberDfmProfileWriter
{
    public Task<int> SaveAsync(MemberDfmProfile profile) => sp.InsertAsync(
        "dbo.usp_MemberDfmProfile_Save",
        new SqlParameter("@MemberId", profile.MemberId),
        new SqlParameter("@InvestorNumber", (object?)profile.InvestorNumber ?? DBNull.Value),
        new SqlParameter("@InvestorType", (object?)(int?)profile.InvestorType ?? DBNull.Value),
        new SqlParameter("@FullName", (object?)profile.FullName ?? DBNull.Value),
        new SqlParameter("@Nationality", (object?)profile.Nationality ?? DBNull.Value),
        new SqlParameter("@Gender", (object?)(int?)profile.Gender ?? DBNull.Value),
        new SqlParameter("@DateOfBirth", (object?)profile.DateOfBirth ?? DBNull.Value),
        new SqlParameter("@MobileNumber", (object?)profile.MobileNumber ?? DBNull.Value),
        new SqlParameter("@EmailAddress", (object?)profile.EmailAddress ?? DBNull.Value),
        new SqlParameter("@ResidentialAddress", (object?)profile.ResidentialAddress ?? DBNull.Value),
        new SqlParameter("@City", (object?)profile.City ?? DBNull.Value),
        new SqlParameter("@Country", (object?)profile.Country ?? DBNull.Value),
        new SqlParameter("@PoBox", (object?)profile.PoBox ?? DBNull.Value),
        new SqlParameter("@AccountStatus", (object?)(int?)profile.AccountStatus ?? DBNull.Value),
        new SqlParameter("@RegistrationDate", (object?)profile.RegistrationDate ?? DBNull.Value),
        new SqlParameter("@LastUpdatedDate", (object?)profile.LastUpdatedDate ?? DBNull.Value));
}
