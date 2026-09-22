using System.ComponentModel.DataAnnotations;

namespace CGTOOL.Web.Data.Governance;

/// <summary>One manual reminder sent from the Dashboard compliance table to a Member with a pending
/// Insider Trading or Related Party &amp; COI declaration for a given period -- denormalized (Member
/// name/email cached, not FK'd) same as DeclarationCycleRunRecipient, since this is a point-in-time
/// audit record rather than a live reference.</summary>
public class DeclarationReminderLog
{
    public int Id { get; set; }

    public int MemberId { get; set; }

    [Required, MaxLength(120)]
    public string MemberName { get; set; } = string.Empty;

    [Required, MaxLength(160)]
    public string Email { get; set; } = string.Empty;

    [MaxLength(120)]
    public string? CompanyName { get; set; }

    public DeclarationCycleType Category { get; set; }

    public int Year { get; set; }
    public int Quarter { get; set; }

    public DateTime SentAtUtc { get; set; } = DateTime.UtcNow;

    [Required, MaxLength(160)]
    public string SentByName { get; set; } = string.Empty;
}
