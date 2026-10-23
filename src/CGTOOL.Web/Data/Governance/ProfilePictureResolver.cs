namespace CGTOOL.Web.Data.Governance;

/// <summary>Turns a stored profile-picture path into one that will actually load, or null.
///
/// The path is in the database but the file is under wwwroot/uploads, which is gitignored and
/// is not carried by a build or a deployment. Any machine pointed at a CGS database that another
/// machine wrote -- a fresh clone against the shared UAT database, a restored backup, a second
/// web server -- therefore holds paths whose files are not on it, and the browser draws its
/// broken-image icon where a face should be. Returning null for a file that is not there lets the
/// caller fall back to the initial-letter placeholder it already shows for an account that never
/// had a picture, which is both correct and the thing a reader can actually use.</summary>
public sealed class ProfilePictureResolver(IWebHostEnvironment environment)
{
    public string? Resolve(string? storedPath)
    {
        if (string.IsNullOrWhiteSpace(storedPath)) return null;

        // Stored as "/uploads/profile-pictures/<file>". MemberDetail appends a "?v=<ticks>"
        // cache-buster after an upload, so the query has to come off before touching disk.
        var relative = storedPath.Split('?', 2)[0].TrimStart('/', '\\');
        if (relative.Length == 0 || relative.Contains("..")) return null;

        var full = Path.Combine(environment.WebRootPath, relative.Replace('/', Path.DirectorySeparatorChar));
        return File.Exists(full) ? storedPath : null;
    }
}
