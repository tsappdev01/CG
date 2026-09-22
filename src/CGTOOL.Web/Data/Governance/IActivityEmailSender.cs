namespace CGTOOL.Web.Data.Governance;

public interface IActivityEmailSender
{
    Task SendAsync(string toEmail, string subject, string body, CancellationToken ct = default);
}

/// <summary>No SMTP server is configured for this app yet; logs instead of sending so the schedule logic is still observable/testable.</summary>
public class LoggingActivityEmailSender(ILogger<LoggingActivityEmailSender> logger) : IActivityEmailSender
{
    public Task SendAsync(string toEmail, string subject, string body, CancellationToken ct = default)
    {
        logger.LogInformation("Scheduled activity email to {ToEmail}: {Subject}", toEmail, subject);
        return Task.CompletedTask;
    }
}
