using Microsoft.Data.SqlClient;

namespace CGTOOL.Web.Data.Governance;

public interface IMemberImpersonationApprovalWriter
{
    Task GrantAsync(int memberId, int impersonatorId);
    Task RevokeAsync(int memberId, int impersonatorId);
}

public class MemberImpersonationApprovalWriter(IStoredProcedureExecutor sp) : IMemberImpersonationApprovalWriter
{
    public Task GrantAsync(int memberId, int impersonatorId) => sp.ExecuteAsync(
        "dbo.usp_MemberImpersonationApproval_Grant",
        new SqlParameter("@MemberId", memberId), new SqlParameter("@ImpersonatorId", impersonatorId));

    public Task RevokeAsync(int memberId, int impersonatorId) => sp.ExecuteAsync(
        "dbo.usp_MemberImpersonationApproval_Revoke",
        new SqlParameter("@MemberId", memberId), new SqlParameter("@ImpersonatorId", impersonatorId));
}
