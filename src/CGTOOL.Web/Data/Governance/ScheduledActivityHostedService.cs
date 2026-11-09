using Microsoft.EntityFrameworkCore;
using System.Globalization;

namespace CGTOOL.Web.Data.Governance;

public class ScheduledActivityHostedService(IServiceScopeFactory scopeFactory, ILogger<ScheduledActivityHostedService> logger) : BackgroundService
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
                var activityWriter = scope.ServiceProvider.GetRequiredService<IScheduledActivityWriter>();
                var auditLog = scope.ServiceProvider.GetRequiredService<IAuditLogger>();
                await RunOnceAsync(db, emailSender, activityWriter, DateOnly.FromDateTime(DateTime.UtcNow), stoppingToken, auditLog);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Scheduled activity check failed.");

                // In the audit log as well as the application log. A run that failed is the one
                // an auditor needs evidence of -- "the reminders went out every quarter" is only
                // worth anything if the times they did not are on the same record.
                try
                {
                    using var failureScope = scopeFactory.CreateScope();
                    await failureScope.ServiceProvider.GetRequiredService<IAuditLogger>().LogAsync(
                        SystemActor, AuditAction.Update, nameof(ScheduledActivity), null,
                        $"Scheduled activity run failed: {ex.Message}");
                }
                catch (Exception auditFailure)
                {
                    // Never let recording a failure become a second one.
                    logger.LogError(auditFailure, "Could not record the scheduled activity failure in the audit log.");
                }
            }

            try
            {
                await Task.Delay(TimeSpan.FromHours(24), stoppingToken);
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

    /// <summary>What the audit log calls a run nobody started. The actor column is never blank:
    /// an entry with no actor reads like one whose actor was lost.</summary>
    public const string SystemActor = "System (scheduled)";

    public static async Task RunOnceAsync(ApplicationDbContext db, IActivityEmailSender emailSender, IScheduledActivityWriter activityWriter, DateOnly today, CancellationToken ct = default, IAuditLogger? auditLog = null)
    {
        var activities = await db.ScheduledActivities.Include(a => a.Dates).Where(a => a.Active).ToListAsync(ct);

        foreach (var activity in activities)
        {
            var match = activity.Dates.FirstOrDefault(d => d.Month == today.Month && d.Day == today.Day);
            if (match is null) continue;
            if (match.LastTriggeredUtc?.Date == DateTime.UtcNow.Date) continue;

            var recipients = await ResolveRecipientsAsync(db, activity.Type, ct);
            var label = ActivityLabel(activity.Type);

            // Copied, like every other declaration reminder: a person approved to file for a
            // colleague cannot act on a notice they never saw.
            var onBehalfCc = await DeclarationCycleReminderHostedService.ResolveOnBehalfCcAsync(
                db, [.. recipients.Select(r => r.MemberId)], ct);

            foreach (var (memberId, email) in recipients)
            {
                await emailSender.SendAsync(email, $"{label} declaration reminder",
                    $"This is a reminder that your {label} declaration is due.", ct,
                    onBehalfCc.GetValueOrDefault(memberId));
            }

            var triggeredAt = DateTime.UtcNow;
            await activityWriter.SetDateLastTriggeredAsync(match.Id, triggeredAt);
            match.LastTriggeredUtc = triggeredAt;

            // Evidence the run happened, what it sent and to how many. Until now the only trace
            // was LastTriggeredUtc on the row, which says a reminder went out and not who it
            // reached -- and which the next run overwrites.
            if (auditLog is not null)
            {
                await auditLog.LogAsync(
                    SystemActor,
                    AuditAction.Notify,
                    nameof(ScheduledActivity),
                    activity.Id.ToString(CultureInfo.InvariantCulture),
                    $"{label} reminder sent to {recipients.Count} recipient(s) on {today:dd/MM/yyyy}.");
            }
        }
    }

    /// <summary>The id travels with the address: whoever may file on this person's behalf is looked
    /// up by member, and an address alone cannot be looked up by.</summary>
    private static async Task<List<(int MemberId, string Email)>> ResolveRecipientsAsync(ApplicationDbContext db, ScheduledActivityType type, CancellationToken ct)
    {
        var query = db.Members.Where(m => m.Active && m.Email != null);

        query = type switch
        {
            ScheduledActivityType.ConflictOfInterest => query.Where(m => m.ConflictOfInterestAccess),
            _ => query.Where(m => m.InsiderTradingAccess || m.IsBoardMember),
        };

        // Projected to an anonymous type and paired afterwards: a tuple constructed inside the
        // query is the provider's business to translate, and a reminder run is not the place to
        // find out it would rather not.
        var rows = await query.Select(m => new { m.Id, Email = m.Email! }).ToListAsync(ct);
        return [.. rows.Select(r => (r.Id, r.Email))];
    }

    private static string ActivityLabel(ScheduledActivityType type) => type switch
    {
        ScheduledActivityType.BlackoutPeriod => "Blackout Period",
        ScheduledActivityType.InsiderTrading => "Insider Trading",
        ScheduledActivityType.ConflictOfInterest => "Conflict of Interest",
        ScheduledActivityType.Outage => "Outage",
        _ => type.ToString(),
    };
}
