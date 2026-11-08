using Microsoft.Data.SqlClient;

namespace CGTOOL.Web.Data.Governance;

public interface IMemberOnboardingWriter
{
    Task<int> InsertAsync(MemberOnboarding onboarding);
    Task UpdateAsync(MemberOnboarding onboarding);
}

public class MemberOnboardingWriter(IStoredProcedureExecutor sp) : IMemberOnboardingWriter
{
    public Task<int> InsertAsync(MemberOnboarding o) =>
        sp.InsertAsync("dbo.usp_MemberOnboarding_Insert", [.. Parameters(o), new SqlParameter("@MemberId", o.MemberId)]);

    public Task UpdateAsync(MemberOnboarding o) =>
        sp.ExecuteAsync("dbo.usp_MemberOnboarding_Update", [.. Parameters(o), new SqlParameter("@Id", o.Id)]);

    private static SqlParameter[] Parameters(MemberOnboarding o) =>
    [
        new("@Status", (int)o.Status),
        new("@StartedAtUtc", (object?)o.StartedAtUtc ?? DBNull.Value),
        new("@CompletedAtUtc", (object?)o.CompletedAtUtc ?? DBNull.Value),

        new("@EmiratesIdStatus", (int)o.EmiratesIdStatus),
        new("@EmiratesIdSource", (int)o.EmiratesIdSource),
        new("@EmiratesIdNumber", (object?)o.EmiratesIdNumber ?? DBNull.Value),
        new("@EmiratesIdHolderName", (object?)o.EmiratesIdHolderName ?? DBNull.Value),
        new("@EmiratesIdHolderNameArabic", (object?)o.EmiratesIdHolderNameArabic ?? DBNull.Value),
        new("@EmiratesIdNationality", (object?)o.EmiratesIdNationality ?? DBNull.Value),
        new("@EmiratesIdExpiryDate", (object?)o.EmiratesIdExpiryDate ?? DBNull.Value),
        new("@EmiratesIdConfirmedAtUtc", (object?)o.EmiratesIdConfirmedAtUtc ?? DBNull.Value),
        new("@EmiratesIdReportedIssue", (object?)o.EmiratesIdReportedIssue ?? DBNull.Value),
        new("@EmiratesIdReportedAtUtc", (object?)o.EmiratesIdReportedAtUtc ?? DBNull.Value),
        new("@EmiratesIdBlobId", (object?)o.EmiratesIdBlobId ?? DBNull.Value),
        new("@EmiratesIdVerifiedAtUtc", (object?)o.EmiratesIdVerifiedAtUtc ?? DBNull.Value),
        new("@EmiratesIdVerifiedByName", (object?)o.EmiratesIdVerifiedByName ?? DBNull.Value),

        new("@DfmStatus", (int)o.DfmStatus),
        new("@DfmSource", (int)o.DfmSource),
        new("@NinResponse", (int)o.NinResponse),
        new("@NinNumber", (object?)o.NinNumber ?? DBNull.Value),
        new("@NinRegisteredName", (object?)o.NinRegisteredName ?? DBNull.Value),
        new("@DfmOtherInformation", (object?)o.DfmOtherInformation ?? DBNull.Value),
        new("@DfmConfirmedAtUtc", (object?)o.DfmConfirmedAtUtc ?? DBNull.Value),
        new("@DfmReportedIssue", (object?)o.DfmReportedIssue ?? DBNull.Value),
        new("@DfmReportedAtUtc", (object?)o.DfmReportedAtUtc ?? DBNull.Value),
        new("@NinDocumentBlobId", (object?)o.NinDocumentBlobId ?? DBNull.Value),
        new("@DfmVerifiedAtUtc", (object?)o.DfmVerifiedAtUtc ?? DBNull.Value),
        new("@DfmVerifiedByName", (object?)o.DfmVerifiedByName ?? DBNull.Value),
    ];
}
