using Microsoft.EntityFrameworkCore;

namespace CGTOOL.Web.Data.Governance;

/// <summary>Sends a weekly reminder email for each open Insider Declaration / Related Party &amp; COI
/// declaration period, to members who haven't submitted yet, until the period's due date.</summary>
public class DeclarationReminderHostedService(IServiceScopeFactory scopeFactory, ILogger<DeclarationReminderHostedService> logger) : BackgroundService
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
                var setupWriter = scope.ServiceProvider.GetRequiredService<IDeclarationSetupWriter>();
                await RunOnceAsync(db, emailSender, setupWriter, DateOnly.FromDateTime(DateTime.UtcNow), stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Declaration reminder check failed.");
            }

            await Task.Delay(TimeSpan.FromHours(24), stoppingToken);
        }
    }

    public static async Task RunOnceAsync(ApplicationDbContext db, IActivityEmailSender emailSender, IDeclarationSetupWriter setupWriter, DateOnly today, CancellationToken ct = default)
    {
        var setups = await db.DeclarationSetups
            .Where(s => s.Active && s.Date <= today && s.DueDate >= today)
            .ToListAsync(ct);

        foreach (var setup in setups)
        {
            var dueForReminder = setup.LastReminderSentUtc is null
                || (DateTime.UtcNow - setup.LastReminderSentUtc.Value).TotalDays >= 7;

            if (!dueForReminder) continue;

            var submittedMemberIds = await db.DeclarationSubmissions
                .Where(s => s.DeclarationSetupId == setup.Id)
                .Select(s => s.MemberId)
                .ToListAsync(ct);

            var recipients = await ResolveRecipientsAsync(db, setup.Type, submittedMemberIds, ct);
            var label = Label(setup.Type);

            foreach (var email in recipients)
            {
                await emailSender.SendAsync(
                    email,
                    $"{label} — declaration reminder",
                    $"This is a reminder that your {label} is due on {setup.DueDate:yyyy-MM-dd}. Please submit it at https://cg.dubaiinvestments.com/.",
                    ct);
            }

            var sentAt = DateTime.UtcNow;
            await setupWriter.SetLastReminderSentUtcAsync(setup.Id, sentAt);
            setup.LastReminderSentUtc = sentAt;
        }
    }

    private static Task<List<string>> ResolveRecipientsAsync(ApplicationDbContext db, DeclarationType type, List<int> alreadySubmittedMemberIds, CancellationToken ct)
    {
        var query = db.Members.Where(m => m.Active && m.Email != null && !alreadySubmittedMemberIds.Contains(m.Id));

        query = type switch
        {
            DeclarationType.ConflictOfInterest => query.Where(m => m.ConflictOfInterestAccess || m.RelatedPartyRegisterAccess),
            _ => query.Where(m => m.InsiderTradingAccess || m.IsBoardMember),
        };

        return query.Select(m => m.Email!).ToListAsync(ct);
    }

    private static string Label(DeclarationType type) => type switch
    {
        DeclarationType.InsiderTrading => "Insider Declaration",
        DeclarationType.ConflictOfInterest => "Related Party & COI Declaration",
        _ => type.ToString(),
    };
}
