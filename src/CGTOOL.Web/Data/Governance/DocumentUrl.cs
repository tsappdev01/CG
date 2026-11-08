namespace CGTOOL.Web.Data.Governance;

/// <summary>Where to point an &lt;img&gt;, an &lt;iframe&gt; or a download link at a stored document.
///
/// Documents used to be files under wwwroot and are now rows in dbo.DocumentBlobs, and for a while
/// both are true: a record uploaded before the change still has only its old path, and one uploaded
/// after has only a blob. Every render goes through here so that is decided in one place rather
/// than in each of the fifteen components that show a document.
///
/// The blob wins when both are set. A backfilled record keeps its path column until the paths are
/// dropped, and the database copy is the one that is going to survive.</summary>
public static class DocumentUrl
{
    /// <summary>The URL for a document, or null when there is no document.</summary>
    public static string? For(int? blobId, string? legacyPath) =>
        blobId is int id ? $"/documents/{id}"
        : string.IsNullOrWhiteSpace(legacyPath) ? null
        : legacyPath;

    /// <summary>True when there is something to show.</summary>
    public static bool Exists(int? blobId, string? legacyPath) =>
        blobId is not null || !string.IsNullOrWhiteSpace(legacyPath);
}
