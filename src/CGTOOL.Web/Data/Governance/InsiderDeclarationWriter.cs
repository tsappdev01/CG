using Microsoft.Data.SqlClient;

namespace CGTOOL.Web.Data.Governance;

public interface IInsiderDeclarationWriter
{
    Task<int> InsertAsync(InsiderDeclaration declaration);

    Task<int> InsertRelativeAsync(int insiderDeclarationId, InsiderDeclarationRelative relative);

    Task<int> InsertNinHolderAsync(int insiderDeclarationId, InsiderDeclarationNinHolder holder);

    Task UpdateAsync(InsiderDeclaration declaration);

    Task DeleteRelativesAsync(int insiderDeclarationId);

    Task DeleteNinHoldersAsync(int insiderDeclarationId);
}

/// <summary>Callers must wrap InsertAsync + InsertRelativeAsync/InsertNinHolderAsync (or
/// UpdateAsync + DeleteRelativesAsync/DeleteNinHoldersAsync + InsertRelativeAsync/InsertNinHolderAsync)
/// in a single DbContext transaction (via Database.BeginTransactionAsync) so a header row is never
/// committed without its child rows, or vice versa -- StoredProcedureExecutor joins whatever ambient
/// transaction is current.</summary>
public class InsiderDeclarationWriter(IStoredProcedureExecutor sp) : IInsiderDeclarationWriter
{
    public Task<int> InsertAsync(InsiderDeclaration declaration) => sp.InsertAsync(
        "dbo.usp_InsiderDeclaration_Insert",
        new SqlParameter("@MemberId", declaration.MemberId),
        new SqlParameter("@DeclarationCycleRunId", declaration.DeclarationCycleRunId),
        new SqlParameter("@HasNin", declaration.HasNin),
        new SqlParameter("@NinNumber", (object?)declaration.NinNumber ?? DBNull.Value),
        new SqlParameter("@HoldsShares", declaration.HoldsShares),
        new SqlParameter("@SharesNinNumber", (object?)declaration.SharesNinNumber ?? DBNull.Value),
        new SqlParameter("@NumberOfSharesHeld", (object?)declaration.NumberOfSharesHeld ?? DBNull.Value),
        new SqlParameter("@RelativesHoldShares", declaration.RelativesHoldShares),
        new SqlParameter("@RelativesHaveNin", declaration.RelativesHaveNin),
        new SqlParameter("@IsDraft", declaration.IsDraft),
        new SqlParameter("@EmiratesIdPath", (object?)declaration.EmiratesIdPath ?? DBNull.Value),
        new SqlParameter("@EmiratesIdNumber", (object?)declaration.EmiratesIdNumber ?? DBNull.Value),
        new SqlParameter("@EmiratesIdNameOnCard", (object?)declaration.EmiratesIdNameOnCard ?? DBNull.Value),
        new SqlParameter("@EmiratesIdExpiryDate", (object?)declaration.EmiratesIdExpiryDate ?? DBNull.Value),
        new SqlParameter("@PassportPath", (object?)declaration.PassportPath ?? DBNull.Value),
        new SqlParameter("@PassportNumber", (object?)declaration.PassportNumber ?? DBNull.Value),
        new SqlParameter("@PassportExpiryDate", (object?)declaration.PassportExpiryDate ?? DBNull.Value),
        new SqlParameter("@PassportIssuingCountry", (object?)declaration.PassportIssuingCountry ?? DBNull.Value),
        new SqlParameter("@TradeLicencePath", (object?)declaration.TradeLicencePath ?? DBNull.Value),
        new SqlParameter("@TradeLicenceNumber", (object?)declaration.TradeLicenceNumber ?? DBNull.Value),
        new SqlParameter("@TradeLicenceLegalName", (object?)declaration.TradeLicenceLegalName ?? DBNull.Value),
        new SqlParameter("@TradeLicenceIssuingAuthority", (object?)declaration.TradeLicenceIssuingAuthority ?? DBNull.Value),
        new SqlParameter("@TradeLicenceExpiryDate", (object?)declaration.TradeLicenceExpiryDate ?? DBNull.Value),
        new SqlParameter("@OtherDocumentPath", (object?)declaration.OtherDocumentPath ?? DBNull.Value),
        new SqlParameter("@SubmittedByName", declaration.SubmittedByName),
        new SqlParameter("@SubmittedOnBehalfOf", (object?)declaration.SubmittedOnBehalfOf ?? DBNull.Value));

