using System.ComponentModel.DataAnnotations;

namespace CGTOOL.Web.Data.Governance;

/// <summary>A kind of document a person keeps on file: what it is called, whether they must
/// provide one, and whether it is being asked for at all.
///
/// This is reference data, not code. The list used to be a static array in My Workspace and a run
/// of hardcoded "Required"/"Optional" pills in the declaration -- two places that could disagree,
/// and neither changeable without a rebuild and a redeploy. Adding a visa to the list was a
/// developer task. Now it is a row.
///
/// The three documents the app reads automatically keep their <see cref="MemberDocumentKind"/> in
/// <see cref="BuiltInKind"/>. That is what ties a row here to the per-kind capture wiring, the
/// declaration's own Emirates ID / passport / trade licence columns, and every report that asks
/// for a passport by name. They can be renamed, made optional, or switched off, but they cannot be
/// deleted and no new row can claim a built-in kind -- the code behind them is specific to the
/// document, so a second "Passport" row would be a name with nothing behind it.
///
/// Rows added by an administrator have no kind. They take a file, a number and an expiry date, and
/// nothing is read off them: Document Intelligence is trained per document, so a type invented
/// this afternoon has no model to read it with. Saying so in the UI is kinder than appearing to
/// try and then returning empty fields.</summary>
public class DocumentType
{
    public int Id { get; set; }

    [MaxLength(120)]
    public string Name { get; set; } = string.Empty;

    /// <summary>The line under the name on the upload card, saying what is wanted. Optional: a
    /// type whose name says enough does not need one.</summary>
    [MaxLength(300)]
    public string? Description { get; set; }

    /// <summary>What each declaration asks of this document -- see DocumentTypeRequirement. This
    /// was a single IsMandatory flag, which could not say that the insider declaration wants an
    /// Emirates ID while the RP and COI one wants an Emirates ID and a passport.</summary>
    public List<DocumentTypeRequirement> Requirements { get; set; } = [];

    /// <summary>Set for the documents the application has specific code for, null for everything an
    /// administrator adds. Not editable from the Document Centre -- see the class note.</summary>
    public MemberDocumentKind? BuiltInKind { get; set; }

    /// <summary>True for a document type that is being asked for. An inactive one disappears from
    /// My Workspace and from the declaration, but is kept, along with everything already uploaded
    /// against it -- which is why switching off is offered instead of deleting for anything a
    /// person may already have filed.</summary>
    public bool IsActive { get; set; } = true;

    public int SortOrder { get; set; }

    /// <summary>A built-in cannot be deleted or have its kind reassigned.</summary>
    public bool IsBuiltIn => BuiltInKind is not null;

    /// <summary>What one declaration asks of this document. A document with no row for a
    /// declaration is not part of it, which is the same thing the enum's zero says -- so nothing
    /// has to be backfilled when a declaration is added.
    ///
    /// Requires Requirements to have been loaded. Every caller includes it; the alternative is a
    /// lazy load per row inside a render loop.</summary>
    public DocumentRequirement RequirementFor(DeclarationType declaration) =>
        Requirements.FirstOrDefault(r => r.Declaration == declaration)?.Requirement
        ?? DocumentRequirement.NotRequested;

    /// <summary>Whether any declaration insists on this document. My Workspace marks a card with
    /// it: the workspace is not a declaration, so "required" there can only mean "something will
    /// ask you for this", and which declarations those are goes in the tooltip.</summary>
    public bool IsRequiredByAnyDeclaration =>
        Requirements.Any(r => r.Requirement == DocumentRequirement.Required);
}
