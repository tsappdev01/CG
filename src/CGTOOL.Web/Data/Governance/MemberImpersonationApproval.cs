namespace CGTOOL.Web.Data.Governance;

/// <summary>Grants ImpersonatorId permission to act as MemberId when filling Related Party Register / Conflict of Interest declarations.</summary>
public class MemberImpersonationApproval
{
    public int Id { get; set; }

    public int MemberId { get; set; }
    public Member? Member { get; set; }

    public int ImpersonatorId { get; set; }
    public Member? Impersonator { get; set; }
}
