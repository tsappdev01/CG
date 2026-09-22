using System.ComponentModel.DataAnnotations;

namespace CGTOOL.Web.Data.Governance;

/// <summary>A member's own persistent "My Workspace &gt; My Companies" list -- reference data the member
/// maintains once (companies they or their family own/hold shares in), independent of any specific
/// declaration cycle.</summary>
public class OwnedCompany
{
    public int Id { get; set; }

    public int MemberId { get; set; }
    public Member? Member { get; set; }

    [Required, MaxLength(160)]
    public string CompanyName { get; set; } = string.Empty;

    [MaxLength(400)]
    public string? TradeLicenseDetails { get; set; }

    /// <summary>Ownership percentage (0-100).</summary>
    public decimal? OwnershipPercentage { get; set; }

    [MaxLength(260)]
    public string? TradeLicensePath { get; set; }

    [MaxLength(260)]
    public string? MoaPath { get; set; }

    /// <summary>Power of Attorney -- optional, unlike the trade license/MOA.</summary>
    [MaxLength(260)]
    public string? PoaPath { get; set; }
}
