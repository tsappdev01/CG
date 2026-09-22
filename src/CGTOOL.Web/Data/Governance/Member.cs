using System.ComponentModel.DataAnnotations;

namespace CGTOOL.Web.Data.Governance;

/// <summary>A Member's position in the Related Party Transaction workflow (FRD §3) -- entirely
/// separate from the ASP.NET Identity "System roles for this login" mechanism (Administrator/Normal
/// User), since a person can be e.g. both Administrator and CCAO at once, or CCAO without any elevated
/// system role at all. Single-select: one person holds at most one of these titles.</summary>
public enum RpTransactionRole
{
    None,
    Ccao,
    Cfo,
    Coo,
    MdCeo,
}

public class Member
{
    public int Id { get; set; }

    [Range(1, int.MaxValue, ErrorMessage = "Select an entity.")]
    public int CompanyId { get; set; }
    public Company? Company { get; set; }

    [Required, MaxLength(120)]
    public string FullName { get; set; } = string.Empty;

    [MaxLength(120)]
    public string? JobTitle { get; set; }

    public int? DepartmentId { get; set; }
    public Department? Department { get; set; }

    [MaxLength(160), EmailAddress(ErrorMessage = "Enter a valid email address.")]
    public string? Email { get; set; }

    /// <summary>Azure AD object ID ("System ID"), populated when the record is loaded/linked from Azure AD.</summary>
    [MaxLength(80)]
    public string? AzureAdObjectId { get; set; }

    [MaxLength(160)]
    public string? Signature { get; set; }

    public int? ReportingManagerId { get; set; }
    public Member? ReportingManager { get; set; }

    public bool IsBoardMember { get; set; }

    /// <summary>True when the person was added manually because the entity's AD isn't synced with the DI data center.</summary>
    public bool IsManualEntry { get; set; }

    public bool IsExternalMember { get; set; }

    /// <summary>Senior Executive Management -- distinguishes the SEM population from Board of
    /// Directors (IsBoardMember) for anything that needs to tell the two apart, e.g. which
    /// Related Party &amp; COI declaration wording variant applies.</summary>
    public bool IsExecutiveManagement { get; set; }

    public bool Active { get; set; } = true;

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime ModifiedAtUtc { get; set; } = DateTime.UtcNow;

    // Role Assigned
    public bool InsiderTradingAccess { get; set; }
    public bool ConflictOfInterestAccess { get; set; }
    public bool RelatedPartyRegisterAccess { get; set; }
    public bool RelatedPartyTransactionAccess { get; set; }

    /// <summary>RP Transaction workflow position (CCAO/CFO/COO/MD&amp;CEO) -- a separate concept from
    /// system roles/access flags above; edited via its own dropdown on the Member screen.</summary>
    public RpTransactionRole RpTransactionRole { get; set; } = RpTransactionRole.None;

    public bool CanBeImpersonated { get; set; }

    /// <summary>Optional link to the login account (Windows/AD identity or local account) that this member signs in as.</summary>
    public string? ApplicationUserId { get; set; }
    public ApplicationUser? ApplicationUser { get; set; }

    public List<Transaction> Transactions { get; set; } = [];

    /// <summary>Other members approved to impersonate THIS member (fill Related Party Register / COI on their behalf).</summary>
    public List<MemberImpersonationApproval> ApprovedImpersonators { get; set; } = [];
}
