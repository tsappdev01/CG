using Microsoft.Data.SqlClient;

namespace CGTOOL.Web.Data.Governance;

public interface IMemberWriter
{
    Task<int> InsertAsync(Member member);
    Task UpdateAsync(Member member);
    Task SetActiveAsync(int id, bool active);
}

public class MemberWriter(IStoredProcedureExecutor sp) : IMemberWriter
{
    public Task<int> InsertAsync(Member m) => sp.InsertAsync("dbo.usp_Member_Insert", Params(m));

    public Task UpdateAsync(Member m) => sp.ExecuteAsync("dbo.usp_Member_Update",
        [new SqlParameter("@Id", m.Id), .. Params(m)]);

    public Task SetActiveAsync(int id, bool active) => sp.ExecuteAsync("dbo.usp_Member_SetActive",
        new SqlParameter("@Id", id), new SqlParameter("@Active", active));

    private static SqlParameter[] Params(Member m) =>
    [
        new SqlParameter("@CompanyId", m.CompanyId),
        new SqlParameter("@FullName", m.FullName),
        new SqlParameter("@JobTitle", (object?)m.JobTitle ?? DBNull.Value),
        new SqlParameter("@DepartmentId", (object?)m.DepartmentId ?? DBNull.Value),
        new SqlParameter("@Email", (object?)m.Email ?? DBNull.Value),
        new SqlParameter("@AzureAdObjectId", (object?)m.AzureAdObjectId ?? DBNull.Value),
        new SqlParameter("@Signature", (object?)m.Signature ?? DBNull.Value),
        new SqlParameter("@ReportingManagerId", (object?)m.ReportingManagerId ?? DBNull.Value),
        new SqlParameter("@ApplicationUserId", (object?)m.ApplicationUserId ?? DBNull.Value),
        new SqlParameter("@IsBoardMember", m.IsBoardMember),
        new SqlParameter("@IsManualEntry", m.IsManualEntry),
        new SqlParameter("@IsExternalMember", m.IsExternalMember),
        new SqlParameter("@IsExecutiveManagement", m.IsExecutiveManagement),
        new SqlParameter("@Active", m.Active),
        new SqlParameter("@InsiderTradingAccess", m.InsiderTradingAccess),
        new SqlParameter("@ConflictOfInterestAccess", m.ConflictOfInterestAccess),
        new SqlParameter("@RelatedPartyRegisterAccess", m.RelatedPartyRegisterAccess),
        new SqlParameter("@RelatedPartyTransactionAccess", m.RelatedPartyTransactionAccess),
        new SqlParameter("@RpTransactionRole", (int)m.RpTransactionRole),
        new SqlParameter("@CanBeImpersonated", m.CanBeImpersonated),
    ];
}
