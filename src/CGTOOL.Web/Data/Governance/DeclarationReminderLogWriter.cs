using Microsoft.Data.SqlClient;

namespace CGTOOL.Web.Data.Governance;

public interface IDeclarationReminderLogWriter
{
    Task<int> InsertAsync(DeclarationReminderLog entry);
}

public class DeclarationReminderLogWriter(IStoredProcedureExecutor sp) : IDeclarationReminderLogWriter
{
    public Task<int> InsertAsync(DeclarationReminderLog entry) => sp.InsertAsync(
        "dbo.usp_DeclarationReminderLog_Insert",
        new SqlParameter("@MemberId", entry.MemberId),
        new SqlParameter("@MemberName", entry.MemberName),
        new SqlParameter("@Email", entry.Email),
        new SqlParameter("@CompanyName", (object?)entry.CompanyName ?? DBNull.Value),
        new SqlParameter("@Category", (int)entry.Category),
        new SqlParameter("@Year", entry.Year),
        new SqlParameter("@Quarter", entry.Quarter),
        new SqlParameter("@SentByName", entry.SentByName));
}
