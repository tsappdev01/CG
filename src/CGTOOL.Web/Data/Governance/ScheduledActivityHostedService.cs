using Microsoft.EntityFrameworkCore;

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
                await RunOnceAsync(db, emailSender, activityWriter, DateOnly.FromDateTime(DateTime.UtcNow), stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Scheduled activity check failed.");
            }

            await Task.Delay(TimeSpan.FromHours(24), stoppingToken);
        }
    }

    public static async Task RunOnceAsync(ApplicationDbContext db, IActivityEmailSender emailSender, IScheduledActivityWriter activityWriter, DateOnly today, CancellationToken ct = default)
    {
        var activities = await db.ScheduledActivities.Include(a => a.Dates).Where(a => a.Active).ToListAsync(ct);

        foreach (var activity in activities)
        {
            var match = activity.Dates.FirstOrDefault(d => d.Month == today.Month && d.Day == today.Day);
            if (match is null) continue;
            if (match.LastTriggeredUtc?.Date == DateTime.UtcNow.Date) continue;

            var recipients = await ResolveRecipientsAsync(db, activity.Type, ct);
            var label = ActivityLabel(activity.Type);
            foreach (var email in recipients)
            {
                await emailSender.SendAsync(email, $"{label} declaration reminder", $"This is a reminder that your {label} declaration is due.", ct);
            }

            var triggeredAt = DateTime.UtcNow;
            await activityWriter.SetDateLastTriggeredAsync(match.Id, triggeredAt);
            match.LastTriggeredUtc = triggeredAt;
        }
    }

    private static Task<List<string>> ResolveRecipientsAsync(ApplicationDbContext db, ScheduledActivityType type, CancellationToken ct)
    {
        var query = db.Members.Where(m => m.Active && m.Email != null);

        query = type switch
        {
            ScheduledActivityType.ConflictOfInterest => query.Where(m => m.ConflictOfInterestAccess),
            _ => query.Where(m => m.InsiderTradingAccess || m.IsBoardMember),
        };

        return query.Select(m => m.Email!).ToListAsync(ct);
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
