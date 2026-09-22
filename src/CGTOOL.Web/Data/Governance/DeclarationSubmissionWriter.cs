using Microsoft.Data.SqlClient;

namespace CGTOOL.Web.Data.Governance;

public interface IDeclarationSubmissionWriter
{
    Task<int> InsertAsync(int memberId, int declarationSetupId);
}

public class DeclarationSubmissionWriter(IStoredProcedureExecutor sp) : IDeclarationSubmissionWriter
{
    public Task<int> InsertAsync(int memberId, int declarationSetupId) => sp.InsertAsync(
        "dbo.usp_DeclarationSubmission_Insert",
        new SqlParameter("@MemberId", memberId), new SqlParameter("@DeclarationSetupId", declarationSetupId));
}
