using PdfSharp.Drawing;
using PdfSharp.Pdf;

namespace CGTOOL.Web.Data.Governance;

/// <summary>Renders a submitted Insider Trading declaration as a corporate-branded PDF (DI navy/gold,
/// matching WelcomeEmailTemplate's email styling and the My Declarations print preview) for attachment
/// to the post-submission confirmation email. SQL Server Database Mail's sp_send_dbmail has no
/// "attach these bytes" parameter, so the caller (SqlDbMailSender.SendWithFileAttachmentAsync) writes
/// the returned bytes to a shared folder first and references that path via @file_attachments.</summary>
public static class InsiderDeclarationPdfBuilder
{
    private static readonly XColor Navy = XColor.FromArgb(14, 42, 71);
    private static readonly XColor Gold = XColor.FromArgb(217, 182, 90);
    private static readonly XColor LightGrey = XColor.FromArgb(245, 246, 248);
    private static readonly XColor RowStripe = XColor.FromArgb(250, 250, 251);
    private static readonly XColor TextDark = XColor.FromArgb(27, 36, 48);
    private static readonly XColor Muted = XColor.FromArgb(137, 147, 163);
    private static readonly XColor BorderGrey = XColor.FromArgb(226, 229, 234);

    private const double Margin = 40;
    private const double HeaderHeight = 82;
    private const double FooterHeight = 46;

    public static byte[] Build(InsiderDeclaration d, Member member, DeclarationCycleRun run, string? logoFilePath)
    {
        using var document = new PdfDocument();
        var logo = logoFilePath is not null && File.Exists(logoFilePath) ? XImage.FromFile(logoFilePath) : null;
        var writer = new Writer(document, logo);

        writer.NewPage();

        writer.SectionHeading("Declarant");
        writer.KeyValueTable(
        [
            ("Name", member.FullName),
            ("Entity", member.Company?.Name ?? "—"),
            ("Period", $"Q{run.PeriodQuarter} {run.PeriodYear}"),
            ("Status", d.IsDraft ? "Draft" : "Submitted"),
            ("Submitted", d.IsDraft ? "—" : d.SubmittedAtUtc.ToLocalDisplay("dd MMM yyyy HH:mm")),
            ("Last modified", d.ModifiedAtUtc?.ToLocalDisplay("dd MMM yyyy HH:mm") ?? "—"),
        ]);

        writer.Gap(10);
        writer.SectionHeading("Declaration Summary");
        writer.KeyValueTable(
        [
            ("Holds a NIN", d.HasNin ? $"Yes ({d.NinNumber})" : "No"),
            ("Relatives have a NIN", d.RelativesHaveNin ? "Yes" : "No"),
            ("Holds shares in DI (self or relatives)", d.HoldsShares ? "Yes" : "No"),
            ("Emirates ID", string.IsNullOrEmpty(d.EmiratesIdPath) ? "—" : $"{d.EmiratesIdNumber} — {d.EmiratesIdNameOnCard}, expires {d.EmiratesIdExpiryDate:dd MMM yyyy}"),
            ("Passport", string.IsNullOrEmpty(d.PassportPath) ? "—" : $"{d.PassportNumber} ({d.PassportIssuingCountry}), expires {d.PassportExpiryDate:dd MMM yyyy}"),
            .. string.IsNullOrEmpty(d.TradeLicencePath)
                ? Array.Empty<(string, string)>()
                : [("Trade Licence", $"{d.TradeLicenceNumber} — {d.TradeLicenceLegalName}" +
                    (d.TradeLicenceExpiryDate is null ? "" : $", expires {d.TradeLicenceExpiryDate:dd MMM yyyy}"))],
        ]);

        if (d.RelativesHaveNin && d.NinHolders.Count > 0)
        {
            writer.Gap(14);
            writer.SectionHeading("Relatives' NIN");
            writer.DataTable(
                ["Share Held By", "Name of Share Holder", "NIN", "Additional"],
                [0.20, 0.30, 0.22, 0.28],
                d.NinHolders.Select(h => new[] { RelationshipLabel(h.Relationship), h.NameOfShareHolder, h.NinNumber, h.Additional ?? "" }));
        }

        if (d.HoldsShares && d.Relatives.Count > 0)
        {
            writer.Gap(14);
            writer.SectionHeading("Shareholding");
            writer.DataTable(
                ["Share Held By", "Name of Share Holder", "NIN", "Additional"],
                [0.20, 0.30, 0.22, 0.28],
                d.Relatives.Select(r => new[]
                {
                    r.IsSelf ? "Self" : RelationshipLabel(r.Relationship),
                    r.IsSelf ? member.FullName : r.RelativeName,
                    r.NinNumber ?? "",
                    r.Additional ?? "",
                }));
        }

        writer.CloseCurrentPage();
        writer.FinishAllPages();

        using var ms = new MemoryStream();
        document.Save(ms);
        return ms.ToArray();
    }

