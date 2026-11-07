using System.ComponentModel.DataAnnotations;

namespace CGTOOL.Web.Data.Governance;

/// <summary>A supporting document attached to one Related Party and COI declaration.
///
/// Every document on this form lives here, including the Emirates ID and the passport. The insider
/// declaration keeps its three in columns of its own because its reports, its PDF and its
/// pre-check rules all read them by name and have since before the Document Center existed; this
/// form never had a documents step at all, so there is nothing to be bound by and no reason to
/// repeat the hybrid.
///
/// A copy, not a reference to the member's own file. A declaration is evidence of what was
/// provided on the day it was submitted -- if it pointed at My Workspace, replacing a document
/// there would quietly rewrite declarations already signed.</summary>
public class RelatedPartyCoiDeclarationDocument
{
    public int Id { get; set; }

    public int RelatedPartyCoiDeclarationId { get; set; }
    public RelatedPartyCoiDeclaration? RelatedPartyCoiDeclaration { get; set; }

    public int DocumentTypeId { get; set; }
    public DocumentType? DocumentType { get; set; }

    [MaxLength(260)]
    public string? FilePath { get; set; }

    [MaxLength(260)]
    public string? OriginalFileName { get; set; }

    [MaxLength(100)]
    public string? DocumentNumber { get; set; }

    public DateTime? ExpiryDate { get; set; }
}
