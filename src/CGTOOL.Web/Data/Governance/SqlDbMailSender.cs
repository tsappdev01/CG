using Microsoft.Data.SqlClient;

namespace CGTOOL.Web.Data.Governance;

/// <summary>Sends mail via SQL Server Database Mail (msdb.dbo.sp_send_dbmail) using the same
/// connection the current DbContext holds, instead of an external SMTP relay. Requires Database Mail
/// to already be enabled on the server with a profile matching Smtp:SqlDbMailProfile, and the app's
/// SQL login to have execute rights on msdb.dbo.sp_send_dbmail (DatabaseMailUserRole in msdb) --
/// neither of which can be verified from this sandbox.</summary>
public class SqlDbMailSender(IStoredProcedureExecutor sp, IConfiguration config)
{
    private string ProfileName => config["Smtp:SqlDbMailProfile"] ?? string.Empty;

    // sp_send_dbmail's @file_attachments needs a path the SQL Server ENGINE (not this app) can read --
    // typically a UNC share both the app host and the SQL Server host can reach. Left blank, callers
    // that want a real file attached (rather than the query-result-as-CSV mechanism below) should treat
    // AttachmentFolderConfigured as false and send without one.
    private string AttachmentFolder => config["Smtp:DatabaseMailAttachmentFolder"] ?? string.Empty;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(ProfileName);

    public bool AttachmentFolderConfigured => !string.IsNullOrWhiteSpace(AttachmentFolder);

    public Task SendAsync(string toEmail, string subject, string htmlBody, CancellationToken ct = default) => sp.ExecuteAsync(
        "msdb.dbo.sp_send_dbmail",
        new SqlParameter("@profile_name", ProfileName),
        new SqlParameter("@recipients", toEmail),
        new SqlParameter("@subject", subject),
        new SqlParameter("@body", htmlBody),
        new SqlParameter("@body_format", "HTML"));

    /// <summary>Writes <paramref name="fileBytes"/> to the configured shared attachment folder and
    /// asks Database Mail to attach that file by path via @file_attachments. Only call this when
    /// AttachmentFolderConfigured is true. The file is left in place afterwards (Database Mail sends
    /// asynchronously via its own queue, so deleting it right after this call returns could race a send
    /// that hasn't happened yet) -- an operator should schedule periodic cleanup of old files in that
    /// folder (e.g. a SQL Agent job or Windows scheduled task), documented in README.md.</summary>
    public async Task SendWithFileAttachmentAsync(string toEmail, string subject, string htmlBody, byte[] fileBytes, string attachmentFilename, CancellationToken ct = default)
    {
        var uniqueName = $"{Guid.NewGuid():N}_{attachmentFilename}";
        var fullPath = Path.Combine(AttachmentFolder, uniqueName);
        await File.WriteAllBytesAsync(fullPath, fileBytes, ct);

        await sp.ExecuteAsync(
            "msdb.dbo.sp_send_dbmail",
            new SqlParameter("@profile_name", ProfileName),
            new SqlParameter("@recipients", toEmail),
            new SqlParameter("@subject", subject),
            new SqlParameter("@body", htmlBody),
            new SqlParameter("@body_format", "HTML"),
            new SqlParameter("@file_attachments", fullPath));
    }

}

public class SqlDbMailActivityEmailSender(SqlDbMailSender mail) : IActivityEmailSender
{
    public Task SendAsync(string toEmail, string subject, string body, CancellationToken ct = default) =>
        mail.SendAsync(toEmail, subject, body, ct);
}

public class SqlDbMailMemberWelcomeEmailSender(SqlDbMailSender mail) : IMemberWelcomeEmailSender
{
    public async Task<bool> SendWelcomeEmailAsync(Member member, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(member.Email)) return false;

        const string logoTag = "<img src=\"https://cg.dubaiinvestments.com/images/di-logo.jpg\" alt=\"Dubai Investments\" height=\"48\" style=\"display:block;\" />";
        var html = WelcomeEmailTemplate.BuildHtml(member, logoTag);
        await mail.SendAsync(member.Email, WelcomeEmailTemplate.Subject, html, ct);
        return true;
    }
}
