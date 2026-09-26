using System.ComponentModel.DataAnnotations;

namespace CGTOOL.Web.Data.Governance;

/// <summary>Which of the member's own documents this is. The first three are singletons -- a person
/// has one Emirates ID -- so uploading again replaces what is on file. Other is a list: a visa, a
/// degree certificate, a power of attorney, whatever else is asked for.</summary>
public enum MemberDocumentKind { EmiratesId, Passport, TradeLicence, Other }

/// <summary>A document the member keeps on their own file in My Workspace, with the details read off
/// it. Separate from the copies attached to a declaration: those are the evidence for one
/// submission and are frozen with it, while these are the member's standing record, uploaded once
/// and reused -- which is also why the declaration can pre-fill from them.
///
/// One table with per-kind fields left null rather than four tables: they are read and shown
/// together, every kind carries a number and an expiry, and the alternative is three joins to
/// answer "what has this member got on file".</summary>
public class MemberDocument
{
    public int Id { get; set; }

    public int MemberId { get; set; }
    public Member? Member { get; set; }

    public MemberDocumentKind Kind { get; set; }

    /// <summary>What an Other document is. Ignored for the three named kinds, which title
    /// themselves.</summary>
    [MaxLength(120)]
    public string? Title { get; set; }

    [MaxLength(260)]
    public string? FilePath { get; set; }

    /// <summary>The name the member uploaded, kept so the file can be recognised after the stored
    /// name has been rewritten to the record's id.</summary>
    [MaxLength(260)]
    public string? OriginalFileName { get; set; }

    /// <summary>Emirates ID number, passport number or trade licence number, by kind.</summary>
    [MaxLength(100)]
    public string? DocumentNumber { get; set; }

    /// <summary>Name on the ID, or the legal name on a trade licence.</summary>
    [MaxLength(200)]
    public string? HolderName { get; set; }

    [MaxLength(80)]
    public string? Nationality { get; set; }

    public DateTime? ExpiryDate { get; set; }

    public DateTime? IssueDate { get; set; }

    public DateTime? DateOfBirth { get; set; }

    public DateTime UploadedAtUtc { get; set; } = DateTime.UtcNow;
}
