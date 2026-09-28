using PdfSharp.Drawing;
using PdfSharp.Pdf;

namespace CGTOOL.Web.Data.Governance;

/// <summary>The page furniture every declaration PDF shares: the navy/gold header band, the footer
/// disclaimer with "Page X of N", and the section headings, label/value rows and data tables the
/// documents are built from. It owns pagination -- the vertical cursor, and starting a new page
/// whenever content would run into the footer band.
///
/// Its own class rather than a private helper on one builder, because there are two documents now
/// (Insider Trading and Related Party &amp; COI) and they should not drift apart: what differs
/// between them is the line under the logo and the footer note, which are constructor arguments.</summary>
internal class DeclarationPdfWriter(PdfDocument document, XImage? logo, string documentTitle, string footerNote)
{
    internal static readonly XColor Navy = XColor.FromArgb(14, 42, 71);
    internal static readonly XColor Gold = XColor.FromArgb(217, 182, 90);
    private static readonly XColor LightGrey = XColor.FromArgb(245, 246, 248);
    private static readonly XColor RowStripe = XColor.FromArgb(250, 250, 251);
    private static readonly XColor TextDark = XColor.FromArgb(27, 36, 48);
    private static readonly XColor Muted = XColor.FromArgb(137, 147, 163);
    private static readonly XColor BorderGrey = XColor.FromArgb(226, 229, 234);

    private const double Margin = 40;
    private const double HeaderHeight = 82;
    private const double FooterHeight = 46;

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

    /// <summary>The heading carries its own space above it rather than leaving each caller to
    /// remember one: a heading drawn hard against the foot of the table before it read as a row of
    /// that table. None is added at the top of a page, where the header band already provides it.</summary>
    public void SectionHeading(string text)
    {
        // Room for the heading AND the start of what it heads: a heading alone at the foot of a page,
        // with its section beginning on the next one, is worse than a slightly short page.
        EnsureSpace(76);
        if (_y > HeaderHeight + 24) _y += 12;

        _gfx.DrawString(text, _sectionHeading, new XSolidBrush(Navy), Margin, _y + 10);
        _y += 14;
        _gfx.DrawLine(new XPen(Gold, 1.5), Margin, _y + 10, Margin + 60, _y + 10);
        _y += 20;
    }

    /// <summary>A run of ordinary prose -- the sentence a declaration makes about itself, which a
    /// label/value table has no room for.</summary>
    public void Paragraph(string text)
    {
        foreach (var line in WrapText(text, _value, _contentWidth))
        {
            EnsureSpace(14);
            _gfx.DrawString(line, _value, new XSolidBrush(TextDark), Margin, _y + 10);
            _y += 13;
        }
        _y += 4;
    }

    /// <summary>The drawn signature, on a ruled line, at its own aspect ratio within the box given.
    /// Kept whole on one page: a signature split across a page break is not a signature.</summary>
    public void Signature(XImage image, double maxWidth, double maxHeight)
    {
        var scale = Math.Min(maxWidth / image.PixelWidth, maxHeight / image.PixelHeight);
        var width = image.PixelWidth * scale;
        var height = image.PixelHeight * scale;

        EnsureSpace(height + 16);
        _gfx.DrawImage(image, Margin, _y, width, height);
        _y += height + 3;
        _gfx.DrawLine(new XPen(BorderGrey, 0.75), Margin, _y, Margin + Math.Max(width, 140), _y);
        _y += 10;
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

    /// <summary>Header text wraps to its column like the cells below it do. Drawn on one line, a
    /// heading wider than its column ran straight over the next one -- "Nature of holding" and
    /// "Trade licence" came out as one unreadable word.</summary>
    private void DrawTableHeaderRow(string[] headers, double[] columnWidths)
    {
        var wrapped = headers.Select((h, i) => WrapText(h, _tableHeader, columnWidths[i] - 8)).ToArray();
        var headerRowHeight = Math.Max(20, (wrapped.Max(w => w.Count) * 11) + 9);

        _gfx.DrawRectangle(new XSolidBrush(Navy), Margin, _y, _contentWidth, headerRowHeight);
        var x = Margin;
        for (var i = 0; i < headers.Length; i++)
        {
            var ty = _y + 13;
            foreach (var line in wrapped[i])
            {
                _gfx.DrawString(line, _tableHeader, XBrushes.White, x + 4, ty);
                ty += 11;
            }
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
                gfx.DrawString(documentTitle, _subtitle, XBrushes.White, Margin + logoWidth + 16, 52);
            }
            else
            {
                gfx.DrawString("Corporate Governance Tool", _title, new XSolidBrush(Gold), Margin, 34);
                gfx.DrawString(documentTitle, _subtitle, XBrushes.White, Margin, 52);
            }

            gfx.DrawRectangle(new XSolidBrush(LightGrey), 0, height - FooterHeight, width, FooterHeight);
            gfx.DrawString(
                footerNote,
                _footer, new XSolidBrush(Muted), Margin, height - FooterHeight + 18);
            gfx.DrawString(
                $"Generated {DateTime.Now:dd MMM yyyy HH:mm}    Page {i + 1} of {total}",
                _footer, new XSolidBrush(Muted), Margin, height - FooterHeight + 32);
        }
    }
}
