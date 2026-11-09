using System.Reflection;
using CGTOOL.Web.Data.Governance;
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

        // The Insider Trading form signs by who was signed in and when they pressed submit, and
        // never collects a drawing. That is a signature with no mark, not a missing mark.
        var byIdentity = new ReportSignature(null, null, "A Declarant", DateTime.UtcNow);
        Check.That("a declaration signed by identity renders no image, rather than a broken one",
                   string.IsNullOrEmpty(Render(byIdentity)));

        Check.Section("Report signatures: the insider report carries one");

        // Built for real rather than inspected: this is the record the report, the PDF and the
        // workbook are all rendered from, so if the signature is not on it, none of them show one.
        var signedAt = new DateTime(2026, 11, 8, 18, 18, 0, DateTimeKind.Utc);
        var record = InsiderDetailSheet.Record(
            new DeclarationSubmissionRow<InsiderDeclaration>
            {
                MemberId = 1,
                MemberName = "A Declarant",
                CompanyName = "An Entity",
                Year = 2026,
                Quarter = 1,
                Submitted = true,
                Declaration = new InsiderDeclaration { SubmittedAtUtc = signedAt },
            },
            "DIP Holding PLC");

        Check.That("the insider report record carries a signature at all", record.Signature is not null);
        Check.That("signed by the declarant", record.Signature?.Name == "A Declarant");
        Check.That("signed when they submitted", record.Signature?.SignedAtUtc == signedAt);
        Check.That("with no mark, because that form never collects one",
                   record.Signature is { Image: null, ImagePath: null });
        Check.That("so the screen draws no image for it",
                   string.IsNullOrEmpty(Render(record.Signature!)));
    }
    /// <summary>The Insider Detail sheet's own tables. The File cell used to print the file name
    /// out of the stored path, which since documents moved into the database is the blob's row id
    /// -- a reviewer was shown "25" and had to click it to learn what it was.</summary>
    public static void DetailTables()
    {
        Check.Section("Insider Detail: the File cell");

        var declaration = new InsiderDeclaration
        {
            EmiratesIdNumber = "784-1000-0000000-0",
            EmiratesIdExpiryDate = new DateTime(2028, 3, 9),
            EmiratesIdBlobId = 25,
        };

        // Reached by reflection rather than by widening the sheet's API: Documents is internal
        // because only the sheet builds it, and a check should not change what the code exposes.
        var build = typeof(InsiderDetailSheet).GetMethod("Documents", BindingFlags.NonPublic | BindingFlags.Static);
        Check.That("the documents table is still built in one place", build is not null);
        if (build is null) return;

        var documents = (ReportSubTable)build.Invoke(null, [declaration])!;

        Check.That("the one uploaded document is listed", documents.Rows.Count == 1);
        if (documents.Rows.Count == 1)
        {
            var cell = documents.Rows[0][documents.LinkColumn!.Value];
            Check.That("the cell is the action, not the blob's row id", cell == "View");
            Check.That("and it does not show a bare number", !int.TryParse(cell, out _));
        }

        Check.That("the cell is drawn as an icon on screen", documents.LinkAsIcon);
        Check.That("and still carries the link behind it",
                   documents.RowLinks is { Count: 1 } links && links[0] is { Length: > 0 });

        Check.Section("Insider Detail: Shareholding");

        // Dropped at the business's request.
        var sheet = typeof(InsiderDetailSheet);
        Check.That("the sheet still builds its tables", sheet is not null);

        var columns = documents.Columns.Select(c => c.Header).ToList();
        Check.That("the documents table keeps Document, Number, Expiry and File",
                   columns.SequenceEqual(new[] { "Document", "Number", "Expiry", "File" }));
    }

}
