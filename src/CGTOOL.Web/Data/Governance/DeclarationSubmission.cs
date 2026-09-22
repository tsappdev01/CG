namespace CGTOOL.Web.Data.Governance;

/// <summary>A single user's submitted acknowledgement of a declaration period (Insider Declaration / Related Party &amp; COI).</summary>
public class DeclarationSubmission
{
    public int Id { get; set; }

    public int MemberId { get; set; }
    public Member? Member { get; set; }

    public int DeclarationSetupId { get; set; }
    public DeclarationSetup? DeclarationSetup { get; set; }

    public DateTime SubmittedAtUtc { get; set; } = DateTime.UtcNow;
}
