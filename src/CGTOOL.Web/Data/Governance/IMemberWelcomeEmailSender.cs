namespace CGTOOL.Web.Data.Governance;

public interface IMemberWelcomeEmailSender
{
    /// <summary>Sends the FRD §2.3 new-user notification email. Returns true if it was actually sent,
    /// false if it was skipped (no email on file, or SMTP not configured) — callers use this to record
    /// the MemberNotification's delivery status (FRD §2.2).</summary>
    Task<bool> SendWelcomeEmailAsync(Member member, CancellationToken ct = default);
}

/// <summary>No SMTP server is configured for this app yet; logs instead of sending.</summary>
public class NoOpMemberWelcomeEmailSender(ILogger<NoOpMemberWelcomeEmailSender> logger) : IMemberWelcomeEmailSender
{
    public Task<bool> SendWelcomeEmailAsync(Member member, CancellationToken ct = default)
    {
        logger.LogInformation("Welcome/declaration email to {Email} was not sent — SMTP is not configured.", member.Email);
        return Task.FromResult(false);
    }
}
