using Microsoft.Data.SqlClient;

namespace CGTOOL.Web.Data.Governance;

public interface IRelatedPartyCoiDeclarationWriter
{
    Task<int> InsertAsync(RelatedPartyCoiDeclaration declaration);

    Task UpdateAsync(RelatedPartyCoiDeclaration declaration);

    Task SetUaePassVerificationAsync(int declarationId, DateTime verifiedAtUtc, string verifiedName, string uuid);

    Task<int> InsertRelativeAsync(int declarationId, CoiRelative relative);

    Task DeleteRelativesAsync(int declarationId);

    Task<int> InsertCompanyAsync(int declarationId, CoiCompanyEntry company);

    Task DeleteCompaniesAsync(int declarationId);

    Task<int> InsertDocumentAsync(int companyEntryId, CoiTradeLicenseDocument document);

    Task<int> InsertConflictAsync(int declarationId, CoiConflictEntry conflict);

    Task DeleteConflictsAsync(int declarationId);
}

/// <summary>Callers must wrap InsertAsync/UpdateAsync + the child insert/delete calls in a single
/// DbContext transaction (via Database.BeginTransactionAsync) -- same pattern as
/// InsiderDeclarationWriter. Company rows must be (re)inserted, with their returned IDs used for
/// InsertDocumentAsync, before relative rows that I.C entries reference via CoiRelativeId -- callers
/// should insert relatives first, then companies (passing the fresh CoiRelative IDs).</summary>
public class RelatedPartyCoiDeclarationWriter(IStoredProcedureExecutor sp) : IRelatedPartyCoiDeclarationWriter
{
    public Task<int> InsertAsync(RelatedPartyCoiDeclaration declaration) => sp.InsertAsync(
        "dbo.usp_RelatedPartyCoiDeclaration_Insert",
        new SqlParameter("@MemberId", declaration.MemberId),
        new SqlParameter("@DeclarationCycleRunId", declaration.DeclarationCycleRunId),
        new SqlParameter("@NothingToDeclareRelatives", declaration.NothingToDeclareRelatives),
        new SqlParameter("@NothingToDeclareSelfOwned", declaration.NothingToDeclareSelfOwned),
        new SqlParameter("@NothingToDeclareRelativeOwned", declaration.NothingToDeclareRelativeOwned),
        new SqlParameter("@NothingToDeclareBoardRoles", declaration.NothingToDeclareBoardRoles),
        new SqlParameter("@NothingToDeclareConflicts", declaration.NothingToDeclareConflicts),
        new SqlParameter("@IsDraft", declaration.IsDraft),
        new SqlParameter("@AttestationName", declaration.AttestationName),
        new SqlParameter("@AttestationConfirmed", declaration.AttestationConfirmed),
        new SqlParameter("@SubmittedByName", declaration.SubmittedByName),
        new SqlParameter("@SubmittedOnBehalfOf", (object?)declaration.SubmittedOnBehalfOf ?? DBNull.Value));

    public Task UpdateAsync(RelatedPartyCoiDeclaration declaration) => sp.ExecuteAsync(
        "dbo.usp_RelatedPartyCoiDeclaration_Update",
        new SqlParameter("@Id", declaration.Id),
        new SqlParameter("@NothingToDeclareRelatives", declaration.NothingToDeclareRelatives),
        new SqlParameter("@NothingToDeclareSelfOwned", declaration.NothingToDeclareSelfOwned),
        new SqlParameter("@NothingToDeclareRelativeOwned", declaration.NothingToDeclareRelativeOwned),
        new SqlParameter("@NothingToDeclareBoardRoles", declaration.NothingToDeclareBoardRoles),
        new SqlParameter("@NothingToDeclareConflicts", declaration.NothingToDeclareConflicts),
        new SqlParameter("@IsDraft", declaration.IsDraft),
        new SqlParameter("@AttestationName", declaration.AttestationName),
        new SqlParameter("@AttestationConfirmed", declaration.AttestationConfirmed),
        new SqlParameter("@SubmittedByName", declaration.SubmittedByName),
        new SqlParameter("@SubmittedOnBehalfOf", (object?)declaration.SubmittedOnBehalfOf ?? DBNull.Value));

