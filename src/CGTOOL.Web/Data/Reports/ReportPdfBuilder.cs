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
    public static byte[] Build(ReportDocument report, string? logoFilePath)
    {
        using var document = new PdfDocument();
        document.Info.Title = report.Title;
        document.Info.Author = report.PreparedBy;

        var logo = logoFilePath is not null && File.Exists(logoFilePath) ? XImage.FromFile(logoFilePath) : null;

        var writer = new DeclarationPdfWriter(
            document,
            logo,
            report.Title,
            $"Confidential — for internal compliance use only. Prepared by {report.PreparedBy} on {report.GeneratedAt:dd MMM yyyy HH:mm}.",
            // Landscape, because these are wide tables. The Users Report alone runs to eight
            // columns, and portrait squeezes an email address into three lines.
            PageOrientation.Landscape);

        writer.NewPage();

        if (report.Subtitle is { Length: > 0 } subtitle) writer.Paragraph(subtitle);

        writer.StatBand([.. report.Stats.Select(s => (s.Label, s.Value, s.Sub))]);
        writer.FilterStatement($"Filters: {(report.FilterStatement is { Length: > 0 } f ? f : "none")}");

        // Widths are normalised rather than trusted: a definition whose fractions sum to 0.9 would
        // otherwise draw a table that stops short of the right margin for no visible reason.
        var total = report.Columns.Sum(c => c.Width);
        var fractions = report.Columns.Select(c => c.Width / total).ToArray();

        writer.DataTable([.. report.Columns.Select(c => c.Header)], fractions, report.Rows);

        writer.CloseCurrentPage();
        writer.FinishAllPages();

        using var stream = new MemoryStream();
        document.Save(stream, false);
        return stream.ToArray();
    }
}
