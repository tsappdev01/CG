using Microsoft.Data.SqlClient;

namespace CGTOOL.Web.Data.Governance;

public interface IDocumentTypeWriter
{
    Task<int> InsertAsync(DocumentType type);
    Task UpdateAsync(DocumentType type);

    /// <summary>Sets what one declaration asks of one document, inserting the row or changing the
    /// one that is there.</summary>
    Task SetRequirementAsync(int documentTypeId, DeclarationType declaration, DocumentRequirement requirement);

    /// <summary>Removes a type outright. The procedure refuses when anything has been filed
    /// against it, so a type in use can only be switched off -- see DocumentType.IsActive.</summary>
    Task DeleteAsync(int id);
}

public class DocumentTypeWriter(IStoredProcedureExecutor sp) : IDocumentTypeWriter
{
    public Task<int> InsertAsync(DocumentType type) => sp.InsertAsync(
        "dbo.usp_DocumentType_Insert",
        new SqlParameter("@Name", type.Name),
        new SqlParameter("@Description", (object?)type.Description ?? DBNull.Value),
        new SqlParameter("@IsActive", type.IsActive),
        new SqlParameter("@SortOrder", type.SortOrder));

    /// <summary>BuiltInKind is deliberately not a parameter: it is set when the row is seeded and
    /// never afterwards, because the code behind a built-in document is specific to it.</summary>
    public Task UpdateAsync(DocumentType type) => sp.ExecuteAsync(
        "dbo.usp_DocumentType_Update",
        new SqlParameter("@Id", type.Id),
        new SqlParameter("@Name", type.Name),
        new SqlParameter("@Description", (object?)type.Description ?? DBNull.Value),
        new SqlParameter("@IsActive", type.IsActive),
        new SqlParameter("@SortOrder", type.SortOrder));

    public Task SetRequirementAsync(int documentTypeId, DeclarationType declaration, DocumentRequirement requirement) =>
        sp.ExecuteAsync("dbo.usp_DocumentTypeRequirement_Set",
            new SqlParameter("@DocumentTypeId", documentTypeId),
            new SqlParameter("@Declaration", (int)declaration),
            new SqlParameter("@Requirement", (int)requirement));

    public Task DeleteAsync(int id) => sp.ExecuteAsync(
        "dbo.usp_DocumentType_Delete", new SqlParameter("@Id", id));
}
