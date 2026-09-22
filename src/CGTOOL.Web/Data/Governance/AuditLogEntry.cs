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
}
