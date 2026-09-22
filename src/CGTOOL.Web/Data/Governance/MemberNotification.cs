using System.ComponentModel.DataAnnotations;

namespace CGTOOL.Web.Data.Governance;

public enum MemberNotificationStatus
{
    Pending,
    Sent,
    Failed,
}

/// <summary>FRD §2.2 — the "notify user?" decision made when a Member is created, and (for Notify Now
/// / Send Message Later) the resulting delivery record: queued at CreatedAtUtc, resolved to Sent/Failed
/// with SentAtUtc once actually dispatched (immediately, or later from the Pending Notifications queue).</summary>
public class MemberNotification
{
    public int Id { get; set; }

    public int MemberId { get; set; }
    public Member? Member { get; set; }

    [Required, MaxLength(160)]
    public string Recipient { get; set; } = string.Empty;

    public MemberNotificationStatus Status { get; set; } = MemberNotificationStatus.Pending;

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? SentAtUtc { get; set; }
}

/// <summary>The three options on the FRD §2.2 "Do you want to notify user?" prompt.</summary>
public enum NotifyChoice
{
    Now,
    Later,
    No,
}
