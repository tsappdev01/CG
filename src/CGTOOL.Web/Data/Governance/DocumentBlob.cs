using System.ComponentModel.DataAnnotations;

namespace CGTOOL.Web.Data.Governance;

/// <summary>
/// The bytes of an uploaded document, held in the database rather than on the web server's disk.
///
/// Column names and types follow the IPO system's dbo.EmiratesIdDocuments so the two systems read
/// the same way in SSMS. One difference: that system has a blob table per document kind, and this
/// is a single table every kind points at. The reason is EF Core rather than taste -- a
/// varbinary(max) column on MemberDocuments would be loaded by "db.MemberDocuments.ToListAsync()",
/// which My Workspace runs on every page load, so listing a member's documents would pull every
/// scan they have ever uploaded into memory. Keeping the bytes in their own table makes that
/// impossible rather than something each new query has to remember. It also means the rule about
/// who may read a document is written once (see DocumentEndpoints) instead of once per kind.
/// </summary>
public class DocumentBlob
{
    public int Id { get; set; }

    /// <summary>The name the user uploaded, kept for the download's Content-Disposition.</summary>
    [MaxLength(255)]
    public string FileName { get; set; } = string.Empty;

    [MaxLength(100)]
    public string ContentType { get; set; } = string.Empty;

    /// <summary>Length of the original file, before protection -- so a size can be shown, and a
    /// download checked, without reading the bytes back.</summary>
    public long SizeBytes { get; set; }

    /// <summary>The file. Whether this is the plain bytes or an encrypted form of them is
    /// <see cref="ProtectionScheme"/>; read it through <see cref="IDocumentProtector"/> rather than
    /// using it directly.</summary>
    public byte[] ContentProtected { get; set; } = [];

    /// <summary>How <see cref="ContentProtected"/> is encoded: "None" for the file as uploaded,
    /// "DataProtection:v1" for ASP.NET Data Protection.
    ///
    /// This column is not in the IPO table and is the one addition made on purpose. Without it the
    /// bytes are self-describing only by guesswork, so encryption could never be switched on for a
    /// database that already holds documents -- old rows would be unreadable and new rows
    /// indistinguishable from them. With it, the two can coexist and a re-encryption pass is a
    /// background job rather than a migration that cannot fail safely.</summary>
    [MaxLength(20)]
    public string ProtectionScheme { get; set; } = DocumentProtection.None;

    /// <summary>SHA-256 of the original file as lowercase hex, so a stored document can be shown to
    /// be the one that was uploaded. Taken before protection, so it stays comparable if the
    /// protection scheme changes.</summary>
    [MaxLength(64)]
    public string? ContentSha256 { get; set; }

    /// <summary>Where the upload came from, matching what the audit log records for the same
    /// action.</summary>
    [MaxLength(64)]
    public string? IpAddress { get; set; }

    public DateTime UploadedAtUtc { get; set; } = DateTime.UtcNow;
}
