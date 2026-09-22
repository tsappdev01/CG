using Microsoft.Data.SqlClient;

namespace CGTOOL.Web.Data.Governance;

public interface IPolicyDocumentVersionWriter
{
    Task<int> InsertAsync(PolicyDocumentVersion version);
}

public class PolicyDocumentVersionWriter(IStoredProcedureExecutor sp) : IPolicyDocumentVersionWriter
{
    public Task<int> InsertAsync(PolicyDocumentVersion version) => sp.InsertAsync(
        "dbo.usp_PolicyDocumentVersion_Insert",
        new SqlParameter("@FilePath", version.FilePath),
        new SqlParameter("@OriginalFileName", version.OriginalFileName),
        new SqlParameter("@UploadedByName", version.UploadedByName));
}
