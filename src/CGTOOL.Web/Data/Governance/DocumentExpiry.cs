namespace CGTOOL.Web.Data.Governance;

/// <summary>
/// When an attached document counts as expired, and when being expired actually stops somebody.
///
/// Those are two different questions and the answer is not the same at every stage, which is why
/// this is one place rather than a test written out wherever it is needed:
///
///   importing    never asks. A list of people is loaded as it is; an expired card is a thing to
///                tell the member about, not a reason to refuse their record.
///   onboarding   asks IsExpired, and says so. Onboarding is where there is still time to put it
///                right, so an expired card is raised the moment the member is looking at it.
///   submitting   asks BlocksSubmission, which allows a fortnight's grace. A declaration has a
///                deadline and a renewal does not always beat it; being two days out of date
///                should not make somebody miss a filing.
///
/// Four screens had each written their own "expired" test and two of them disagreed about what
/// day it was -- one read UTC, one read local, which in Gulf Standard Time are different dates
/// for four hours out of every twenty-four, so the same card could be expired on one screen and
/// current on the next.
/// </summary>
public static class DocumentExpiry
{
    /// <summary>How long after a document expires a declaration may still be submitted.
    /// Inclusive: expired exactly this many days ago still goes through.
    ///
    /// Every kind of document, not the Emirates ID alone. A declarant whose passport lapsed on
    /// the Tuesday is in the same position as one whose card did.</summary>
    public const int SubmissionGraceDays = 14;

    /// <summary>The date the rules are read against: local, because an expiry is a calendar date
    /// on a card rather than an instant, and the people reading it are in one place.</summary>
    public static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow.ToLocalTime());

    /// <summary>Past its date. A document with no expiry is not expired -- plenty of things
    /// people attach do not carry one.</summary>
    public static bool IsExpired(DateTime? expiry, DateOnly? asOf = null) =>
        DaysSinceExpiry(expiry, asOf) > 0;

    /// <summary>Days since it expired: positive once past, zero on the day itself, negative while
    /// still current, and null when the document carries no date at all.</summary>
    public static int? DaysSinceExpiry(DateTime? expiry, DateOnly? asOf = null) =>
        expiry is { } date
            ? (asOf ?? Today).DayNumber - DateOnly.FromDateTime(date).DayNumber
            : null;

    /// <summary>Expired, but inside the fortnight a declaration may still be filed in.</summary>
    public static bool IsWithinSubmissionGrace(DateTime? expiry, DateOnly? asOf = null) =>
        DaysSinceExpiry(expiry, asOf) is > 0 and <= SubmissionGraceDays;

    /// <summary>Whether this document, expired as it is, stops a declaration being submitted.</summary>
    public static bool BlocksSubmission(DateTime? expiry, DateOnly? asOf = null) =>
        DaysSinceExpiry(expiry, asOf) > SubmissionGraceDays;

    /// <summary>What to tell somebody whose card has lapsed, or null when there is nothing to
    /// say. One wording, so the onboarding screen and the declaration do not invent their own.</summary>
    /// <param name="documentName">What to call it -- "Emirates ID", or whatever the Document
    /// Centre named an added type. Passed in rather than derived, so a type somebody added is
    /// described by its own name instead of the word "document".</param>
    public static string? Notice(string documentName, DateTime? expiry, DateOnly? asOf = null)
    {
        if (DaysSinceExpiry(expiry, asOf) is not { } days || days <= 0) return null;

        var ago = $"{days} day{(days == 1 ? "" : "s")} ago";

        if (BlocksSubmission(expiry, asOf))
        {
            return $"Your {documentName} expired {ago}. Please upload a valid one before submitting.";
        }

        var left = SubmissionGraceDays - days;
        return $"Your {documentName} expired {ago}. You can still submit for {left} more "
             + $"day{(left == 1 ? "" : "s")}, but please upload a valid one.";
    }
}
