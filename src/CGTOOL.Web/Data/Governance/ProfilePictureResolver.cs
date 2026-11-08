namespace CGTOOL.Web.Data.Governance;

/// <summary>Turns a stored profile picture into an address that will actually load, or null.
///
/// This type exists because of a problem that only applied to pictures kept as files: the path was
/// in the database but the file was under wwwroot/uploads, which is gitignored and is not carried
/// by a build or a deployment. Any machine pointed at a CGS database that another machine wrote --
/// a fresh clone against the shared UAT database, a restored backup, a second web server --
/// therefore held paths whose files were not on it, and the browser drew its broken-image icon
/// where a face should be. Checking the file and returning null let the caller fall back to the
/// initial-letter placeholder it already shows for an account that never had a picture.
///
/// A picture stored in dbo.DocumentBlobs cannot go missing that way: it travels with the database
/// it is referenced from. So the check applies only to the older, file-backed pictures, and
/// disappears with them once the paths are dropped.</summary>
public sealed class ProfilePictureResolver(IWebHostEnvironment environment)
{
    public string? Resolve(int? blobId, string? storedPath)
    {
        if (blobId is int id) return DocumentUrl.For(id, null);
        if (string.IsNullOrWhiteSpace(storedPath)) return null;

        // Stored as "/uploads/profile-pictures/<file>", possibly with a "?v=<ticks>" cache-buster
        // appended after an upload, so the query has to come off before touching disk.
        var relative = storedPath.Split('?', 2)[0].TrimStart('/', '\\');
        if (relative.Length == 0 || relative.Contains("..")) return null;

        var full = Path.Combine(environment.WebRootPath, relative.Replace('/', Path.DirectorySeparatorChar));
        return File.Exists(full) ? storedPath : null;
    }
}
