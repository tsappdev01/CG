namespace CGTOOL.Web.Data.Governance;

/// <summary>
/// Where the corporate policy document lives, and how to link to it.
///
/// The file is always at the same path, which is what makes it linkable from anywhere -- and also
/// what made a replacement invisible: a browser that had already fetched /assets/policy.pdf kept
/// serving its cached copy, so "View current policy document" opened the previous one. Every link
/// therefore carries the file's last-write time, which changes the URL whenever the document does
/// and leaves it cacheable in between.
///
/// Anything linking to the policy should use <see cref="CurrentUrl"/> rather than writing the path
/// out, or it will have the same problem.
/// </summary>
public static class PolicyDocumentLink
{
    public const string RelativePath = "assets/policy.pdf";
    public const string BackupRelativeDir = "assets/policy-backups";

    /// <summary>The URL to link to, or null when nothing has been uploaded yet.</summary>
    public static string? CurrentUrl(IWebHostEnvironment environment)
    {
        var path = Path.Combine(environment.WebRootPath, RelativePath);
        if (!File.Exists(path)) return null;

        return $"/{RelativePath}?v={File.GetLastWriteTimeUtc(path).Ticks}";
    }

    public static bool Exists(IWebHostEnvironment environment) =>
        File.Exists(Path.Combine(environment.WebRootPath, RelativePath));
}
