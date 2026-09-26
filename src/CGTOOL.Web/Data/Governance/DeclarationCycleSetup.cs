using System.ComponentModel.DataAnnotations;

namespace CGTOOL.Web.Data.Governance;

/// <summary>The five declaration/notification categories under Data Management > Declarations Setup
/// (31-Jul-2026 requirement note). Recipients for every type are all active users of the selected
/// entity -- there is no role-based gating.</summary>
public enum DeclarationCycleType
{
    InsiderTrading,
    ConflictOfInterest,
    RelatedPartyRegister,
    BlackoutPeriod,
    ScheduledMaintenance,
}

/// <summary>One configuration row per DeclarationCycleType (singleton, like ScheduledActivity) -- how
/// many reminders to send, which day and time of the week they go out on, and the email template used
/// when an admin sends or schedules a cycle. Which period a notification covers, and its due date, are
/// still set per send/schedule action on DeclarationCycleRun rather than as a recurring policy here.</summary>
public class DeclarationCycleSetup
{
    public int Id { get; set; }

    public DeclarationCycleType Type { get; set; }

    [Range(0, 20)]
    public int ReminderCount { get; set; }

    /// <summary>Reminders go out once a week, on this day. Saturday by default: the UAE working week
    /// opens on Sunday, so a Saturday reminder is waiting when people come in.</summary>
    public DayOfWeek ReminderDayOfWeek { get; set; } = DayOfWeek.Saturday;

    /// <summary>The time of day reminders are sent, and the time a "Schedule Send" fires on its chosen
    /// date. Read as UAE Standard Time (see UaeTime), never as the server's own time zone.</summary>
    public TimeOnly ReminderTimeOfDay { get; set; } = new(8, 0);

    [Required, MaxLength(200)]
    public string EmailSubject { get; set; } = string.Empty;

    /// <summary>Supports {FullName} and {DueDate} placeholders, substituted per recipient at send time.</summary>
    [Required]
    public string EmailBody { get; set; } = string.Empty;

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime ModifiedAtUtc { get; set; } = DateTime.UtcNow;
}

/// <summary>One notification event for a DeclarationCycleSetup -- either sent immediately ("Send Now")
/// or scheduled for a future date (always 08:00 on that date) via "Schedule Send". Sent=false rows are
/// scheduled-but-not-yet-fired; the hosted service flips them to Sent=true once their SentAtUtc arrives,
/// resolving recipients and sending at that point (RecipientCount/reminders are meaningless until then).</summary>
public class DeclarationCycleRun
{
    public int Id { get; set; }

    public int DeclarationCycleSetupId { get; set; }
    public DeclarationCycleSetup? DeclarationCycleSetup { get; set; }

    public DeclarationCycleType Type { get; set; }

    /// <summary>Null means every entity.</summary>
    public int? CompanyId { get; set; }
    public Company? Company { get; set; }

    /// <summary>For a "Send Now" run, when it was actually sent. For a scheduled-but-not-yet-fired
    /// run, the target send time (08:00 on the chosen date) until the hosted service fires it, after
    /// which this is updated to the actual send time.</summary>
    public DateTime SentAtUtc { get; set; } = DateTime.UtcNow;

    /// <summary>The declaration period this run covers, chosen explicitly by the admin sending/
    /// scheduling it rather than derived from SentAtUtc -- sending a catch-up notification a few days
    /// into the next quarter must not silently relabel it, and a late "Send Now" should still be able
    /// to target the quarter it was actually meant for.</summary>
    public int PeriodYear { get; set; }
    public int PeriodQuarter { get; set; }

    public DateTime DueDateUtc { get; set; }

    /// <summary>False while a scheduled send is still pending its target date/time.</summary>
    public bool Sent { get; set; } = true;

    public int RecipientCount { get; set; }

    public int RemindersSent { get; set; }

    public DateTime? LastReminderSentUtc { get; set; }

    /// <summary>Set when an admin recalls a Sent run (only allowed while it has zero submissions) --
    /// once true, this run stops counting as "due" everywhere (wizard pages, the dashboard banner, My
    /// Declarations) and stops receiving further reminders, without deleting its history row.</summary>
    public bool Recalled { get; set; }

    public DateTime? RecalledAtUtc { get; set; }

    [MaxLength(160)]
    public string? RecalledByName { get; set; }

    public List<DeclarationCycleRunRecipient> Recipients { get; set; } = [];
}

/// <summary>One recipient of a DeclarationCycleRun's notification -- who, their email, their entity,
/// and when they were sent it, for detailed print/reporting (name/email/sent-on/entity).</summary>
public class DeclarationCycleRunRecipient
{
    public int Id { get; set; }

    public int DeclarationCycleRunId { get; set; }
    public DeclarationCycleRun? DeclarationCycleRun { get; set; }

    public int MemberId { get; set; }

    [Required, MaxLength(120)]
    public string MemberName { get; set; } = string.Empty;

    [Required, MaxLength(160)]
    public string Email { get; set; } = string.Empty;

    [MaxLength(120)]
    public string? CompanyName { get; set; }

    public DateTime SentAtUtc { get; set; } = DateTime.UtcNow;

    public bool Success { get; set; } = true;
}
