using System.ComponentModel.DataAnnotations;

namespace CGTOOL.Web.Data.Governance;

public enum AuditReviewOutcome
{
    /// <summary>Looked at, nothing further required.</summary>
    Reviewed,

    /// <summary>Looked at and something is wrong -- raised for follow-up.</summary>
    ExceptionRaised,
}

/// <summary>
/// A reviewer's sign-off on one audit entry, for the periodic review an auditor asks to see.
///
/// Kept in its own table rather than as columns on the entry: the entry is append-only and sealed
/// by a hash, so writing a review into it would break the chain. A review is a later, separate
/// assertion about an event, not part of the event.
/// </summary>
public class AuditLogReview
{
    public int Id { get; set; }

    public int AuditLogEntryId { get; set; }
    public AuditLogEntry? AuditLogEntry { get; set; }

    [Required, MaxLength(160)]
    public string ReviewerName { get; set; } = string.Empty;

    public DateTime ReviewedAtUtc { get; set; } = DateTime.UtcNow;

    public AuditReviewOutcome Outcome { get; set; } = AuditReviewOutcome.Reviewed;

    [Required, MaxLength(1000)]
    public string Comment { get; set; } = string.Empty;
}
