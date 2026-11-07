using System.ComponentModel.DataAnnotations;

namespace CGTOOL.Web.Data.Governance;

/// <summary>A document attached to one insider declaration against a Document Centre type an
/// administrator added.
///
/// The three built-in documents have their own columns on <see cref="InsiderDeclaration"/> --
/// EmiratesIdPath, PassportPath, TradeLicencePath -- and keep them: the reports, the PDF and the
/// pre-check rules all read those by name, and moving them here to be tidy would rewrite far more
/// than it would clean up. This table is for everything else, which cannot have columns because
/// nobody knows yet what it will be.
///
/// A copy, not a reference to the member's own file. A declaration is evidence of what was
/// provided on the day it was submitted -- if it pointed at My Register, replacing a document
/// there would quietly rewrite declarations already signed.</summary>
public class InsiderDeclarationDocument
{
    public int Id { get; set; }

    public int InsiderDeclarationId { get; set; }
    public InsiderDeclaration? InsiderDeclaration { get; set; }

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
