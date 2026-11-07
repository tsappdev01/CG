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

        if (report.Subtitle is { Length: > 0 } subtitle)
        {
            if (report.SubtitleTone == ReportTone.Warning) writer.WarningParagraph(subtitle);
            else writer.Paragraph(subtitle);
        }

        writer.StatBand([.. report.Stats.Select(s => (s.Label, s.Value, s.Sub))]);
        writer.FilterStatement($"Filters: {(report.FilterStatement is { Length: > 0 } f ? f : "none")}");

        if (report.IsDetail)
        {
            var first = true;
            foreach (var record in report.Records)
            {
                // Each person starts a page of their own: a record split across a page break is
                // harder to read than a short page. The exception is the first one, which joins
                // the cover when it fits -- a filter narrowed to a single declarant produced three
                // sheets, one of them a line of figures and the other two half empty.
                if (!first || Height(writer, record) > writer.Remaining) writer.NewPage();
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
