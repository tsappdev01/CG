using Microsoft.Data.SqlClient;

namespace CGTOOL.Web.Data.Governance;

public interface IMemberDocumentWriter
{
    Task<int> InsertAsync(MemberDocument document);
    Task UpdateAsync(MemberDocument document);
    Task DeleteAsync(int id);
}

public class MemberDocumentWriter(IStoredProcedureExecutor sp) : IMemberDocumentWriter
{
    public Task<int> InsertAsync(MemberDocument document) => sp.InsertAsync("dbo.usp_MemberDocument_Insert",
        new SqlParameter("@MemberId", document.MemberId),
        new SqlParameter("@Kind", (int)document.Kind),
        new SqlParameter("@Title", (object?)document.Title ?? DBNull.Value),
        new SqlParameter("@FilePath", (object?)document.FilePath ?? DBNull.Value),
        new SqlParameter("@OriginalFileName", (object?)document.OriginalFileName ?? DBNull.Value),
        new SqlParameter("@DocumentNumber", (object?)document.DocumentNumber ?? DBNull.Value),
        new SqlParameter("@HolderName", (object?)document.HolderName ?? DBNull.Value),
        new SqlParameter("@Nationality", (object?)document.Nationality ?? DBNull.Value),
        new SqlParameter("@ExpiryDate", (object?)document.ExpiryDate ?? DBNull.Value),
        new SqlParameter("@IssueDate", (object?)document.IssueDate ?? DBNull.Value),
        new SqlParameter("@DateOfBirth", (object?)document.DateOfBirth ?? DBNull.Value),
        new SqlParameter("@UploadedAtUtc", document.UploadedAtUtc));

    public Task UpdateAsync(MemberDocument document) => sp.ExecuteAsync("dbo.usp_MemberDocument_Update",
        new SqlParameter("@Id", document.Id),
        new SqlParameter("@Title", (object?)document.Title ?? DBNull.Value),
        new SqlParameter("@FilePath", (object?)document.FilePath ?? DBNull.Value),
        new SqlParameter("@OriginalFileName", (object?)document.OriginalFileName ?? DBNull.Value),
        new SqlParameter("@DocumentNumber", (object?)document.DocumentNumber ?? DBNull.Value),
        new SqlParameter("@HolderName", (object?)document.HolderName ?? DBNull.Value),
        new SqlParameter("@Nationality", (object?)document.Nationality ?? DBNull.Value),
        new SqlParameter("@ExpiryDate", (object?)document.ExpiryDate ?? DBNull.Value),
        new SqlParameter("@IssueDate", (object?)document.IssueDate ?? DBNull.Value),
        new SqlParameter("@DateOfBirth", (object?)document.DateOfBirth ?? DBNull.Value),
        new SqlParameter("@UploadedAtUtc", document.UploadedAtUtc));

    public Task DeleteAsync(int id) => sp.ExecuteAsync("dbo.usp_MemberDocument_Delete", new SqlParameter("@Id", id));
}
