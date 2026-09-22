using System.ComponentModel.DataAnnotations;

namespace CGTOOL.Web.Data.Governance;

/// <summary>A member's own persistent "My Workspace &gt; My Family" list -- reference data the member
/// maintains once, independent of any specific declaration cycle (unlike InsiderDeclarationRelative/
/// CoiRelative, which are answers captured against one particular submission).</summary>
public class FamilyMember
{
    public int Id { get; set; }

    public int MemberId { get; set; }
    public Member? Member { get; set; }

    [Required, MaxLength(120)]
    public string Name { get; set; } = string.Empty;

    public RelativeRelationship Relationship { get; set; } = RelativeRelationship.Spouse;

    [MaxLength(260)]
    public string? EmiratesIdPath { get; set; }

    [MaxLength(260)]
    public string? PassportPath { get; set; }
}
