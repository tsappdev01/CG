using System.Reflection;
using CGTOOL.Web.Data.Reports;

namespace CGTOOL.Checks;

/// <summary>
/// A report is described once and rendered several ways, and ReportDocument says in as many words
/// that the renderings must agree. They had stopped: signatures moved into dbo.DocumentBlobs, the
/// PDF writer was taught to read the bytes, and the on-screen viewer was left reading a path that
/// is now null -- so every signature since showed as a broken image on screen while the PDF of
/// the same report drew it correctly.
///
/// These check the renderers against the two shapes a stored signature comes in.
/// </summary>
public static class ReportRenderingChecks
{
    public static void Run()
    {
        Check.Section("Report signatures");

        var screen = typeof(CGTOOL.Web.Components.Shared.Reports.ReportViewer)
            .GetMethod("SignatureSource", BindingFlags.NonPublic | BindingFlags.Static);

        Check.That("the on-screen viewer still has a signature source to check", screen is not null);
        if (screen is null) return;

        string? Render(ReportSignature s) => (string?)screen.Invoke(null, [s]);

        // How every signature written since they moved into the database looks: bytes, no path.
        var stored = new ReportSignature([1, 2, 3], null, "A Declarant", DateTime.UtcNow, "image/png");
        var rendered = Render(stored);
        Check.That("a signature stored as a blob renders from its bytes, not an empty src",
                   rendered is { Length: > 0 } && rendered.StartsWith("data:image/png;base64,", StringComparison.Ordinal));

        // The media type comes off the blob rather than being assumed.
        var jpeg = new ReportSignature([1, 2, 3], null, "A Declarant", DateTime.UtcNow, "image/jpeg");
        Check.That("the stored media type is honoured",
                   Render(jpeg)?.StartsWith("data:image/jpeg;base64,", StringComparison.Ordinal) == true);

        var untyped = new ReportSignature([1, 2, 3], null, "A Declarant", DateTime.UtcNow);
        Check.That("an unknown media type falls back to PNG rather than an unusable src",
                   Render(untyped)?.StartsWith("data:image/png;base64,", StringComparison.Ordinal) == true);

        // Anything signed before the move still has only a path.
        var legacy = new ReportSignature(null, "/uploads/signatures/7.png", "A Declarant", DateTime.UtcNow);
        Check.That("a signature written before the move still renders from its path",
                   Render(legacy) == "/uploads/signatures/7.png");

        Check.That("nothing to show renders nothing, rather than a broken image",
                   string.IsNullOrEmpty(Render(new ReportSignature(null, null, "A Declarant", DateTime.UtcNow))));
    }
}
