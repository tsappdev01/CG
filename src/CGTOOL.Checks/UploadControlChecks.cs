using System.Text.RegularExpressions;

namespace CGTOOL.Checks;

/// <summary>
/// A file input nested inside a &lt;label&gt; is activated by a click anywhere in that label.
/// On the Related Party &amp; COI declaration the label wrapped the caption, the progress bar and
/// the list of documents already uploaded, so clicking any of them opened the file picker --
/// including clicking a document's own link to read it.
///
/// This walks the markup rather than the built assembly: the defect is in the HTML, and the
/// compiler has nothing to say about it.
/// </summary>
public static class UploadControlChecks
{
    public static void Run()
    {
        Check.Section("A file picker opens only when asked");

        var root = FindRepositoryRoot();
        if (root is null)
        {
            Check.That("the markup can be read from here", false);
            return;
        }

        var components = Path.Combine(root, "src", "CGTOOL.Web");
        var offenders = new List<string>();
        var scanned = 0;

        foreach (var file in Directory.EnumerateFiles(components, "*.razor", SearchOption.AllDirectories))
        {
            scanned++;
            var depth = 0;
            var openedAt = 0;
            var line = 0;

            foreach (var text in File.ReadLines(file))
            {
                line++;
                foreach (var _ in Regex.Matches(text, @"<label\b")) { depth++; openedAt = line; }
                depth -= Count(text, "</label>");
                if (depth < 0) depth = 0;

                if (text.Contains("<InputFile", StringComparison.Ordinal) && depth > 0)
                {
                    offenders.Add($"{Path.GetFileName(file)}:{line} (label opened at {openedAt})");
                }
            }
        }

        Check.That("there is markup to check", scanned > 0);
        Check.That(offenders.Count == 0
                       ? "no file input sits inside a label, so nothing else can open the picker"
                       : $"these would open the picker on any click: {string.Join("; ", offenders)}",
                   offenders.Count == 0);
    }

    private static int Count(string text, string needle)
    {
        var n = 0;
        var i = text.IndexOf(needle, StringComparison.Ordinal);
        while (i >= 0)
        {
            n++;
            i = text.IndexOf(needle, i + needle.Length, StringComparison.Ordinal);
        }
        return n;
    }

    /// <summary>Walks up from the running assembly until it finds the solution file. The checks
    /// run from bin/, which is three or four levels down depending on the build.</summary>
    private static string? FindRepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "src", "CGTOOL.Web"))) return dir.FullName;
            dir = dir.Parent;
        }
        return null;
    }
}
