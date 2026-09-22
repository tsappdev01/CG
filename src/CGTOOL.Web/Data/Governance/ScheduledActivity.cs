namespace CGTOOL.Web.Data.Governance;

public enum ScheduledActivityType
{
    BlackoutPeriod,
    InsiderTrading,
    ConflictOfInterest,
    Outage,
}

/// <summary>A recurring compliance activity that triggers a reminder email on specific month/day dates each year.</summary>
public class ScheduledActivity
{
    public int Id { get; set; }

    public ScheduledActivityType Type { get; set; }

    public bool Active { get; set; } = true;

    public List<ScheduledActivityDate> Dates { get; set; } = [];
}

/// <summary>One recurring (month, day) trigger for a ScheduledActivity, e.g. "15th of March".</summary>
public class ScheduledActivityDate
{
    public int Id { get; set; }

    public int ScheduledActivityId { get; set; }
    public ScheduledActivity? ScheduledActivity { get; set; }

    public int Month { get; set; }

    public int Day { get; set; }

    /// <summary>UTC timestamp of the last time this date triggered an email send, to avoid duplicate sends within the same day.</summary>
    public DateTime? LastTriggeredUtc { get; set; }
}
