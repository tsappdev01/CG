using Microsoft.Data.SqlClient;

namespace CGTOOL.Web.Data.Governance;

public interface IAuditLogReviewWriter
{
    Task UpsertAsync(int auditLogEntryId, string reviewerName, AuditReviewOutcome outcome, string comment);
}

/// <summary>Writes a reviewer's sign-off via dbo.usp_AuditLogReview_Upsert. One per entry --
/// signing off again replaces the previous assertion rather than stacking.</summary>
public class AuditLogReviewWriter(IStoredProcedureExecutor sp) : IAuditLogReviewWriter
{
    public Task UpsertAsync(int auditLogEntryId, string reviewerName, AuditReviewOutcome outcome, string comment) =>
        sp.ExecuteAsync("dbo.usp_AuditLogReview_Upsert",
            new SqlParameter("@AuditLogEntryId", auditLogEntryId),
            new SqlParameter("@ReviewerName", reviewerName),
            new SqlParameter("@Outcome", (int)outcome),
            new SqlParameter("@Comment", comment));
}
