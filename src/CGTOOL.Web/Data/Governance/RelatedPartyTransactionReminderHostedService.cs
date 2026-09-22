using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace CGTOOL.Web.Data.Governance;

/// <summary>FRD §3.3.3 -- 7-day/weekly reminders to the Approver while a transaction is AwaitingApproval,
/// 30-day auto-escalation to CCAO, and 7-day/weekly reminders to CCAO/CFO once Escalated (timed from
/// EscalatedAtUtc, not the original request date). Runs hourly, same pattern as
/// DeclarationReminderHostedService.</summary>
public class RelatedPartyTransactionReminderHostedService(IServiceScopeFactory scopeFactory, ILogger<RelatedPartyTransactionReminderHostedService> logger) : BackgroundService
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
                var writer = scope.ServiceProvider.GetRequiredService<IRelatedPartyTransactionWriter>();
                var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
                await RunOnceAsync(db, emailSender, writer, userManager, DateTime.UtcNow, stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Related Party Transaction reminder check failed.");
            }

            await Task.Delay(TimeSpan.FromHours(1), stoppingToken);
        }
    }

    public static async Task RunOnceAsync(ApplicationDbContext db, IActivityEmailSender emailSender, IRelatedPartyTransactionWriter writer, UserManager<ApplicationUser> userManager, DateTime nowUtc, CancellationToken ct = default)
    {
        var ccaoEmails = await RpTransactionRoleResolver.GetRoleEmailsAsync(db, RpTransactionRole.Ccao);
        var cfoEmails = await RpTransactionRoleResolver.GetRoleEmailsAsync(db, RpTransactionRole.Cfo);

        var awaiting = await db.RelatedPartyTransactions
            .Include(t => t.Member)
            .Include(t => t.Company)
            .Include(t => t.ApproverMember)
            .Where(t => t.Status == RpTransactionStatus.AwaitingApproval)
            .ToListAsync(ct);

        foreach (var t in awaiting)
        {
            var daysSinceRequest = (nowUtc - t.DateOfRequest).TotalDays;

            if (daysSinceRequest >= 30)
            {
                await writer.AutoEscalateAsync(t.Id, RpEscalationReason.AutoTimeout30Days, nowUtc);
                t.Status = RpTransactionStatus.Escalated;
                t.EscalationReason = RpEscalationReason.AutoTimeout30Days;
                t.EscalatedAtUtc = nowUtc;
                await RpTransactionNotificationService.EscalatedAsync(emailSender, t, t.ApproverMember?.Email, ccaoEmails, cfoEmails, RpEscalationReason.AutoTimeout30Days);
                continue;
            }

            var dueForReminder = daysSinceRequest >= 7
                && (t.LastReminderSentUtc is null || (nowUtc - t.LastReminderSentUtc.Value).TotalDays >= 7);

            if (dueForReminder)
            {
                await RpTransactionNotificationService.ReminderApproverAsync(emailSender, t, t.ApproverMember?.Email);
                await writer.SetLastReminderSentAsync(t.Id, nowUtc);
            }
        }

        // Escalated and still awaiting the CCAO's Form 3 decision -- timed from EscalatedAtUtc, not
        // DateOfRequest, per §3.3.3 item 4. A transaction that reached this state via the 30-day
        // auto-escalation above in this same run keeps whatever LastReminderSentUtc it already had
        // from the AwaitingApproval reminders; in the rare case that lands within 7 days of "now" the
        // first CCAO reminder is delayed slightly rather than firing immediately -- an acceptable
        // simplification rather than plumbing a reset through every escalation path.
        var escalated = await db.RelatedPartyTransactions
            .Include(t => t.Member)
            .Include(t => t.Company)
            .Where(t => t.Status == RpTransactionStatus.Escalated && t.CcaoAction == null)
            .ToListAsync(ct);

        foreach (var t in escalated)
        {
            var escalatedAt = t.EscalatedAtUtc ?? t.DateOfRequest;
            var daysSinceEscalation = (nowUtc - escalatedAt).TotalDays;

            var dueForReminder = daysSinceEscalation >= 7
                && (t.LastReminderSentUtc is null || (nowUtc - t.LastReminderSentUtc.Value).TotalDays >= 7);

            if (dueForReminder)
            {
                await RpTransactionNotificationService.ReminderCcaoAsync(emailSender, t, ccaoEmails, cfoEmails);
                await writer.SetLastReminderSentAsync(t.Id, nowUtc);
            }
        }
    }
}
