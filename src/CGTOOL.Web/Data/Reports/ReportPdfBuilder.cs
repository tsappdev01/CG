using CGTOOL.Web.Data.Governance;
using PdfSharp;
using PdfSharp.Drawing;
using PdfSharp.Pdf;

namespace CGTOOL.Web.Data.Reports;

/// <summary>A report as a PDF, drawn on the server rather than by asking the browser to print to
/// file. A report that leaves the building by email has to look the same for everyone who opens
/// it, and a browser's print-to-PDF stamps its own header, its own margins and whatever the
/// reader's page setup happens to say.
///
/// It draws through <see cref="DeclarationPdfWriter"/>, which already owns the navy/gold band, the
/// paginated data table and the "Page X of N" footer for the declaration PDFs -- so a report PDF
/// and a declaration PDF are recognisably documents from the same system, and pagination is not
/// solved a second time.</summary>
public static class ReportPdfBuilder
{
    public static byte[] Build(ReportDocument report, string? logoFilePath, string brandName)
    {
        using var document = new PdfDocument();
        document.Info.Title = report.Title;
        document.Info.Author = report.PreparedBy;

        var logo = logoFilePath is not null && File.Exists(logoFilePath) ? XImage.FromFile(logoFilePath) : null;

        var writer = new DeclarationPdfWriter(
            document,
            logo,
            brandName,
            report.Title,
            $"Confidential — for internal compliance use only. Prepared by {report.PreparedBy} on {report.GeneratedAt:dd MMM yyyy HH:mm}.",
            // Landscape, because these are wide tables. The Users Report alone runs to eight
            // columns, and portrait squeezes an email address into three lines.
            PageOrientation.Landscape);

        writer.NewPage();

        // A detail report's pages are each about one person, and the figures above them are about
        // the set: how many declared, how many hold a NIN. On screen that is a useful summary at
        // the top of a list. On a sheet handed to someone about one declarant it is a count of
        // other people, and it was costing the first of them room on their own page.
        if (!report.IsDetail)
        {
            if (report.Subtitle is { Length: > 0 } subtitle)
            {
                if (report.SubtitleTone == ReportTone.Warning) writer.WarningParagraph(subtitle);
                else writer.Paragraph(subtitle);
            }

            writer.StatBand([.. report.Stats.Select(s => (s.Label, s.Value, s.Sub))]);
        }

        // The filters stay on both kinds -- a sheet that does not say what it was filtered to
        // cannot be checked against anything -- but in different places. A table's filters head the
        // table. A detail report's go in the footer of every sheet, so the first declarant starts
        // at the top of page one rather than behind a line of preamble.
        var filters = $"Filters: {(report.FilterStatement is { Length: > 0 } f ? f : "none")}";
        if (report.IsDetail) writer.FilterNote(filters);
        else writer.FilterStatement(filters);

        if (report.IsDetail)
        {
            var first = true;
            foreach (var record in report.Records)
            {
                // Each person starts a page of their own: a record split across a page break is
                // harder to read than a short page. The exception is the first one, which joins
                // the cover when it fits -- a filter narrowed to a single declarant produced three
                // sheets, one of them a line of figures and the other two half empty.
                // The first one starts on the page already open; the rest each start a new one.
                if (!first) writer.NewPage();
                else if (Height(writer, record) > writer.Remaining) writer.NewPage();
                first = false;

                writer.BeginRecord(record.Title);
                writer.RecordHeading(record.Title, record.Subtitle);

                // Two columns: these are short answers, and one column of them ran a third of the
                // way down a landscape page that was twice as wide as it needed to be.
                if (record.Facts.Count > 0)
                    writer.KeyValueTable([.. record.Facts.Select(f => (f.Label, f.Value))], columns: 2);

                foreach (var table in record.Tables)
                {
                    writer.SectionHeading(table.Heading);
                    if (table.Rows.Count == 0)
                        writer.Paragraph(table.EmptyNote ?? "None.");
                    else
                        writer.DataTable([.. table.Columns.Select(c => c.Header)], Fractions(table.Columns), table.Rows);
                }

                DrawSignature(writer, record, logoFilePath);
            }
        }
        else
        {
            writer.DataTable([.. report.Columns.Select(c => c.Header)], Fractions(report.Columns), report.Rows);
        }

        writer.CloseCurrentPage();
        writer.FinishAllPages();

        using var stream = new MemoryStream();
        document.Save(stream, false);
        return stream.ToArray();
    }

