using System.Data;
using Microsoft.Data.SqlClient;

namespace CGTOOL.Web.Data.Governance;

public interface IDeclarationSetupWriter
{
    Task<int> InsertAsync(DeclarationSetup setup);
    Task UpdateAsync(DeclarationSetup setup);
    Task SetLastReminderSentUtcAsync(int id, DateTime lastReminderSentUtc);
}

public class DeclarationSetupWriter(IStoredProcedureExecutor sp) : IDeclarationSetupWriter
{
    public Task<int> InsertAsync(DeclarationSetup setup) => sp.InsertAsync("dbo.usp_DeclarationSetup_Insert",
        new SqlParameter("@Type", (int)setup.Type),
        DateParam("@Date", setup.Date),
        DateParam("@DueDate", setup.DueDate),
        new SqlParameter("@Active", setup.Active));

    public Task UpdateAsync(DeclarationSetup setup) => sp.ExecuteAsync("dbo.usp_DeclarationSetup_Update",
        new SqlParameter("@Id", setup.Id),
        new SqlParameter("@Type", (int)setup.Type),
        DateParam("@Date", setup.Date),
        DateParam("@DueDate", setup.DueDate),
        new SqlParameter("@Active", setup.Active));

    public Task SetLastReminderSentUtcAsync(int id, DateTime lastReminderSentUtc) => sp.ExecuteAsync(
        "dbo.usp_DeclarationSetup_SetLastReminderSentUtc",
        new SqlParameter("@Id", id), new SqlParameter("@LastReminderSentUtc", lastReminderSentUtc));

    private static SqlParameter DateParam(string name, DateOnly value) =>
        new(name, SqlDbType.Date) { Value = value.ToDateTime(TimeOnly.MinValue) };
}
