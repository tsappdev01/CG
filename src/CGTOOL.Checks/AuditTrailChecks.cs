using CGTOOL.Web.Data.Governance;
using CGTOOL.Web.Data.Reports;

namespace CGTOOL.Checks;

/// <summary>
/// The audit trail is the evidence an ICOFR or ITGC review is handed, so the things that make it
/// evidence rather than a log are checked here: that every action it can record has a name a
/// reader understands, and that the numbers behind those names never move.
///
/// The stored value is the enum's number. Inserting an action in the middle rather than appending
/// one silently rewrites history -- every "Delete" already on the record becomes a "Deactivate" --
/// and nothing about the database would look wrong afterwards.
/// </summary>
public static class AuditTrailChecks
{
    public static void Run()
    {
        var actions = Enum.GetValues<AuditAction>();

        Check.Section($"Audit trail ({actions.Length} recordable actions)");

        foreach (var action in actions)
        {
            var label = AuditTrailLanguage.Change(action);
            Check.That($"{action,-18} reads as \"{label}\"",
                       !string.IsNullOrWhiteSpace(label) && label != action.ToString());
        }

        Check.Section("Audit trail: the stored numbers must not move");
        // Pinned. A failure here means an action was inserted rather than appended, and every
        // entry already written below that number now means something else.
        foreach (var (action, number) in new (AuditAction, int)[]
                 {
                     (AuditAction.Create, 0), (AuditAction.Update, 1), (AuditAction.Delete, 2),
                     (AuditAction.Deactivate, 3), (AuditAction.Reactivate, 4), (AuditAction.RoleChange, 5),
                     (AuditAction.ImpersonationStart, 6), (AuditAction.ImpersonationEnd, 7),
                     (AuditAction.Notify, 8), (AuditAction.Recall, 9), (AuditAction.AccessDenied, 10),
                     (AuditAction.DocumentViewed, 11),
                 })
        {
            Check.That($"{action,-18} is still {number}", (int)action == number);
        }

        Check.Section("Audit trail: reading a document is recordable");
        // Reading leaves no other trace, so if this action goes the trail cannot answer who
        // looked at a member's Emirates ID -- only who uploaded it.
        Check.That("there is an action for opening a stored document",
                   Enum.IsDefined(AuditAction.DocumentViewed));
        Check.That("and a refusal is recorded too, which is the entry worth having",
                   Enum.IsDefined(AuditAction.AccessDenied));

        Check.Section("Audit trail: automated runs are attributable");
        Check.That("a run nobody started still names an actor",
                   !string.IsNullOrWhiteSpace(ScheduledActivityHostedService.SystemActor));
    }
}
