using Microsoft.Extensions.FileProviders;

namespace CGTOOL.Web.Data.Governance;

/// <summary>Where the logo comes from.
///
/// The logo files ship inside the app, under wwwroot/images, so changing one has meant a rebuild
/// and a redeploy. Branding:ImageDirectory names a folder outside the app -- a local path or a UNC
/// share -- whose files take precedence over the bundled ones, file by file. Drop di-logo-en.png
/// in there and the sidebar picks it up when the app next starts; leave the setting empty and
/// everything behaves exactly as it did before.
///
/// Program.cs maps that folder onto /images AHEAD of wwwroot, so the markup goes on saying
/// &lt;img src="images/di-logo.jpg"&gt; and does not care where the bytes came from, and a file the
/// folder does not have still resolves to the copy that shipped with the build. This type is for
/// the two consumers that cannot go through a URL: the PDF writers, which read the file off disk,
/// and the e-mail templates, which need an absolute URL because the mail is read outside the app.
///
/// A UNC share is read by the IIS application pool identity, not by the signed-in user. If the
/// pool runs as ApplicationPoolIdentity it reaches the network as the machine account, so the
/// share has to grant read to DOMAIN\MACHINE$ -- or the pool has to run as an account that has it.</summary>
public sealed class BrandingImages(IConfiguration config, IWebHostEnvironment env)
{
    /// <summary>The override folder, or empty when the bundled images are in use.</summary>
    public string ImageDirectory => config["Branding:ImageDirectory"] ?? string.Empty;

    /// <summary>The site's own public address, for an &lt;img&gt; inside an e-mail. A relative path
    /// there would resolve against the mail client, which is not this app.</summary>
    public string PublicBaseUrl =>
        (config["Branding:PublicBaseUrl"] is { Length: > 0 } url ? url : "https://cg.dubaiinvestments.com")
        .TrimEnd('/');

    /// <summary>The file on disk for a PDF to draw, or null when there is none. Looks in the
    /// override folder first, then at the copy inside the app, and returns null rather than a path
    /// that is not there -- every caller already treats null as "draw the header without a logo".</summary>
    public string? FilePath(string fileName)
    {
        if (ImageDirectory is { Length: > 0 } dir)
        {
            var candidate = Path.Combine(dir, fileName);
            if (File.Exists(candidate)) return candidate;
        }

        var bundled = Path.Combine(env.WebRootPath, "images", fileName);
        return File.Exists(bundled) ? bundled : null;
    }

    /// <summary>An &lt;img&gt; tag for an e-mail, pointing at the public address of the same file the
    /// app serves at /images. Always the flat JPEG: e-mail clients are unreliable with PNG alpha,
    /// and this one is already flattened onto the navy of the band it sits on.</summary>
    public string EmailLogoTag(int height) =>
        $"<img src=\"{PublicBaseUrl}/images/di-logo.jpg\" alt=\"DIP Holding PLC\" height=\"{height}\" style=\"display:block;\" />";

    /// <summary>The provider Program.cs layers over wwwroot, or null when nothing is configured or
    /// the folder cannot be reached right now (a UNC share that is down, most likely).</summary>
    public static IFileProvider? OverrideProvider(string? directory)
        => !string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory)
            ? new PhysicalFileProvider(Path.GetFullPath(directory))
            : null;
}
