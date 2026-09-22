using Microsoft.EntityFrameworkCore;

namespace CGTOOL.Web.Data.Governance;

/// <summary>Fires scheduled ("Schedule Send") notification runs once their target time arrives, and
/// sends the remaining reminder emails for each already-sent DeclarationCycleRun, at the cadence
/// configured on its DeclarationCycleSetup (Daily/Weekly), until ReminderCount is reached.</summary>
public class DeclarationCycleReminderHostedService(IServiceScopeFactory scopeFactory, ILogger<DeclarationCycleReminderHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var emailSender = scope.ServiceProvider.GetRequiredService<IActivityEmailSender>();
                var runWriter = scope.ServiceProvider.GetRequiredService<IDeclarationCycleRunWriter>();
                var auditLog = scope.ServiceProvider.GetRequiredService<IAuditLogger>();
                await ProcessDueScheduledSendsAsync(db, emailSender, runWriter, auditLog, stoppingToken);
                await RunOnceAsync(db, emailSender, runWriter, auditLog, stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Declaration cycle check failed.");
            }

            // Hourly (not daily) so a "Schedule Send" targeting 08:00 fires reasonably close to that time.
            await Task.Delay(TimeSpan.FromHours(1), stoppingToken);
        }
    }

    /// <summary>Fires any scheduled-but-not-yet-sent run (Sent = false) whose target SentAtUtc has arrived.</summary>
    public static async Task ProcessDueScheduledSendsAsync(ApplicationDbContext db, IActivityEmailSender emailSender, IDeclarationCycleRunWriter runWriter, IAuditLogger auditLog, CancellationToken ct = default)
    {
        var dueRuns = await db.DeclarationCycleRuns
            .Include(r => r.DeclarationCycleSetup)
            .Where(r => !r.Sent && r.SentAtUtc <= DateTime.UtcNow)
            .ToListAsync(ct);

        foreach (var run in dueRuns)
        {
            if (run.DeclarationCycleSetup is null) continue;

            var (sentCount, failedCount) = await SendNotificationAsync(db, emailSender, runWriter, auditLog, run, run.DeclarationCycleSetup, "System", ct);
            await runWriter.MarkSentAsync(run.Id, DateTime.UtcNow, sentCount + failedCount);
        }
    }

    public static async Task RunOnceAsync(ApplicationDbContext db, IActivityEmailSender emailSender, IDeclarationCycleRunWriter runWriter, IAuditLogger auditLog, CancellationToken ct = default)
    {
        var runs = await db.DeclarationCycleRuns
            .Include(r => r.DeclarationCycleSetup)
            .Where(r => r.Sent && !r.Recalled && r.DeclarationCycleSetup != null)
            .ToListAsync(ct);

        foreach (var run in runs)
        {
            var setup = run.DeclarationCycleSetup!;
            if (run.RemindersSent >= setup.ReminderCount) continue;

            var intervalDays = setup.ReminderFrequency == CycleReminderFrequency.Daily ? 1 : 7;
            var lastSent = run.LastReminderSentUtc ?? run.SentAtUtc;
            if ((DateTime.UtcNow - lastSent).TotalDays < intervalDays) continue;

            var recipients = await ResolveRecipientsAsync(db, run.CompanyId, ct);
            var reminderNumber = run.RemindersSent + 1;
            foreach (var (memberId, email, fullName, _) in recipients)
            {
                var body = ApplyPlaceholders(setup.EmailBody, fullName, run.DueDateUtc);
                try
                {
                    await emailSender.SendAsync(email, $"Reminder: {setup.EmailSubject}", body, ct);
                    await auditLog.LogAsync("System", AuditAction.Notify, nameof(Member), memberId.ToString(),
                        $"Reminder {reminderNumber}/{setup.ReminderCount} for {setup.Type} sent to {fullName} <{email}>, due {run.DueDateUtc.ToLocalDisplay():yyyy-MM-dd}");
                }
                catch (Exception ex)
                {
                    await auditLog.LogAsync("System", AuditAction.Notify, nameof(Member), memberId.ToString(),
                        $"Reminder {reminderNumber}/{setup.ReminderCount} for {setup.Type} to {fullName} <{email}> FAILED: {ex.Message}");
                }
            }

            var sentAt = DateTime.UtcNow;
            await runWriter.IncrementReminderAsync(run.Id, sentAt);
            run.RemindersSent++;
            run.LastReminderSentUtc = sentAt;
        }
    }

    /// <summary>Shared by "Send Now"/schedule-firing: resolves recipients, sends the notification, and
    /// records a per-recipient row (name/email/entity/sent-on) plus an audit-log entry for each.</summary>
    public static async Task<(int SentCount, int FailedCount)> SendNotificationAsync(ApplicationDbContext db, IActivityEmailSender emailSender, IDeclarationCycleRunWriter runWriter, IAuditLogger auditLog, DeclarationCycleRun run, DeclarationCycleSetup setup, string actorName, CancellationToken ct = default)
    {
        var recipients = await ResolveRecipientsAsync(db, run.CompanyId, ct);
        var sentCount = 0;
        var failedCount = 0;

        foreach (var (memberId, email, fullName, companyName) in recipients)
        {
            var body = ApplyPlaceholders(setup.EmailBody, fullName, run.DueDateUtc);
            var success = true;
            try
            {
                await emailSender.SendAsync(email, setup.EmailSubject, body, ct);
                sentCount++;
                await auditLog.LogAsync(actorName, AuditAction.Notify, nameof(Member), memberId.ToString(),
                    $"{setup.Type} notification sent to {fullName} <{email}>, due {run.DueDateUtc.ToLocalDisplay():yyyy-MM-dd}");
            }
            catch (Exception ex)
            {
                success = false;
                failedCount++;
                await auditLog.LogAsync(actorName, AuditAction.Notify, nameof(Member), memberId.ToString(),
                    $"{setup.Type} notification to {fullName} <{email}> FAILED: {ex.Message}");
            }

            await runWriter.InsertRecipientAsync(new DeclarationCycleRunRecipient
            {
                DeclarationCycleRunId = run.Id,
                MemberId = memberId,
                MemberName = fullName,
                Email = email,
                CompanyName = companyName,
                Success = success,
            });
        }

        return (sentCount, failedCount);
    }

    /// <summary>Emails every original recipient of an already-Sent run (from its own recorded
    /// DeclarationCycleRunRecipient rows, not a fresh active-Member lookup -- the recall notice should
    /// reach exactly who got the original notification) to say it's been withdrawn, then marks the run
    /// recalled. Callers are responsible for only invoking this while the run has zero submissions.</summary>
    public static async Task RecallAsync(IActivityEmailSender emailSender, IDeclarationCycleRunWriter runWriter, IAuditLogger auditLog, DeclarationCycleRun run, DeclarationCycleType type, string actorName, CancellationToken ct = default)
    {
        var periodLabel = $"Q{run.PeriodQuarter} {run.PeriodYear}";
        var subject = $"Recalled: {TypeLabel(type)} Declaration Notification — {periodLabel}";

        foreach (var recipient in run.Recipients)
        {
            var body = $"""
                <p>Dear {recipient.MemberName},</p>
                <p>The {TypeLabel(type)} declaration notification for <b>{periodLabel}</b> (previously due
                {run.DueDateUtc.ToLocalDisplay("dd/MM/yyyy")}) that was sent to you on
                {run.SentAtUtc.ToLocalDisplay("dd/MM/yyyy")} has been <b>recalled</b> by the Corporate Governance team.</p>
                <p>No submission is required for this notice. You will be notified separately if this declaration is re-issued.</p>
                <p>Should you have any queries, please contact the Corporate Affairs Office.</p>
                """;

            try
            {
                await emailSender.SendAsync(recipient.Email, subject, body, ct);
                await auditLog.LogAsync(actorName, AuditAction.Notify, nameof(Member), recipient.MemberId.ToString(),
                    $"{TypeLabel(type)} recall notice sent to {recipient.MemberName} <{recipient.Email}> for {periodLabel}.");
            }
            catch (Exception ex)
            {
                await auditLog.LogAsync(actorName, AuditAction.Notify, nameof(Member), recipient.MemberId.ToString(),
                    $"{TypeLabel(type)} recall notice to {recipient.MemberName} <{recipient.Email}> for {periodLabel} FAILED: {ex.Message}");
            }
        }

        var recalledAtUtc = DateTime.UtcNow;
        await runWriter.RecallAsync(run.Id, recalledAtUtc, actorName);
        await auditLog.LogAsync(actorName, AuditAction.Recall, nameof(DeclarationCycleRun), run.Id.ToString(),
            $"{TypeLabel(type)} notification for {periodLabel} (sent {run.SentAtUtc.ToLocalDisplay("dd/MM/yyyy")}) recalled; {run.Recipients.Count} recipient(s) notified.");
    }

    private static string TypeLabel(DeclarationCycleType type) => type switch
    {
        DeclarationCycleType.InsiderTrading => "Insider Trading",
        DeclarationCycleType.ConflictOfInterest => "Related Party & Conflict of Interest",
        DeclarationCycleType.RelatedPartyRegister => "Related Party Register",
        DeclarationCycleType.BlackoutPeriod => "Blackout Period",
        DeclarationCycleType.ScheduledMaintenance => "Scheduled Maintenance",
        _ => type.ToString(),
    };

    public static async Task<List<(int MemberId, string Email, string FullName, string? CompanyName)>> ResolveRecipientsAsync(ApplicationDbContext db, int? companyId, CancellationToken ct)
    {
        var query = db.Members.Include(m => m.Company).Where(m => m.Active && m.Email != null);
        if (companyId is { } id) query = query.Where(m => m.CompanyId == id);
        return await query.Select(m => new ValueTuple<int, string, string, string?>(m.Id, m.Email!, m.FullName, m.Company!.Name)).ToListAsync(ct);
    }

    public static string ApplyPlaceholders(string template, string fullName, DateTime dueDateUtc) =>
        template.Replace("{FullName}", fullName).Replace("{DueDate}", dueDateUtc.ToLocalDisplay("dd/MM/yyyy"));
}