    private static string RelationshipLabel(RelativeRelationship relationship) => relationship switch
    {
        RelativeRelationship.InLaws => "In-Laws",
        RelativeRelationship.FatherInLaw => "Father-in-Law",
        RelativeRelationship.MotherInLaw => "Mother-in-Law",
        _ => relationship.ToString(),
    };

    /// <summary>Owns pagination: tracks the current page/graphics and vertical cursor, starting a new
    /// page whenever content would run into the reserved footer band. Headers/footers are stamped on
    /// every page via FinishAllPages once the total page count is known, rather than while each page is
    /// still being written to.</summary>
    private class Writer(PdfDocument document, XImage? logo)
    {
        private readonly XFont _title = new("DejaVu Sans", 15, XFontStyleEx.Bold);
        private readonly XFont _subtitle = new("DejaVu Sans", 9.5, XFontStyleEx.Regular);
        private readonly XFont _sectionHeading = new("DejaVu Sans", 11.5, XFontStyleEx.Bold);
        private readonly XFont _label = new("DejaVu Sans", 9, XFontStyleEx.Bold);
        private readonly XFont _value = new("DejaVu Sans", 9, XFontStyleEx.Regular);
        private readonly XFont _tableHeader = new("DejaVu Sans", 8.5, XFontStyleEx.Bold);
        private readonly XFont _tableCell = new("DejaVu Sans", 8.5, XFontStyleEx.Regular);
        private readonly XFont _footer = new("DejaVu Sans", 7, XFontStyleEx.Regular);

        private double _pageWidth;
        private double _pageHeight;
        private double _contentWidth;
        private double _y;
        private XGraphics _gfx = null!;

        public void NewPage()
        {
            // Only one XGraphics may exist per PdfPage at a time -- FinishAllPages later reopens each
            // page in Append mode to stamp the header/footer, which throws unless the content pass's
            // XGraphics has already been disposed.
            _gfx?.Dispose();

            var page = document.AddPage();
            page.Size = PdfSharp.PageSize.A4;
            _pageWidth = page.Width.Point;
            _pageHeight = page.Height.Point;
            _contentWidth = _pageWidth - (2 * Margin);
            _gfx = XGraphics.FromPdfPage(page);
            _y = HeaderHeight + 24;
        }

        public void CloseCurrentPage() => _gfx?.Dispose();

        public void Gap(double points) => _y += points;

        public void EnsureSpace(double neededHeight)
        {
            if (_y + neededHeight > _pageHeight - FooterHeight)
            {
                NewPage();
            }
        }

        public void SectionHeading(string text)
        {
            EnsureSpace(26);
            _gfx.DrawString(text, _sectionHeading, new XSolidBrush(Navy), Margin, _y);
            _y += 4;
            _gfx.DrawLine(new XPen(Gold, 1.5), Margin, _y + 10, Margin + 60, _y + 10);
            _y += 20;
        }

        public void KeyValueTable((string Label, string Value)[] rows)
        {
            const double labelWidth = 180;
            var valueWidth = _contentWidth - labelWidth;

            foreach (var (label, value) in rows)
            {
                var lines = WrapText(value, _value, valueWidth - 8);
                var rowHeight = Math.Max(18, (lines.Count * 12) + 6);
                EnsureSpace(rowHeight);

                _gfx.DrawString(label, _label, new XSolidBrush(TextDark), Margin, _y + 12);
                var ty = _y + 12;
                foreach (var line in lines)
                {
                    _gfx.DrawString(line, _value, new XSolidBrush(TextDark), Margin + labelWidth, ty);
                    ty += 12;
                }
                _y += rowHeight;
                _gfx.DrawLine(new XPen(BorderGrey, 0.5), Margin, _y, Margin + _contentWidth, _y);
            }
        }

