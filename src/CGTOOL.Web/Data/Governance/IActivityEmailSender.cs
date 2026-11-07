namespace CGTOOL.Web.Data.Governance;

public interface IActivityEmailSender
{
    /// <param name="ccEmails">Semicolon-separated, or null. Database Mail's own separator, so it
    /// goes straight to @copy_recipients.</param>
    Task SendAsync(string toEmail, string subject, string body, CancellationToken ct = default, string? ccEmails = null);
}

/// <summary>No SMTP server is configured for this app yet; logs instead of sending so the schedule logic is still observable/testable.</summary>
public class LoggingActivityEmailSender(ILogger<LoggingActivityEmailSender> logger) : IActivityEmailSender
{
    public Task SendAsync(string toEmail, string subject, string body, CancellationToken ct = default, string? ccEmails = null)
    {
        logger.LogInformation("Scheduled activity email to {ToEmail} (cc {Cc}): {Subject}", toEmail, ccEmails ?? "none", subject);
        return Task.CompletedTask;
    }
}
