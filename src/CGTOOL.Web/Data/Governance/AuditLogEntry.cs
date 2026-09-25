using System.ComponentModel.DataAnnotations;

namespace CGTOOL.Web.Data.Governance;

public enum AuditAction
{
    Create,
    Update,
    Delete,
    Deactivate,
    Reactivate,
    RoleChange,
    ImpersonationStart,
    ImpersonationEnd,
    Notify,
    Recall,
}

/// <summary>Immutable record of a CRUD/permission change, kept for SCA compliance audits.</summary>
public class AuditLogEntry
{
    public int Id { get; set; }

    public DateTime OccurredAtUtc { get; set; } = DateTime.UtcNow;

    [Required, MaxLength(160)]
    public string ActorDisplayName { get; set; } = string.Empty;

    /// <summary>Set when the actor was impersonating another member at the time of the action.</summary>
    [MaxLength(160)]
    public string? ActingOnBehalfOf { get; set; }

    public AuditAction Action { get; set; }

    [Required, MaxLength(80)]
    public string EntityType { get; set; } = string.Empty;

    [MaxLength(80)]
    public string? EntityId { get; set; }

    [MaxLength(2000)]
    public string? Details { get; set; }

    /// <summary>The business reason (WHY) -- required for Delete/Deactivate/RoleChange-removal actions.</summary>
    [MaxLength(1000)]
    public string? Justification { get; set; }

    [MaxLength(64)]
    public string? IpAddress { get; set; }

    [MaxLength(400)]
    public string? UserAgent { get; set; }

    // ---------------------------------------------------------------- tamper evidence
    //
    // Each row is sealed with a SHA-256 over its own fields plus the previous row's hash, so the
    // log is a chain: altering or removing a row breaks every hash after it, and the break is
    // detectable without knowing what the row used to say.
    //
    // Both the sealing and the verification are done in SQL (usp_AuditLogEntry_Insert and
    // usp_AuditLog_Verify), deliberately: one definition of the payload, so there is no way for a
    // C# encoding difference to make a sound chain look broken. The sequence and previous hash are
    // taken under an application lock inside the insert, so two concurrent writes cannot claim the
    // same position.
    //
    // Rows written before the chain existed were sealed retrospectively by the backfill, which
    // means they are protected from the moment of backfill onwards and not before. See README.

    /// <summary>Position in the chain, 1-based and gapless. Null only on a row the backfill has
    /// not reached.</summary>
    public long? Sequence { get; set; }

    /// <summary>SHA-256, hex, of this row's fields and <see cref="PreviousHash"/>.</summary>
    [MaxLength(64)]
    public string? RecordHash { get; set; }

    /// <summary>The previous row's <see cref="RecordHash"/>; a fixed genesis value for the first.</summary>
    [MaxLength(64)]
    public string? PreviousHash { get; set; }
}