    public Task SetUaePassVerificationAsync(int declarationId, DateTime verifiedAtUtc, string verifiedName, string uuid) => sp.ExecuteAsync(
        "dbo.usp_RelatedPartyCoiDeclaration_SetUaePassVerification",
        new SqlParameter("@Id", declarationId),
        new SqlParameter("@UaePassVerifiedAtUtc", verifiedAtUtc),
        new SqlParameter("@UaePassVerifiedName", verifiedName),
        new SqlParameter("@UaePassUuid", uuid));

    public Task<int> InsertRelativeAsync(int declarationId, CoiRelative relative) => sp.InsertAsync(
        "dbo.usp_CoiRelative_Insert",
        new SqlParameter("@RelatedPartyCoiDeclarationId", declarationId),
        new SqlParameter("@Name", relative.Name),
        new SqlParameter("@Relationship", (int)relative.Relationship));

    public Task DeleteRelativesAsync(int declarationId) => sp.ExecuteAsync(
        "dbo.usp_CoiRelative_DeleteByDeclaration",
        new SqlParameter("@RelatedPartyCoiDeclarationId", declarationId));

    public Task<int> InsertCompanyAsync(int declarationId, CoiCompanyEntry company) => sp.InsertAsync(
        "dbo.usp_CoiCompanyEntry_Insert",
        new SqlParameter("@RelatedPartyCoiDeclarationId", declarationId),
        new SqlParameter("@OwnerType", (int)company.OwnerType),
        new SqlParameter("@CoiRelativeId", (object?)company.CoiRelativeId ?? DBNull.Value),
        new SqlParameter("@LegalCompanyName", company.LegalCompanyName),
        new SqlParameter("@PrincipalBusinessActivity", (object?)company.PrincipalBusinessActivity ?? DBNull.Value),
        new SqlParameter("@TradeLicenseNumber", (object?)company.TradeLicenseNumber ?? DBNull.Value),
        new SqlParameter("@TradeLicenseExpiryDate", (object?)company.TradeLicenseExpiryDate ?? DBNull.Value),
        new SqlParameter("@LicenseActivities", (object?)company.LicenseActivities ?? DBNull.Value));

    public Task DeleteCompaniesAsync(int declarationId) => sp.ExecuteAsync(
        "dbo.usp_CoiCompanyEntry_DeleteByDeclaration",
        new SqlParameter("@RelatedPartyCoiDeclarationId", declarationId));

    public Task<int> InsertDocumentAsync(int companyEntryId, CoiTradeLicenseDocument document) => sp.InsertAsync(
        "dbo.usp_CoiTradeLicenseDocument_Insert",
        new SqlParameter("@CoiCompanyEntryId", companyEntryId),
        new SqlParameter("@FilePath", document.FilePath),
        new SqlParameter("@FileName", document.FileName));

    public Task<int> InsertConflictAsync(int declarationId, CoiConflictEntry conflict) => sp.InsertAsync(
        "dbo.usp_CoiConflictEntry_Insert",
        new SqlParameter("@RelatedPartyCoiDeclarationId", declarationId),
        new SqlParameter("@CompanyOrCounterpartyName", conflict.CompanyOrCounterpartyName),
        new SqlParameter("@PrincipalBusinessActivity", (object?)conflict.PrincipalBusinessActivity ?? DBNull.Value),
        new SqlParameter("@NatureOfHolding", (object?)conflict.NatureOfHolding ?? DBNull.Value));

    public Task DeleteConflictsAsync(int declarationId) => sp.ExecuteAsync(
        "dbo.usp_CoiConflictEntry_DeleteByDeclaration",
        new SqlParameter("@RelatedPartyCoiDeclarationId", declarationId));
}
