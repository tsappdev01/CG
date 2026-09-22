namespace CGTOOL.Web.Data.Governance;

public enum DeclarationType
{
    InsiderTrading,
    ConflictOfInterest,
}

/// <summary>A declaration period: when it opens, when it's due, which transaction type it covers.</summary>
public class DeclarationSetup
{
    public int Id { get; set; }

    public DateOnly Date { get; set; }

    public DateOnly DueDate { get; set; }

    public bool Active { get; set; } = true;

    public DeclarationType Type { get; set; }

    /// <summary>When the weekly reminder email was last sent for this period (null = never sent yet).</summary>
    public DateTime? LastReminderSentUtc { get; set; }
}
