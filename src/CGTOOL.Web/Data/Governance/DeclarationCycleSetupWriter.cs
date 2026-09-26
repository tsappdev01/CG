using Microsoft.Data.SqlClient;

namespace CGTOOL.Web.Data.Governance;

public interface IDeclarationCycleSetupWriter
{
    /// <summary>Every Type has exactly one settings row; creates it (zero reminders, blank template)
    /// the first time that type's page is visited if missing.</summary>
    Task<int> EnsureExistsAsync(DeclarationCycleType type);

    Task UpdateAsync(DeclarationCycleSetup setup);
}

public class DeclarationCycleSetupWriter(IStoredProcedureExecutor sp) : IDeclarationCycleSetupWriter
{
    public Task<int> EnsureExistsAsync(DeclarationCycleType type) => sp.InsertAsync(
        "dbo.usp_DeclarationCycleSetup_EnsureExists",
        new SqlParameter("@Type", (int)type));

    public Task UpdateAsync(DeclarationCycleSetup setup) => sp.ExecuteAsync(
        "dbo.usp_DeclarationCycleSetup_Update",
        new SqlParameter("@Id", setup.Id),
        new SqlParameter("@ReminderCount", setup.ReminderCount),
        new SqlParameter("@ReminderDayOfWeek", (int)setup.ReminderDayOfWeek),
        new SqlParameter("@ReminderTimeOfDay", setup.ReminderTimeOfDay.ToTimeSpan()),
        new SqlParameter("@EmailSubject", setup.EmailSubject),
        new SqlParameter("@EmailBody", setup.EmailBody));
}
