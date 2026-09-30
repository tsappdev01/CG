using System.Data;
using Microsoft.Data.SqlClient;

namespace CGTOOL.Web.Data.Governance;

public interface IRelatedPartyTransactionWriter
{
    Task<int> InsertAsync(RelatedPartyTransaction t);

    Task AmendAsync(int id, string counterPartyName, decimal transactionValue, string description);

    Task RecordApproverActionAsync(RelatedPartyTransaction t);

    /// <summary>Puts a returned transaction back in front of its approver after the requestor
    /// has amended it.</summary>
    Task ResubmitAsync(int id);

    Task RecordCcaoActionAsync(RelatedPartyTransaction t);

    Task ReleaseAsync(int id, DateTime releasedAtUtc);

    Task AutoEscalateAsync(int id, RpEscalationReason reason, DateTime escalatedAtUtc);

    Task SetLastReminderSentAsync(int id, DateTime sentAtUtc);

    Task<int> InsertDocumentAsync(int transactionId, RelatedPartyTransactionDocument document);

    /// <summary>The CCAO writing down the offline decision -- which body, when, who abstained.</summary>
    Task RecordGovernanceDecisionAsync(RelatedPartyTransaction t);
}

public class RelatedPartyTransactionWriter(IStoredProcedureExecutor sp) : IRelatedPartyTransactionWriter
{
    public Task<int> InsertAsync(RelatedPartyTransaction t) => sp.InsertAsync(
        "dbo.usp_RelatedPartyTransaction_Insert",
        new SqlParameter("@CompanyId", t.CompanyId),
        new SqlParameter("@MemberId", t.MemberId),
        new SqlParameter("@CounterPartyName", t.CounterPartyName),
        new SqlParameter("@TransactionValue", t.TransactionValue),
        new SqlParameter("@Description", t.Description),
        new SqlParameter("@DateOfRequest", t.DateOfRequest),
        new SqlParameter("@Status", (int)t.Status),
        new SqlParameter("@ApproverMemberId", (object?)t.ApproverMemberId ?? DBNull.Value),
        new SqlParameter("@EscalationReason", (int)t.EscalationReason),
        new SqlParameter("@EscalatedAtUtc", (object?)t.EscalatedAtUtc ?? DBNull.Value),
        new SqlParameter("@SubmittedByName", t.SubmittedByName),
        new SqlParameter("@SubmittedOnBehalfOf", (object?)t.SubmittedOnBehalfOf ?? DBNull.Value));

    public Task AmendAsync(int id, string counterPartyName, decimal transactionValue, string description) => sp.ExecuteAsync(
        "dbo.usp_RelatedPartyTransaction_Amend",
        new SqlParameter("@Id", id),
        new SqlParameter("@CounterPartyName", counterPartyName),
        new SqlParameter("@TransactionValue", transactionValue),
        new SqlParameter("@Description", description));

    public Task RecordApproverActionAsync(RelatedPartyTransaction t) => sp.ExecuteAsync(
        "dbo.usp_RelatedPartyTransaction_RecordApproverAction",
        new SqlParameter("@Id", t.Id),
        new SqlParameter("@ApproverAction", (int)t.ApproverAction!.Value),
        new SqlParameter("@ApproverRemarks", (object?)t.ApproverRemarks ?? DBNull.Value),
        new SqlParameter("@ApproverActionAtUtc", t.ApproverActionAtUtc!.Value),
        new SqlParameter("@Status", (int)t.Status),
        new SqlParameter("@EscalationReason", (int)t.EscalationReason),
        new SqlParameter("@EscalatedAtUtc", (object?)t.EscalatedAtUtc ?? DBNull.Value));

    // The status is spelled out with its type rather than passed as a bare value, because
    // AwaitingApproval is zero and SqlParameter has both (string, object) and (string, SqlDbType)
    // constructors: a compile-time 0 is implicitly convertible to any enum, so C# picks the
    // SqlDbType overload and builds a parameter that carries a type and no value. It compiles, and
    // SQL Server then refuses the call with "expects parameter '@Status', which was not supplied".
    public Task ResubmitAsync(int id) => sp.ExecuteAsync(
        "dbo.usp_RelatedPartyTransaction_Resubmit",
        new SqlParameter("@Id", id),
        new SqlParameter("@Status", SqlDbType.Int) { Value = (int)RpTransactionStatus.AwaitingApproval });

    public Task RecordCcaoActionAsync(RelatedPartyTransaction t) => sp.ExecuteAsync(
        "dbo.usp_RelatedPartyTransaction_RecordCcaoAction",
        new SqlParameter("@Id", t.Id),
        new SqlParameter("@CcaoAction", (int)t.CcaoAction!.Value),
        new SqlParameter("@CcaoRemarks", (object?)t.CcaoRemarks ?? DBNull.Value),
        new SqlParameter("@CcaoActionAtUtc", t.CcaoActionAtUtc!.Value),
        new SqlParameter("@Status", (int)t.Status));

    public Task ReleaseAsync(int id, DateTime releasedAtUtc) => sp.ExecuteAsync(
        "dbo.usp_RelatedPartyTransaction_Release",
        new SqlParameter("@Id", id),
        new SqlParameter("@ReleasedAtUtc", releasedAtUtc));

    public Task AutoEscalateAsync(int id, RpEscalationReason reason, DateTime escalatedAtUtc) => sp.ExecuteAsync(
        "dbo.usp_RelatedPartyTransaction_AutoEscalate",
        new SqlParameter("@Id", id),
        new SqlParameter("@EscalationReason", (int)reason),
        new SqlParameter("@EscalatedAtUtc", escalatedAtUtc));

    public Task SetLastReminderSentAsync(int id, DateTime sentAtUtc) => sp.ExecuteAsync(
        "dbo.usp_RelatedPartyTransaction_SetLastReminderSent",
        new SqlParameter("@Id", id),
        new SqlParameter("@LastReminderSentUtc", sentAtUtc));

    public Task<int> InsertDocumentAsync(int transactionId, RelatedPartyTransactionDocument document) => sp.InsertAsync(
        "dbo.usp_RelatedPartyTransactionDocument_Insert",
        new SqlParameter("@RelatedPartyTransactionId", transactionId),
        new SqlParameter("@FilePath", document.FilePath),
        new SqlParameter("@FileName", document.FileName),
        new SqlParameter("@Kind", (int)document.Kind));

    public Task RecordGovernanceDecisionAsync(RelatedPartyTransaction t) => sp.ExecuteAsync(
        "dbo.usp_RelatedPartyTransaction_RecordGovernanceDecision",
        new SqlParameter("@Id", t.Id),
        new SqlParameter("@GoverningBody", (int)t.GoverningBody!.Value),
        new SqlParameter("@GoverningBodyDecisionDate", t.GoverningBodyDecisionDate!.Value),
        new SqlParameter("@AbstainedMembers", (object?)t.AbstainedMembers ?? DBNull.Value));
}
