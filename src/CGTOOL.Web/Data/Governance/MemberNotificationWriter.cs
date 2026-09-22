using Microsoft.Data.SqlClient;

namespace CGTOOL.Web.Data.Governance;

public interface IMemberNotificationWriter
{
    Task<int> InsertAsync(int memberId, string recipient, MemberNotificationStatus status);
    Task SetStatusAsync(int id, MemberNotificationStatus status, DateTime? sentAtUtc);
}

public class MemberNotificationWriter(IStoredProcedureExecutor sp) : IMemberNotificationWriter
{
    public Task<int> InsertAsync(int memberId, string recipient, MemberNotificationStatus status) => sp.InsertAsync(
        "dbo.usp_MemberNotification_Insert",
        new SqlParameter("@MemberId", memberId),
        new SqlParameter("@Recipient", recipient),
        new SqlParameter("@Status", (int)status));

    public Task SetStatusAsync(int id, MemberNotificationStatus status, DateTime? sentAtUtc) => sp.ExecuteAsync(
        "dbo.usp_MemberNotification_SetStatus",
        new SqlParameter("@Id", id),
        new SqlParameter("@Status", (int)status),
        new SqlParameter("@SentAtUtc", (object?)sentAtUtc ?? DBNull.Value));
}
