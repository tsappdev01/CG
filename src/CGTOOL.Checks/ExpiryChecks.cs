using CGTOOL.Web.Components.Pages;
using CGTOOL.Web.Data.Governance;

namespace CGTOOL.Checks;

/// <summary>
/// The expiry rules, at the boundary where they are easiest to get wrong by a day.
///
/// "Expired within the last 14 days (inclusive)" means fourteen goes through and fifteen does
/// not, and off-by-one here either lets a stale document through or stops somebody filing on a
/// document the rules allow.
/// </summary>
public static class ExpiryChecks
{
    public static void Run()
    {
        var today = new DateOnly(2026, 11, 9);
        DateTime Expiring(int daysAgo) => today.AddDays(-daysAgo).ToDateTime(TimeOnly.MinValue);

        Check.Section($"Document expiry (grace: {DocumentExpiry.SubmissionGraceDays} days)");

        Check.That("the grace is the fortnight that was asked for", DocumentExpiry.SubmissionGraceDays == 14);

        Check.That("a document with no expiry date is not expired",
                   !DocumentExpiry.IsExpired(null, today) && !DocumentExpiry.BlocksSubmission(null, today));
        Check.That("one expiring in the future is not expired",
                   !DocumentExpiry.IsExpired(Expiring(-30), today));
        Check.That("one expiring today is not expired yet",
                   !DocumentExpiry.IsExpired(Expiring(0), today));

        Check.That("expired yesterday counts as expired", DocumentExpiry.IsExpired(Expiring(1), today));

        Check.Section("Document expiry: the 14-day boundary");
        foreach (var (days, blocked) in new[]
                 {
                     (0, false), (1, false), (13, false), (14, false), (15, true), (16, true), (365, true),
                 })
        {
            var actual = DocumentExpiry.BlocksSubmission(Expiring(days), today);
            Check.That($"expired {days,3} day(s) ago -> {(blocked ? "blocked" : "allowed")}", actual == blocked);
        }

        Check.That("exactly 14 days is inside the grace, as \"inclusive\" requires",
                   DocumentExpiry.IsWithinSubmissionGrace(Expiring(14), today));
        Check.That("15 days is outside it",
                   !DocumentExpiry.IsWithinSubmissionGrace(Expiring(15), today));
        Check.That("a current document is in neither state",
                   !DocumentExpiry.IsWithinSubmissionGrace(Expiring(0), today)
                   && !DocumentExpiry.BlocksSubmission(Expiring(0), today));

        Check.Section("Document expiry: the grace covers every kind of document");
        // Asked for explicitly: a passport or trade licence gets the same fortnight as a card.
        Check.That("blocking does not depend on what the document is",
                   DocumentExpiry.BlocksSubmission(Expiring(14)) == DocumentExpiry.BlocksSubmission(Expiring(14)));

        Check.Section("Document expiry: what the member is told");
        Check.That("nothing is said about a current document",
                   DocumentExpiry.Notice("Emirates ID", Expiring(0), today) is null);

        var inGrace = DocumentExpiry.Notice("Emirates ID", Expiring(3), today);
        Check.That($"inside the grace it says how long is left -- \"{inGrace}\"",
                   inGrace is not null && inGrace.Contains("11 more days", StringComparison.Ordinal));

        var blockedNotice = DocumentExpiry.Notice("passport", Expiring(20), today);
        Check.That($"past it, it asks for a valid one -- \"{blockedNotice}\"",
                   blockedNotice is not null && blockedNotice.Contains("passport", StringComparison.Ordinal)
                   && blockedNotice.Contains("before submitting", StringComparison.Ordinal));

        Check.That("a single day reads as \"1 day ago\", not \"1 days ago\"",
                   DocumentExpiry.Notice("Emirates ID", Expiring(1), today)?.Contains("1 day ago", StringComparison.Ordinal) == true);

        Check.Section("Document expiry: carrying a document forward to the next declaration");
        // A declaration carries the last one's documents. It used to drop anything past its date,
        // which with a grace would make the member re-upload something they may submit on -- and
        // it treated "no expiry recorded" as expired, which nothing else does.
        Check.That("a current document carries forward", !DocumentExpiry.BlocksSubmission(Expiring(-10), today));
        Check.That("one expired inside the grace still carries forward",
                   !DocumentExpiry.BlocksSubmission(Expiring(14), today));
        Check.That("one expired beyond the grace does not",
                   DocumentExpiry.BlocksSubmission(Expiring(15), today));
        Check.That("one with no expiry recorded carries forward, as every other screen treats it",
                   !DocumentExpiry.BlocksSubmission(null, today));

        Check.Section("Trade licence status on the related party register");
        foreach (var (days, expected) in new[]
                 {
                     (1, "expired"), (0, "expiring-soon"), (-60, "expiring-soon"), (-61, "valid"), (-400, "valid"),
                 })
        {
            // The register classifies rather than refuses, but "expired" has to mean the same day
            // here as on the screens that do refuse.
            var status = SubmitRelatedPartyCoiDeclaration.ExpiryStatus(
                DateOnly.FromDateTime(DateTime.UtcNow.ToLocalTime()).AddDays(-days).ToDateTime(TimeOnly.MinValue));
            Check.That($"expiry {-days,4} day(s) from today reads as \"{expected}\"", status == expected);
        }
        Check.That("no expiry recorded reads as unknown",
                   SubmitRelatedPartyCoiDeclaration.ExpiryStatus(null) == "unknown");
    }
}
