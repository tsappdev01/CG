using System.Reflection;

namespace CGTOOL.Web.Data.Governance;

/// <summary>The build this site is running, shown at the foot of the nav menu.
///
/// Read from the assembly's informational version, which the pipeline stamps with its own build
/// number -- so what is on screen is the build that produced the files on disk, and cannot drift
/// from it the way a hand-edited constant does. The format is the pipeline's `name:`, currently
/// "ddMMyyyy v N", where N restarts each day.
///
/// A local build has no stamp and says so rather than inventing a number: "Local build" is the
/// truth, and a developer who sees a date and a counter on their own machine will believe it.</summary>
public class BuildVersion
{
    public const string LocalBuildLabel = "Local build";

    public string Value { get; } = Read();

    private static string Read()
    {
        var informational = Assembly.GetEntryAssembly()
            ?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion;

        if (string.IsNullOrWhiteSpace(informational)) return LocalBuildLabel;

        // The SDK appends "+<commit sha>" when the build has a source revision, which is useful in
        // a log and noise in a sidebar.
        var plus = informational.IndexOf('+');
        var version = (plus >= 0 ? informational[..plus] : informational).Trim();

        // An unstamped build still carries the project's own version, which is 1.0.0 and says
        // nothing. Anything the pipeline set is longer and holds a space, because its name is
        // "ddMMyyyy v N".
        return version.Length == 0 || version == "1.0.0" ? LocalBuildLabel : version;
    }
}