    /// <summary>The signature under the record it signs, with the name and the time beneath it.
    ///
    /// A missing or unreadable file does not take the sheet down with it: the name and the time are
    /// still printed, and "signature image unavailable" is a truer thing to hand a reviewer than a
    /// report that failed to generate.</summary>
    private static void DrawSignature(DeclarationPdfWriter writer, ReportRecord record, string? logoFilePath)
    {
        if (record.Signature is not { } signature) return;

        writer.SectionHeading("Signature");

        // A declaration that carries no mark at all is not a declaration whose mark is missing.
        // The Insider Trading form is signed by who was signed in and when they pressed submit --
        // the form says so in as many words and never collects a drawing -- so "Signature image
        // unavailable" under it would report a fault that is not one.
        var carriesAMark = signature.Image is not null || signature.ImagePath is { Length: > 0 };

        var bytes = signature.Image;
        if (bytes is null && signature.ImagePath is { Length: > 0 } legacyPath)
        {
            // A declaration signed before signatures moved into the database still points at a file.
            var path = LocalPathOf(legacyPath, logoFilePath);
            if (path is not null && File.Exists(path)) bytes = File.ReadAllBytes(path);
        }

        if (bytes is { Length: > 0 })
        {
            try
            {
                using var stream = new MemoryStream(bytes, writable: false);
                using var image = XImage.FromStream(stream);
                writer.Signature(image, 180, 60);
            }
            catch (Exception)
            {
                writer.Paragraph("Signature image unavailable.");
            }
        }
        else if (carriesAMark)
        {
            writer.Paragraph("Signature image unavailable.");
        }

        writer.Paragraph(signature.Name is { Length: > 0 } name ? name : record.Title);
        writer.Paragraph(signature.SignedAtUtc is { } at
            ? $"Signed {at.ToLocalDisplay("dd/MM/yyyy HH:mm")}"
            : "Time not recorded");
    }

    /// <summary>Turns a web path like "/uploads/declarations/coi/ab12.png" into a file on this
    /// server, by walking back from the logo -- which the caller already resolved against
    /// wwwroot, and which is the only thing here that knows where wwwroot is.</summary>
    private static string? LocalPathOf(string webPath, string? logoFilePath)
    {
        if (logoFilePath is null) return null;

        // wwwroot/images/<logo> -> wwwroot
        var webRoot = Path.GetDirectoryName(Path.GetDirectoryName(logoFilePath));
        if (webRoot is null) return null;

        var relative = webPath.TrimStart('/', '\\').Replace('/', Path.DirectorySeparatorChar);
        var full = Path.GetFullPath(Path.Combine(webRoot, relative));

        // Stays inside wwwroot: the path comes from a database row, and a row that says "../.."
        // should not read a file off the server.
        return full.StartsWith(Path.GetFullPath(webRoot) + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            ? full
            : null;
    }

    /// <summary>How tall a record will be, measured before anything is drawn, so the decision to
    /// break a page is made with the answer in hand rather than discovered halfway down one.
    ///
    /// It mirrors the drawing below it step for step. The heights themselves come from the writer,
    /// which measures with the same fonts and wrapping it draws with -- a second opinion about how
    /// tall a row is would be a page break predicted in the wrong place.</summary>
    private static double Height(DeclarationPdfWriter writer, ReportRecord record)
    {
        var height = writer.MeasureSectionHeading();

        if (record.Facts.Count > 0)
            height += writer.MeasureKeyValueTable([.. record.Facts.Select(f => (f.Label, f.Value))], columns: 2);

        foreach (var table in record.Tables)
        {
            height += writer.MeasureSectionHeading();
            height += table.Rows.Count == 0
                ? writer.MeasureParagraph(table.EmptyNote ?? "None.")
                : writer.MeasureDataTable([.. table.Columns.Select(c => c.Header)], Fractions(table.Columns), [.. table.Rows]);
        }

        if (record.Signature is { } signature)
        {
            // Heading, the image at its drawn height where there is one, then the name and the
            // time. A declaration signed by identity reserves no space for a drawing.
            var mark = signature.Image is not null || signature.ImagePath is { Length: > 0 } ? 60 + 13 : 0;
            height += writer.MeasureSectionHeading() + mark + (2 * writer.MeasureParagraph("Signed 00/00/0000 00:00"));
        }

        return height;
    }

    /// <summary>Column widths normalised rather than trusted: a definition whose fractions sum to
    /// 0.9 would otherwise draw a table that stops short of the right margin for no visible
    /// reason.</summary>
    private static double[] Fractions(IReadOnlyList<ReportColumn> columns)
    {
        var total = columns.Sum(c => c.Width);
        return total <= 0
            ? [.. Enumerable.Repeat(1.0 / Math.Max(1, columns.Count), columns.Count)]
            : [.. columns.Select(c => c.Width / total)];
    }
}
