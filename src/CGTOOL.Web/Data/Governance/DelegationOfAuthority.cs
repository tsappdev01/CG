using System.ComponentModel.DataAnnotations;

namespace CGTOOL.Web.Data.Governance;

/// <summary>Who has to approve an RP Transaction of a given size. The workflow's stage 1 says
/// "route by authority — per DoA matrix"; without one, every transaction went to the entity's single
/// approving authority whatever it was worth, and "escalate: over limit" could never happen.</summary>
public enum RpApprovalAuthority
{
    ApproverOnly,
    ApproverThenCcao,
    ApproverCcaoThenAuditCommittee,
    ApproverCcaoThenBoard,
    ApproverCcaoThenGeneralAssembly,
}

public static class RpApprovalAuthorities
{
    public static readonly (RpApprovalAuthority Value, string Label)[] All =
    [
        (RpApprovalAuthority.ApproverOnly, "Approver only"),
        (RpApprovalAuthority.ApproverThenCcao, "Approver, then CCAO"),
        (RpApprovalAuthority.ApproverCcaoThenAuditCommittee, "Approver, CCAO, then Audit Committee"),
        (RpApprovalAuthority.ApproverCcaoThenBoard, "Approver, CCAO, then Board of Directors"),
        (RpApprovalAuthority.ApproverCcaoThenGeneralAssembly, "Approver, CCAO, then General Assembly"),
    ];

    public static string Label(RpApprovalAuthority value) => All.First(a => a.Value == value).Label;

    /// <summary>Whether a band needs the CCAO at all. Everything above ApproverOnly does, which is
    /// what makes a transaction "over the approver's limit".</summary>
    public static bool RequiresCcao(RpApprovalAuthority value) => value != RpApprovalAuthority.ApproverOnly;
}

/// <summary>One entity's Delegation of Authority: the value bands, and the conditions that send a
/// transaction to the CCAO regardless of its value.
///
/// An entity with no row here keeps the behaviour that existed before: one approver, no value
/// routing. That is deliberate -- a half-configured matrix must not silently start routing
/// transactions somewhere nobody chose.</summary>
public class DelegationOfAuthority
{
    public int Id { get; set; }

    public int CompanyId { get; set; }
    public Company? Company { get; set; }

    /// <summary>When this matrix started applying. Recorded rather than used to pick between
    /// versions: a transaction already in flight keeps the route it was given, so changing the
    /// matrix never re-routes it.</summary>
    public DateTime EffectiveFrom { get; set; } = DateTime.Today;

    /// <summary>The most the approver may clear on their own. A transaction above it goes straight
    /// to the CCAO at submission -- the workflow's "escalate: over limit", which is a different
    /// thing from the bands: the bands say which body must ultimately approve, this says whether the
    /// approver is in the path at all. Null means no limit, which is how every entity behaved before
    /// this existed.</summary>
    public decimal? ApproverLimit { get; set; }

    public bool EscalateOnApproverConflict { get; set; } = true;

    /// <summary>Days before an undecided transaction escalates by itself. Null leaves the app-wide
    /// default (RelatedPartyTransactionReminderHostedService.AutoEscalateAfterDays) in place.</summary>
    public int? EscalateAfterDays { get; set; }

    public DateTime ModifiedAtUtc { get; set; } = DateTime.UtcNow;

    public List<DelegationOfAuthorityBand> Bands { get; set; } = [];

    /// <summary>The band a value falls in, or null where the bands don't cover it -- which the
    /// editor refuses to save, but an older row may still have.</summary>
    public DelegationOfAuthorityBand? BandFor(decimal value) =>
        Bands.FirstOrDefault(b => value >= b.FromValue && (b.ToValue is null || value <= b.ToValue));
}

/// <summary>One value band. ToValue null is the top band -- no upper limit.</summary>
public class DelegationOfAuthorityBand
{
    public int Id { get; set; }

    public int DelegationOfAuthorityId { get; set; }
    public DelegationOfAuthority? DelegationOfAuthority { get; set; }

    public decimal FromValue { get; set; }

    public decimal? ToValue { get; set; }

    public RpApprovalAuthority Authority { get; set; } = RpApprovalAuthority.ApproverOnly;

    public string Range => ToValue is null
        ? $"{FromValue:N0} and above"
        : $"{FromValue:N0} – {ToValue.Value:N0}";
}
