namespace CGTOOL.Web.Data.Governance;

/// <summary>What one declaration asks of one document.</summary>
public enum DocumentRequirement
{
    /// <summary>Not part of this declaration at all. The card is not drawn and nothing is asked
    /// for -- which is what makes the two declarations genuinely different sets rather than the
    /// same list with different asterisks.</summary>
    NotRequested = 0,

    /// <summary>Offered, and accepted if supplied, but the declaration can be submitted without
    /// it.</summary>
    Optional = 1,

    /// <summary>Must be attached before the declaration can be submitted.</summary>
    Required = 2,
}

/// <summary>A document's standing in one declaration: Emirates ID required on the insider
/// declaration, Emirates ID and passport required on the RP and COI one, and so on.
///
/// A row per (document, declaration) rather than two columns on DocumentType. The declarations are
/// a list in DeclarationType, not a fixed pair -- a third one would be a row here and a column
/// there, and a column per declaration is a schema change every time the business adds a form.
///
/// A document with no row for a declaration is NotRequested for it. That is the same thing the
/// enum's zero says, so a missing row and an explicit "no" behave alike and neither has to be
/// backfilled.</summary>
public class DocumentTypeRequirement
{
    public int Id { get; set; }

    public int DocumentTypeId { get; set; }
    public DocumentType? DocumentType { get; set; }

    public DeclarationType Declaration { get; set; }

    public DocumentRequirement Requirement { get; set; }
}
