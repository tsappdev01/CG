using System.Reflection;

namespace CGTOOL.Web.Data.Governance;

/// <summary>Appends a build stamp to the stylesheets and scripts in the page head, so a deployed
/// change to app.css actually reaches a browser that already has the old one.
///
/// Without it the browser is entitled to keep serving the cached copy, and a new page renders against
/// last week's CSS -- which has looked like "the styling is broken" more than once. The stamp is the
/// application assembly's write time, so it changes exactly when a new build is deployed and not on
/// every request (which would defeat caching entirely).</summary>
public static class AssetVersion
{
    private static readonly string Stamp = Resolve();

    public static string Url(string path) => $"{path}?v={Stamp}";

    private static string Resolve()
    {
        try
        {
            var assembly = Assembly.GetEntryAssembly()?.Location;
            if (!string.IsNullOrEmpty(assembly) && File.Exists(assembly))
            {
                return File.GetLastWriteTimeUtc(assembly).Ticks.ToString();
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }

        // Single-file or trimmed publishes have no assembly on disk to stamp; falling back to the
        // process start time still busts the cache once per restart.
        return Environment.TickCount64.ToString();
    }
}
