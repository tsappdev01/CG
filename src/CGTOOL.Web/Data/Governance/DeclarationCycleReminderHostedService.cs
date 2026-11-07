using Microsoft.EntityFrameworkCore;

namespace CGTOOL.Web.Data.Governance;

/// <summary>Fires scheduled ("Schedule Send") notification runs once their target time arrives, and
/// sends the remaining reminder emails for each already-sent DeclarationCycleRun on the weekday and
/// time configured on its DeclarationCycleSetup -- read in UAE Standard Time -- until ReminderCount
/// is reached.</summary>
public class DeclarationCycleReminderHostedService(IServiceScopeFactory scopeFactory, ILogger<DeclarationCycleReminderHostedService> logger, IConfiguration config) : BackgroundService
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
                await ProcessDueScheduledSendsAsync(db, emailSender, runWriter, auditLog, stoppingToken, BaseUrl(config));
                await RunOnceAsync(db, emailSender, runWriter, auditLog, stoppingToken, BaseUrl(config));
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Declaration cycle check failed.");
            }

            // Both a scheduled send and a weekly reminder name a time of day, so the tick has to be
            // short enough that "08:00" means 08:00 and not "some time before 09:00".
            try
            {
                await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                // Shutting down. Task.Delay throws when its token is cancelled, which is the normal
                // way this loop ends -- letting it escape reports a stopping application as a
                // crashed background service, and breaks the debugger on every F5 stop.
                break;
            }
        }
    }

    /// <summary>Fires any scheduled-but-not-yet-sent run (Sent = false) whose target SentAtUtc has arrived.</summary>
    public static async Task ProcessDueScheduledSendsAsync(ApplicationDbContext db, IActivityEmailSender emailSender, IDeclarationCycleRunWriter runWriter, IAuditLogger auditLog, CancellationToken ct = default, string? baseUrl = null)
    {
        var dueRuns = await db.DeclarationCycleRuns
            .Include(r => r.DeclarationCycleSetup)
            .Where(r => !r.Sent && r.SentAtUtc <= DateTime.UtcNow)
            .ToListAsync(ct);

        foreach (var run in dueRuns)
        {
            if (run.DeclarationCycleSetup is null) continue;

            var (sentCount, failedCount) = await SendNotificationAsync(db, emailSender, runWriter, auditLog, run, run.DeclarationCycleSetup, "System", ct, baseUrl);
            await runWriter.MarkSentAsync(run.Id, DateTime.UtcNow, sentCount + failedCount);
        }
    }

    public static async Task RunOnceAsync(ApplicationDbContext db, IActivityEmailSender emailSender, IDeclarationCycleRunWriter runWriter, IAuditLogger auditLog, CancellationToken ct = default, string? baseUrl = null)
    {
        var runs = await db.DeclarationCycleRuns
            .Include(r => r.DeclarationCycleSetup)
            .Where(r => r.Sent && !r.Recalled && r.DeclarationCycleSetup != null)
            .ToListAsync(ct);

        foreach (var run in runs)
        {
            var setup = run.DeclarationCycleSetup!;
            if (run.RemindersSent >= setup.ReminderCount) continue;

            if (!IsReminderDue(setup, run.LastReminderSentUtc ?? run.SentAtUtc)) continue;

            var recipients = await ResolveRecipientsAsync(db, run.CompanyId, ct);
            var onBehalfCc = await ResolveOnBehalfCcAsync(db, [.. recipients.Select(r => r.MemberId)], ct);
            var reminderNumber = run.RemindersSent + 1;
            foreach (var (memberId, email, fullName, _) in recipients)
            {
                var body = ApplyPlaceholders(setup.EmailBody, fullName, run.DueDateUtc, baseUrl ?? BaseUrl(null));
                try
                {
                    await emailSender.SendAsync(email, $"Reminder: {setup.EmailSubject}", body, ct,
                        onBehalfCc.GetValueOrDefault(memberId));
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

    /// <summary>A reminder is due once the configured weekday has come round in UAE time and its time
    /// of day has passed. The slots are a week apart by construction, so the only thing to guard
    /// against is sending twice inside one slot -- hence "not already sent today". The notification
    /// itself counts as the last send, so the first reminder waits for the next slot after it.</summary>
    public static bool IsReminderDue(DeclarationCycleSetup setup, DateTime lastSentUtc)
    {
        var now = UaeTime.Now;
        if (now.DayOfWeek != setup.ReminderDayOfWeek) return false;
        if (now.TimeOfDay < setup.ReminderTimeOfDay.ToTimeSpan()) return false;

        return UaeTime.FromUtc(lastSentUtc).Date < now.Date;
    }

    /// <summary>Shared by "Send Now"/schedule-firing: resolves recipients, sends the notification, and
    /// records a per-recipient row (name/email/entity/sent-on) plus an audit-log entry for each.</summary>
    public static async Task<(int SentCount, int FailedCount)> SendNotificationAsync(ApplicationDbContext db, IActivityEmailSender emailSender, IDeclarationCycleRunWriter runWriter, IAuditLogger auditLog, DeclarationCycleRun run, DeclarationCycleSetup setup, string actorName, CancellationToken ct = default, string? baseUrl = null)
    {
        var recipients = await ResolveRecipientsAsync(db, run.CompanyId, ct);
        var onBehalfCc = await ResolveOnBehalfCcAsync(db, [.. recipients.Select(r => r.MemberId)], ct);
        var sentCount = 0;
        var failedCount = 0;

        foreach (var (memberId, email, fullName, companyName) in recipients)
        {
            var body = ApplyPlaceholders(setup.EmailBody, fullName, run.DueDateUtc, baseUrl ?? BaseUrl(null));
            var success = true;
            try
            {
                await emailSender.SendAsync(email, setup.EmailSubject, body, ct,
                    onBehalfCc.GetValueOrDefault(memberId));
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

        // Blackout Period and Scheduled Maintenance are notices; nothing is declared against them.
        // Calling one a "declaration notification" in the email that withdraws it contradicts the
        // notice it is withdrawing, and the page already draws this line in its own heading.
        var isDeclaration = type is DeclarationCycleType.InsiderTrading
            or DeclarationCycleType.ConflictOfInterest
            or DeclarationCycleType.RelatedPartyRegister;

        var subject = $"Recalled: {TypeLabel(type)} {(isDeclaration ? "Declaration Notification" : "Notification")} — {periodLabel}";

        foreach (var recipient in run.Recipients)
        {
            var body = $"""
                <p>Dear {recipient.MemberName},</p>
                <p>The {TypeLabel(type)} {(isDeclaration ? "declaration notification" : "notification")} for <b>{periodLabel}</b> (previously due
                {run.DueDateUtc.ToLocalDisplay("dd/MM/yyyy")}) that was sent to you on
                {run.SentAtUtc.ToLocalDisplay("dd/MM/yyyy")} has been <b>recalled</b> by the Corporate Governance team.</p>
                <p>No {(isDeclaration ? "submission" : "action")} is required for this notice. You will be notified separately if it is re-issued.</p>
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

    /// <summary>For each member, the e-mail addresses of the people allowed to file on their
    /// behalf -- the "Who may file on their behalf?" ticks on the member's own screen. Those
    /// people are copied on the declaration mail, because someone expected to file for a colleague
    /// cannot act on a notice they never saw.
    ///
    /// One query for the whole send rather than one per recipient. The member's own address is
    /// dropped if it appears: a person approved to file for themselves would otherwise be both the
    /// recipient and a copy, and some clients then show the mail twice. Only active filers with an
    /// address count, and the result is semicolon-separated because that is what Database Mail's
    /// @copy_recipients takes.</summary>
    public static async Task<Dictionary<int, string>> ResolveOnBehalfCcAsync(
        ApplicationDbContext db, IReadOnlyCollection<int> memberIds, CancellationToken ct)
    {
        if (memberIds.Count == 0) return [];

        var pairs = await db.MemberImpersonationApprovals
            .Where(a => memberIds.Contains(a.MemberId)
                        && a.Impersonator!.Active
                        && a.Impersonator.Email != null
                        && a.Impersonator.Email != a.Member!.Email)
            .Select(a => new { a.MemberId, Email = a.Impersonator!.Email! })
            .ToListAsync(ct);

        return pairs
            .GroupBy(x => x.MemberId)
            .ToDictionary(
                g => g.Key,
                g => string.Join(";", g.Select(x => x.Email).Distinct(StringComparer.OrdinalIgnoreCase)));
    }

    /// <summary>The site's own address. A reminder is read in a mail client, which is not this
    /// app, so both the logo and every link in a template need an absolute URL. Overridable with
    /// Branding:PublicBaseUrl for a deployment on another host.</summary>
    public static string BaseUrl(IConfiguration? config) =>
        (config?["Branding:PublicBaseUrl"] is { Length: > 0 } url ? url : "https://cg.dubaiinvestments.com")
            .TrimEnd('/');

    /// <summary>What {LogoUrl} becomes when the mail is sent.</summary>
    public static string LogoUrl(IConfiguration? config) => BaseUrl(config) + "/images/di-logo.jpg";

    /// <param name="baseUrl">What {BaseUrl} becomes, and what {LogoUrl} is built from. The sending
    /// paths pass the absolute address, because the mail is read outside the app; the setup
    /// screen's preview passes an empty string, which leaves both site-relative so the preview
    /// shows what THIS build serves rather than whatever the deployed site still has.</param>
    public static string ApplyPlaceholders(string template, string fullName, DateTime dueDateUtc, string baseUrl = "") =>
        template.Replace("{FullName}", fullName)
                .Replace("{DueDate}", dueDateUtc.ToLocalDisplay("dd/MM/yyyy"))
                .Replace("{LogoUrl}", baseUrl.TrimEnd('/') + "/images/di-logo.jpg")
                .Replace("{BaseUrl}", baseUrl.TrimEnd('/'));
}