    public Task<int> InsertRelativeAsync(int insiderDeclarationId, InsiderDeclarationRelative relative) => sp.InsertAsync(
        "dbo.usp_InsiderDeclarationRelative_Insert",
        new SqlParameter("@InsiderDeclarationId", insiderDeclarationId),
        new SqlParameter("@NinNumber", (object?)relative.NinNumber ?? DBNull.Value),
        new SqlParameter("@RelativeName", relative.RelativeName),
        new SqlParameter("@Relationship", (int)relative.Relationship),
        new SqlParameter("@NumberOfShares", relative.NumberOfShares),
        new SqlParameter("@Additional", (object?)relative.Additional ?? DBNull.Value),
        new SqlParameter("@IsSelf", relative.IsSelf));

    public Task<int> InsertNinHolderAsync(int insiderDeclarationId, InsiderDeclarationNinHolder holder) => sp.InsertAsync(
        "dbo.usp_InsiderDeclarationNinHolder_Insert",
        new SqlParameter("@InsiderDeclarationId", insiderDeclarationId),
        new SqlParameter("@Relationship", (int)holder.Relationship),
        new SqlParameter("@NameOfShareHolder", holder.NameOfShareHolder),
        new SqlParameter("@NinNumber", holder.NinNumber),
        new SqlParameter("@Additional", (object?)holder.Additional ?? DBNull.Value));

    public Task UpdateAsync(InsiderDeclaration declaration) => sp.ExecuteAsync(
        "dbo.usp_InsiderDeclaration_Update",
        new SqlParameter("@Id", declaration.Id),
        new SqlParameter("@HasNin", declaration.HasNin),
        new SqlParameter("@NinNumber", (object?)declaration.NinNumber ?? DBNull.Value),
        new SqlParameter("@HoldsShares", declaration.HoldsShares),
        new SqlParameter("@SharesNinNumber", (object?)declaration.SharesNinNumber ?? DBNull.Value),
        new SqlParameter("@NumberOfSharesHeld", (object?)declaration.NumberOfSharesHeld ?? DBNull.Value),
        new SqlParameter("@RelativesHoldShares", declaration.RelativesHoldShares),
        new SqlParameter("@RelativesHaveNin", declaration.RelativesHaveNin),
        new SqlParameter("@IsDraft", declaration.IsDraft),
        new SqlParameter("@EmiratesIdPath", (object?)declaration.EmiratesIdPath ?? DBNull.Value),
        new SqlParameter("@EmiratesIdNumber", (object?)declaration.EmiratesIdNumber ?? DBNull.Value),
        new SqlParameter("@EmiratesIdNameOnCard", (object?)declaration.EmiratesIdNameOnCard ?? DBNull.Value),
        new SqlParameter("@EmiratesIdExpiryDate", (object?)declaration.EmiratesIdExpiryDate ?? DBNull.Value),
        new SqlParameter("@PassportPath", (object?)declaration.PassportPath ?? DBNull.Value),
        new SqlParameter("@PassportNumber", (object?)declaration.PassportNumber ?? DBNull.Value),
        new SqlParameter("@PassportExpiryDate", (object?)declaration.PassportExpiryDate ?? DBNull.Value),
        new SqlParameter("@PassportIssuingCountry", (object?)declaration.PassportIssuingCountry ?? DBNull.Value),
        new SqlParameter("@TradeLicencePath", (object?)declaration.TradeLicencePath ?? DBNull.Value),
        new SqlParameter("@TradeLicenceNumber", (object?)declaration.TradeLicenceNumber ?? DBNull.Value),
        new SqlParameter("@TradeLicenceLegalName", (object?)declaration.TradeLicenceLegalName ?? DBNull.Value),
        new SqlParameter("@TradeLicenceIssuingAuthority", (object?)declaration.TradeLicenceIssuingAuthority ?? DBNull.Value),
        new SqlParameter("@TradeLicenceExpiryDate", (object?)declaration.TradeLicenceExpiryDate ?? DBNull.Value),
        new SqlParameter("@OtherDocumentPath", (object?)declaration.OtherDocumentPath ?? DBNull.Value),
        new SqlParameter("@SubmittedByName", declaration.SubmittedByName),
        new SqlParameter("@SubmittedOnBehalfOf", (object?)declaration.SubmittedOnBehalfOf ?? DBNull.Value));

    public Task DeleteRelativesAsync(int insiderDeclarationId) => sp.ExecuteAsync(
        "dbo.usp_InsiderDeclarationRelative_DeleteByDeclaration",
        new SqlParameter("@InsiderDeclarationId", insiderDeclarationId));

    public Task DeleteNinHoldersAsync(int insiderDeclarationId) => sp.ExecuteAsync(
        "dbo.usp_InsiderDeclarationNinHolder_DeleteByDeclaration",
        new SqlParameter("@InsiderDeclarationId", insiderDeclarationId));
}
