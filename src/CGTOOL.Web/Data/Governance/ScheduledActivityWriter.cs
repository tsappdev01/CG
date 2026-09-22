using Microsoft.Data.SqlClient;

namespace CGTOOL.Web.Data.Governance;

public interface IScheduledActivityWriter
{
    Task SetActiveAsync(int id, bool active);
    Task<int> InsertDateAsync(int scheduledActivityId, int month, int day);
    Task DeleteDateAsync(int id);
    Task SetDateLastTriggeredAsync(int id, DateTime lastTriggeredUtc);

    /// <summary>Returns the Id of the singleton ScheduledActivity row for this type, creating it
    /// (inactive, no dates) if it doesn't exist yet -- there's no general "add activity" UI since
    /// each Type is meant to have exactly one row.</summary>
    Task<int> EnsureExistsAsync(ScheduledActivityType type);
}

public class ScheduledActivityWriter(IStoredProcedureExecutor sp) : IScheduledActivityWriter
{
    public Task SetActiveAsync(int id, bool active) => sp.ExecuteAsync("dbo.usp_ScheduledActivity_SetActive",
        new SqlParameter("@Id", id), new SqlParameter("@Active", active));

    public Task<int> EnsureExistsAsync(ScheduledActivityType type) => sp.InsertAsync(
        "dbo.usp_ScheduledActivity_EnsureExists", new SqlParameter("@Type", (int)type));

    public Task<int> InsertDateAsync(int scheduledActivityId, int month, int day) => sp.InsertAsync(
        "dbo.usp_ScheduledActivityDate_Insert",
        new SqlParameter("@ScheduledActivityId", scheduledActivityId),
        new SqlParameter("@Month", month),
        new SqlParameter("@Day", day));

    public Task DeleteDateAsync(int id) => sp.ExecuteAsync("dbo.usp_ScheduledActivityDate_Delete", new SqlParameter("@Id", id));

    public Task SetDateLastTriggeredAsync(int id, DateTime lastTriggeredUtc) => sp.ExecuteAsync(
        "dbo.usp_ScheduledActivityDate_SetLastTriggered",
        new SqlParameter("@Id", id), new SqlParameter("@LastTriggeredUtc", lastTriggeredUtc));
}
