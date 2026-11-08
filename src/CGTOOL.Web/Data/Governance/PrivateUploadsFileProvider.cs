using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Primitives;

namespace CGTOOL.Web.Data.Governance;

/// <summary>
/// The public half of wwwroot: everything the static-file middleware would serve, minus anything
/// that resolves inside <c>wwwroot/uploads</c>.
///
/// The obvious way to keep uploads private is to test the request path for a "/uploads" prefix and
/// refuse it. That does not hold: the static-file middleware and a hand-written prefix test
/// normalise paths differently, so "//uploads/42-emiratesid.pdf" walks past the test and is still
/// served (verified -- it returned 200 to an anonymous caller before this type existed). Windows
/// adds more of the same, stripping trailing dots and reading '\' as a separator.
///
/// So the decision is made on the file that was actually resolved, not on the text of the request:
/// whatever path trick got here, if the answer lives under the uploads folder, this provider says
/// the file does not exist. Serving those files to signed-in users is a second, separate
/// UseStaticFiles rooted at that folder (see Program.cs).
/// </summary>
public sealed class PrivateUploadsFileProvider(IFileProvider inner, string privateRoot) : IFileProvider
{
    private readonly string _privateRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(privateRoot))
        + Path.DirectorySeparatorChar;

    public IFileInfo GetFileInfo(string subpath)
    {
        var file = inner.GetFileInfo(subpath);
        return IsPrivate(file.PhysicalPath) ? new NotFoundFileInfo(subpath) : file;
    }

    public IDirectoryContents GetDirectoryContents(string subpath)
    {
        var contents = inner.GetDirectoryContents(subpath);
        return contents.Any(e => IsPrivate(e.PhysicalPath)) ? NotFoundDirectoryContents.Singleton : contents;
    }

    public IChangeToken Watch(string filter) => inner.Watch(filter);

    /// <summary>True when the resolved file sits inside the private folder. Case-insensitive
    /// because the deployment target is Windows, where it is the same file either way.</summary>
    public bool IsPrivate(string? physicalPath) =>
        physicalPath is { Length: > 0 }
        && Path.GetFullPath(physicalPath).StartsWith(_privateRoot, StringComparison.OrdinalIgnoreCase);
}
