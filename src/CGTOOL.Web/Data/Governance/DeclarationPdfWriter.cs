using PdfSharp;
using PdfSharp.Drawing;
using PdfSharp.Pdf;

namespace CGTOOL.Web.Data.Governance;

/// <summary>The page furniture every declaration PDF shares: the navy/gold header band naming the
/// group these documents come from, the footer
/// disclaimer with "Page X of N", and the section headings, label/value rows and data tables the
/// documents are built from. It owns pagination -- the vertical cursor, and starting a new page
/// whenever content would run into the footer band.
///
/// Its own class rather than a private helper on one builder, because there are two documents now
/// (Insider Trading and Related Party &amp; COI) and they should not drift apart: what differs
/// between them is the line under the logo and the footer note, which are constructor arguments.</summary>
internal class DeclarationPdfWriter(
    PdfDocument document,
    XImage? logo,
    string brandName,
    string documentTitle,
    string footerNote,
    PageOrientation orientation = PageOrientation.Portrait)
{
    internal static readonly XColor Navy = XColor.FromArgb(0, 40, 80);

    /// <summary>The logo's gold, for rules and headings drawn on a white page.</summary>
    internal static readonly XColor Brand = XColor.FromArgb(175, 131, 46);

    /// <summary>The same colour lightened, for anything drawn on the navy header band, where the
    /// swoosh itself sits at about 2.5:1 and reads as a smudge.</summary>
    internal static readonly XColor BrandOnDark = XColor.FromArgb(199, 154, 60);
    private static readonly XColor LightGrey = XColor.FromArgb(245, 246, 248);
    private static readonly XColor RowStripe = XColor.FromArgb(250, 250, 251);
    private static readonly XColor TextDark = XColor.FromArgb(27, 36, 48);
    private static readonly XColor Muted = XColor.FromArgb(137, 147, 163);
    private static readonly XColor BorderGrey = XColor.FromArgb(226, 229, 234);

    private const double Margin = 40;
    private const double HeaderHeight = 82;
    private const double FooterHeight = 46;

    /// <summary>Where content starts under the header band. 16 rather than 24: the band already
    /// carries its own breathing room, and eight points at the top of every page is eight points
    /// not available at the bottom, where a row gets pushed onto a sheet of its own.</summary>
    private const double ContentTop = HeaderHeight + 16;

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
        // Set after Size, which resets the orientation: a page asked for A4 landscape and given
        // its size afterwards comes out portrait with the content laid out for a wider sheet.
        page.Orientation = orientation;
        _pageWidth = page.Width.Point;
        _pageHeight = page.Height.Point;
        _contentWidth = _pageWidth - (2 * Margin);
        _gfx = XGraphics.FromPdfPage(page);
        _y = ContentTop;
        TagCurrentPage();
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
        EnsureSpace(64);
        if (_y > ContentTop) _y += 6;

        _gfx.DrawString(text, _sectionHeading, new XSolidBrush(Navy), Margin, _y + 10);
        _y += 13;
        _gfx.DrawLine(new XPen(Brand, 1.5), Margin, _y + 8, Margin + 60, _y + 8);
        _y += 14;
    }

    /// <summary>The band of headline figures a report opens with -- the same four the screen shows,
    /// in the same order, so a reader holding the PDF and a reader holding the screen are looking
    /// at the same report. Drawn as equal columns divided by hairlines rather than as a table,
    /// because these are one row of answers and a header row above them would only repeat the
    /// labels already under each figure.</summary>
    public void StatBand((string Label, string Value, string? Sub)[] stats)
    {
        if (stats.Length == 0) return;

        const double bandHeight = 42;
        EnsureSpace(bandHeight + 10);

        var columnWidth = _contentWidth / stats.Length;
        _gfx.DrawRectangle(new XSolidBrush(LightGrey), Margin, _y, _contentWidth, bandHeight);

        for (var i = 0; i < stats.Length; i++)
        {
            var x = Margin + (i * columnWidth);
            if (i > 0) _gfx.DrawLine(new XPen(BorderGrey, 0.5), x, _y + 6, x, _y + bandHeight - 6);

            _gfx.DrawString(stats[i].Label.ToUpperInvariant(), _footer, new XSolidBrush(Muted), x + 10, _y + 15);
            _gfx.DrawString(stats[i].Value, _sectionHeading, new XSolidBrush(Navy), x + 10, _y + 33);

            if (stats[i].Sub is { Length: > 0 } sub)
            {
                var valueWidth = _gfx.MeasureString(stats[i].Value, _sectionHeading).Width;
                _gfx.DrawString(sub, _footer, new XSolidBrush(Muted), x + 14 + valueWidth, _y + 33);
            }
        }

        _y += bandHeight + 12;
    }

    /// <summary>What the report was filtered to, printed under the figures. A printed report that
    /// does not say what it excluded is not evidence of anything -- the reader cannot tell an empty
    /// result from a filter that hid everything.</summary>
    public void FilterStatement(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;

        EnsureSpace(20);
        _gfx.DrawString(text, _footer, new XSolidBrush(Muted), Margin, _y + 8);
        _y += 18;
    }

    /// <summary>A run of ordinary prose -- the sentence a declaration makes about itself, which a
    /// label/value table has no room for.</summary>
    /// <summary>A paragraph the reader must not skim past -- drawn bold, in the critical colour,
    /// on its own tinted band.</summary>
    public void WarningParagraph(string text)
    {
        var lines = WrapText(text, _label, _contentWidth - 20);
        var height = (lines.Count * 12) + 14;
        EnsureSpace(height + 8);

        _gfx.DrawRectangle(new XSolidBrush(XColor.FromArgb(253, 236, 236)), Margin, _y, _contentWidth, height);
        _gfx.DrawRectangle(new XSolidBrush(XColor.FromArgb(220, 38, 38)), Margin, _y, 3, height);

        var ty = _y + 15;
        foreach (var line in lines)
        {
            _gfx.DrawString(line, _label, new XSolidBrush(XColor.FromArgb(153, 27, 27)), Margin + 12, ty);
            ty += 12;
        }
        _y += height + 10;
    }

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

    /// <summary>How wide each half of a key/value block is. One column is the default and what a
    /// declaration PDF uses; two is for the landscape report sheets, where a single column of ten
    /// facts runs a third of the way down a page that is twice as wide as it needs to be.</summary>
    private (double Block, double Label, double Gutter) KeyValueLayout(int columns)
    {
        const double gutter = 24;
        var block = columns <= 1 ? _contentWidth : (_contentWidth - gutter) / 2;
        return (block, columns <= 1 ? 180 : Math.Min(150, block * 0.42), gutter);
    }

    /// <summary>One row's height, in the one place both drawing and measuring read it from. Two
    /// copies of this arithmetic is a page break that happens where it was not predicted.</summary>
    private double KeyValueRowHeight(string label, string value, double labelWidth, double valueWidth)
    {
        var labelLines = WrapText(label, _label, labelWidth - 8).Count;
        var valueLines = WrapText(value, _value, valueWidth - 8).Count;
        return Math.Max(17, (Math.Max(labelLines, valueLines) * 12) + 5);
    }

    public void KeyValueTable((string Label, string Value)[] rows, int columns = 1)
    {
        var (block, labelWidth, gutter) = KeyValueLayout(columns);
        var valueWidth = block - labelWidth;
        var perRow = Math.Max(1, columns);

        for (var i = 0; i < rows.Length; i += perRow)
        {
            var pair = rows.Skip(i).Take(perRow).ToArray();
            var rowHeight = pair.Max(p => KeyValueRowHeight(p.Label, p.Value, labelWidth, valueWidth));
            EnsureSpace(rowHeight);

            for (var c = 0; c < pair.Length; c++)
            {
                var left = Margin + (c * (block + gutter));

                // The label wraps to its own column like the value does. Drawn on one line, a
                // label wider than its column ran straight over the value beside it -- the
                // declaration's "I / my relatives hold shares in <the parent company>?" came out
                // overlapping its own answer.
                var ly = _y + 12;
                foreach (var line in WrapText(pair[c].Label, _label, labelWidth - 8))
                {
                    _gfx.DrawString(line, _label, new XSolidBrush(TextDark), left, ly);
                    ly += 12;
                }

                var ty = _y + 12;
                foreach (var line in WrapText(pair[c].Value, _value, valueWidth - 8))
                {
                    _gfx.DrawString(line, _value, new XSolidBrush(TextDark), left + labelWidth, ty);
                    ty += 12;
                }
            }

            _y += rowHeight;
            _gfx.DrawLine(new XPen(BorderGrey, 0.5), Margin, _y, Margin + _contentWidth, _y);
        }
    }

    // ---------- measuring, for deciding where a page break belongs before drawing one ----------

    /// <summary>What is left on this page below the cursor and above the footer band.</summary>
    public double Remaining => _pageHeight - FooterHeight - _y;

    // ---------- whose pages these are ----------

    private readonly Dictionary<int, string> _pageRecord = [];
    private string? _recordTitle;

    /// <summary>Everything from here belongs to this person, including any page a long table
    /// starts on its own. Their name is repeated at the top of each of those pages with "Page 1 of
    /// 2" beside it, counted within their own sheets rather than the whole document: someone
    /// reading one declarant's pages wants to know they have all of them, and "page 7 of 31"
    /// answers a question nobody asked.</summary>
    public void BeginRecord(string title)
    {
        _recordTitle = title;
        TagCurrentPage();
    }

    private void TagCurrentPage()
    {
        if (_recordTitle is { Length: > 0 } title && document.PageCount > 0)
        {
            _pageRecord[document.PageCount - 1] = title;
        }
    }

    /// <summary>Each page's place among its own record's pages, worked out from runs of pages
    /// carrying the same name.</summary>
    private Dictionary<int, (string Title, int Index, int Count)> RecordPaging(int total)
    {
        var paging = new Dictionary<int, (string, int, int)>();

        var i = 0;
        while (i < total)
        {
            if (!_pageRecord.TryGetValue(i, out var title)) { i++; continue; }

            var end = i;
            while (end < total && _pageRecord.TryGetValue(end, out var t) && t == title) end++;

            for (var k = i; k < end; k++) paging[k] = (title, k - i + 1, end - i);
            i = end;
        }

        return paging;
    }

    /// <summary>Drawn from the right margin leftwards, cut rather than allowed to run back into
    /// the document title beside it.</summary>
    private static void DrawRightAligned(XGraphics gfx, string text, XFont font, XBrush brush,
        double right, double y, double maxWidth)
    {
        var shown = text;
        while (shown.Length > 4 && gfx.MeasureString(shown, font).Width > maxWidth)
        {
            shown = shown[..(shown.Length - 2)] + "…";
        }

        gfx.DrawString(shown, font, brush, right - gfx.MeasureString(shown, font).Width, y);
    }

    /// <summary>A record's name with its line of context beside it rather than under it. The name
    /// is repeated in the header band on every one of this record's pages, so a title block that
    /// takes three lines to say what the band already says costs a sheet it has not earned.</summary>
    public void RecordHeading(string title, string? context)
    {
        EnsureSpace(64);
        if (_y > ContentTop) _y += 6;

        _gfx.DrawString(title, _sectionHeading, new XSolidBrush(Navy), Margin, _y + 10);

        if (context is { Length: > 0 })
        {
            var titleWidth = _gfx.MeasureString(title, _sectionHeading).Width;
            _gfx.DrawString(context, _value, new XSolidBrush(Muted), Margin + titleWidth + 14, _y + 10);
        }

        _y += 13;
        _gfx.DrawLine(new XPen(Brand, 1.5), Margin, _y + 8, Margin + 60, _y + 8);
        _y += 14;
    }

    /// <summary>A heading's height as <see cref="SectionHeading"/> draws it, including the 6
    /// points it adds when something is already above it -- which, inside a record, there is.</summary>
    public double MeasureSectionHeading() => 33;

    public double MeasureParagraph(string text) =>
        (WrapText(text, _value, _contentWidth).Count * 13) + 4;

    public double MeasureKeyValueTable((string Label, string Value)[] rows, int columns = 1)
    {
        var (block, labelWidth, _) = KeyValueLayout(columns);
        var valueWidth = block - labelWidth;
        var perRow = Math.Max(1, columns);

        var total = 0.0;
        for (var i = 0; i < rows.Length; i += perRow)
        {
            total += rows.Skip(i).Take(perRow)
                .Max(p => KeyValueRowHeight(p.Label, p.Value, labelWidth, valueWidth));
        }
        return total;
    }

    public double MeasureDataTable(string[] headers, double[] columnFractions, IReadOnlyList<string[]> rows)
    {
        var columnWidths = columnFractions.Select(f => f * _contentWidth).ToArray();
        var total = HeaderRowHeight(headers, columnWidths);

        foreach (var row in rows)
        {
            total += DataRowHeight(row, columnWidths);
        }

        return total;
    }

    public void DataTable(string[] headers, double[] columnFractions, IEnumerable<string[]> rows)
    {
        var columnWidths = columnFractions.Select(f => f * _contentWidth).ToArray();
        var table = rows as IReadOnlyList<string[]> ?? [.. rows];

        // Room for the header AND its first row. Asking only for the header's own height let a
        // navy bar sit alone at the foot of a page with everything it labels on the next one.
        EnsureSpace(HeaderRowHeight(headers, columnWidths)
            + (table.Count > 0 ? DataRowHeight(table[0], columnWidths) : 0));
        DrawTableHeaderRow(headers, columnWidths);

        var rowIndex = 0;
        foreach (var row in table)
        {
            var wrapped = row.Select((cell, i) => WrapText(cell, _tableCell, columnWidths[i] - 8)).ToArray();
            var rowHeight = DataRowHeight(row, columnWidths);

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
        var headerRowHeight = HeaderRowHeight(headers, columnWidths);

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

    private double DataRowHeight(string[] row, double[] columnWidths) => Math.Max(15,
        (row.Select((cell, i) => WrapText(cell, _tableCell, columnWidths[Math.Min(i, columnWidths.Length - 1)] - 8).Count)
            .DefaultIfEmpty(1).Max() * 11) + 6);

    private double HeaderRowHeight(string[] headers, double[] columnWidths) => Math.Max(19,
        (headers.Select((h, i) => WrapText(h, _tableHeader, columnWidths[Math.Min(i, columnWidths.Length - 1)] - 8).Count)
            .DefaultIfEmpty(1).Max() * 11) + 9);

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
        var paging = RecordPaging(total);

        for (var i = 0; i < total; i++)
        {
            var page = document.Pages[i];
            using var gfx = XGraphics.FromPdfPage(page, XGraphicsPdfPageOptions.Append);
            var width = page.Width.Point;
            var height = page.Height.Point;

            gfx.DrawRectangle(new XSolidBrush(Navy), 0, 0, width, HeaderHeight);
            gfx.DrawRectangle(new XSolidBrush(Brand), 0, HeaderHeight, width, 3);

            if (logo is not null)
            {
                // 32, not the 44 the old logo used: this wordmark is 4.4:1 rather than
                // 2.3:1, so the same height would push the title 90pt further right.
                var logoHeight = 32.0;
                var logoWidth = logoHeight * logo.PixelWidth / logo.PixelHeight;
                gfx.DrawImage(logo, Margin, (HeaderHeight - logoHeight) / 2, logoWidth, logoHeight);
                gfx.DrawString(brandName, _title, new XSolidBrush(BrandOnDark), Margin + logoWidth + 16, 34);
                gfx.DrawString(documentTitle, _subtitle, XBrushes.White, Margin + logoWidth + 16, 52);
            }
            else
            {
                gfx.DrawString(brandName, _title, new XSolidBrush(BrandOnDark), Margin, 34);
                gfx.DrawString(documentTitle, _subtitle, XBrushes.White, Margin, 52);
            }

            // Whose sheet this is, repeated at the top of every one of their pages, so a page
            // separated from the one before it still says who it belongs to.
            if (paging.TryGetValue(i, out var record))
            {
                var room = (width - Margin) - (Margin + 300);
                DrawRightAligned(gfx, record.Title, _sectionHeading, XBrushes.White, width - Margin, 34, room);
                DrawRightAligned(gfx, $"Page {record.Index} of {record.Count}", _subtitle,
                    new XSolidBrush(BrandOnDark), width - Margin, 52, room);
            }

            gfx.DrawRectangle(new XSolidBrush(LightGrey), 0, height - FooterHeight, width, FooterHeight);
            gfx.DrawString(
                footerNote,
                _footer, new XSolidBrush(Muted), Margin, height - FooterHeight + 18);

            // A page that belongs to someone is numbered within their sheets, up in the header. A
            // document-wide count underneath it would be a second page number saying something
            // else, which is how a printed set gets collated wrongly.
            gfx.DrawString(
                paging.ContainsKey(i)
                    ? $"Generated {DateTime.Now:dd MMM yyyy HH:mm}    Sheet {i + 1} of {total}"
                    : $"Generated {DateTime.Now:dd MMM yyyy HH:mm}    Page {i + 1} of {total}",
                _footer, new XSolidBrush(Muted), Margin, height - FooterHeight + 32);
        }
    }
}
