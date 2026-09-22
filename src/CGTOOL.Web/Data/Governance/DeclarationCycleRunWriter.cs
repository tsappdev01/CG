using Microsoft.Data.SqlClient;

namespace CGTOOL.Web.Data.Governance;

public interface IDeclarationCycleRunWriter
{
    Task<int> InsertAsync(DeclarationCycleRun run);

    Task MarkSentAsync(int id, DateTime sentAtUtc, int recipientCount);

    Task UpdateDueDateAsync(int id, DateTime dueDateUtc);

    Task DeleteAsync(int id);

    Task RecallAsync(int id, DateTime recalledAtUtc, string recalledByName);

    Task IncrementReminderAsync(int id, DateTime lastReminderSentUtc);

    Task<int> InsertRecipientAsync(DeclarationCycleRunRecipient recipient);
}

public class DeclarationCycleRunWriter(IStoredProcedureExecutor sp) : IDeclarationCycleRunWriter
{
    public Task<int> InsertAsync(DeclarationCycleRun run) => sp.InsertAsync(
        "dbo.usp_DeclarationCycleRun_Insert",
        new SqlParameter("@DeclarationCycleSetupId", run.DeclarationCycleSetupId),
        new SqlParameter("@Type", (int)run.Type),
        new SqlParameter("@CompanyId", (object?)run.CompanyId ?? DBNull.Value),
        new SqlParameter("@SentAtUtc", run.SentAtUtc),
        new SqlParameter("@PeriodYear", run.PeriodYear),
        new SqlParameter("@PeriodQuarter", run.PeriodQuarter),
        new SqlParameter("@DueDateUtc", run.DueDateUtc),
        new SqlParameter("@Sent", run.Sent),
        new SqlParameter("@RecipientCount", run.RecipientCount));

    public Task MarkSentAsync(int id, DateTime sentAtUtc, int recipientCount) => sp.ExecuteAsync(
        "dbo.usp_DeclarationCycleRun_MarkSent",
        new SqlParameter("@Id", id),
        new SqlParameter("@SentAtUtc", sentAtUtc),
        new SqlParameter("@RecipientCount", recipientCount));

    public Task UpdateDueDateAsync(int id, DateTime dueDateUtc) => sp.ExecuteAsync(
        "dbo.usp_DeclarationCycleRun_UpdateDueDate",
        new SqlParameter("@Id", id),
        new SqlParameter("@DueDateUtc", dueDateUtc));

    public Task DeleteAsync(int id) => sp.ExecuteAsync(
        "dbo.usp_DeclarationCycleRun_Delete",
        new SqlParameter("@Id", id));

    public Task RecallAsync(int id, DateTime recalledAtUtc, string recalledByName) => sp.ExecuteAsync(
        "dbo.usp_DeclarationCycleRun_Recall",
        new SqlParameter("@Id", id),
        new SqlParameter("@RecalledAtUtc", recalledAtUtc),
        new SqlParameter("@RecalledByName", recalledByName));

    public Task IncrementReminderAsync(int id, DateTime lastReminderSentUtc) => sp.ExecuteAsync(
        "dbo.usp_DeclarationCycleRun_IncrementReminder",
        new SqlParameter("@Id", id),
        new SqlParameter("@LastReminderSentUtc", lastReminderSentUtc));

    public Task<int> InsertRecipientAsync(DeclarationCycleRunRecipient recipient) => sp.InsertAsync(
        "dbo.usp_DeclarationCycleRunRecipient_Insert",
        new SqlParameter("@DeclarationCycleRunId", recipient.DeclarationCycleRunId),
        new SqlParameter("@MemberId", recipient.MemberId),
        new SqlParameter("@MemberName", recipient.MemberName),
        new SqlParameter("@Email", recipient.Email),
        new SqlParameter("@CompanyName", (object?)recipient.CompanyName ?? DBNull.Value),
        new SqlParameter("@Success", recipient.Success));
}