        public void DataTable(string[] headers, double[] columnFractions, IEnumerable<string[]> rows)
        {
            var columnWidths = columnFractions.Select(f => f * _contentWidth).ToArray();

            EnsureSpace(22);
            DrawTableHeaderRow(headers, columnWidths);

            var rowIndex = 0;
            foreach (var row in rows)
            {
                var wrapped = row.Select((cell, i) => WrapText(cell, _tableCell, columnWidths[i] - 8)).ToArray();
                var lineCount = wrapped.Max(w => w.Count);
                var rowHeight = Math.Max(16, (lineCount * 11) + 6);

                if (_y + rowHeight > _pageHeight - FooterHeight)
                {
                    NewPage();
                    DrawTableHeaderRow(headers, columnWidths);
                    rowIndex = 0;
                }

                var background = rowIndex % 2 == 1 ? RowStripe : XColor.FromArgb(255, 255, 255);
                _gfx.DrawRectangle(new XSolidBrush(background), Margin, _y, _contentWidth, rowHeight);

                var x = Margin;
                for (var col = 0; col < wrapped.Length; col++)
                {
                    var ty = _y + 11;
                    foreach (var line in wrapped[col])
                    {
                        _gfx.DrawString(line, _tableCell, new XSolidBrush(TextDark), x + 4, ty);
                        ty += 11;
                    }
                    x += columnWidths[col];
                }

                _y += rowHeight;
                _gfx.DrawLine(new XPen(BorderGrey, 0.5), Margin, _y, Margin + _contentWidth, _y);
                rowIndex++;
            }
        }

        private void DrawTableHeaderRow(string[] headers, double[] columnWidths)
        {
            const double headerRowHeight = 20;
            _gfx.DrawRectangle(new XSolidBrush(Navy), Margin, _y, _contentWidth, headerRowHeight);
            var x = Margin;
            for (var i = 0; i < headers.Length; i++)
            {
                _gfx.DrawString(headers[i], _tableHeader, XBrushes.White, x + 4, _y + 13);
                x += columnWidths[i];
            }
            _y += headerRowHeight;
        }

        private List<string> WrapText(string text, XFont font, double maxWidth)
        {
            if (string.IsNullOrEmpty(text)) return [""];

            var lines = new List<string>();
            var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var current = "";
            foreach (var word in words)
            {
                var candidate = current.Length == 0 ? word : $"{current} {word}";
                if (_gfx.MeasureString(candidate, font).Width > maxWidth && current.Length > 0)
                {
                    lines.Add(current);
                    current = word;
                }
                else
                {
                    current = candidate;
                }
            }
            if (current.Length > 0) lines.Add(current);
            return lines.Count == 0 ? [""] : lines;
        }

        /// <summary>Stamps the navy/gold header band and footer disclaimer on every page now that the
        /// total page count is settled -- done as a final pass (XGraphicsPdfPageOptions.Append keeps
        /// each page's already-drawn content) rather than while pages were still being written, so the
        /// footer can show "Page X of N".</summary>
        public void FinishAllPages()
        {
            var total = document.PageCount;
            for (var i = 0; i < total; i++)
            {
                var page = document.Pages[i];
                using var gfx = XGraphics.FromPdfPage(page, XGraphicsPdfPageOptions.Append);
                var width = page.Width.Point;
                var height = page.Height.Point;

                gfx.DrawRectangle(new XSolidBrush(Navy), 0, 0, width, HeaderHeight);
                gfx.DrawRectangle(new XSolidBrush(Gold), 0, HeaderHeight, width, 3);

                if (logo is not null)
                {
                    var logoHeight = 44.0;
                    var logoWidth = logoHeight * logo.PixelWidth / logo.PixelHeight;
                    gfx.DrawImage(logo, Margin, (HeaderHeight - logoHeight) / 2, logoWidth, logoHeight);
                    gfx.DrawString("Corporate Governance Tool", _title, new XSolidBrush(Gold), Margin + logoWidth + 16, 34);
                    gfx.DrawString("Insider Trading Declaration — Confirmation", _subtitle, XBrushes.White, Margin + logoWidth + 16, 52);
                }
                else
                {
                    gfx.DrawString("Corporate Governance Tool", _title, new XSolidBrush(Gold), Margin, 34);
                    gfx.DrawString("Insider Trading Declaration — Confirmation", _subtitle, XBrushes.White, Margin, 52);
                }

                gfx.DrawRectangle(new XSolidBrush(LightGrey), 0, height - FooterHeight, width, FooterHeight);
                gfx.DrawString(
                    "Confidential — for the intended recipient only. This is a system-generated record of your Insider Trading declaration.",
                    _footer, new XSolidBrush(Muted), Margin, height - FooterHeight + 18);
                gfx.DrawString(
                    $"Generated {DateTime.Now:dd MMM yyyy HH:mm}    Page {i + 1} of {total}",
                    _footer, new XSolidBrush(Muted), Margin, height - FooterHeight + 32);
            }
        }
    }
}
