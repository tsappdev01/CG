using ClosedXML.Excel;

using CGTOOL.Web.Data.Governance;

namespace CGTOOL.Web.Data.Reports;

/// <summary>A report as a real .xlsx, not a CSV with a spreadsheet's file extension.
///
/// CSV was what the register page wrote, and it costs the reader something every time: the header
/// is indistinguishable from data, the column widths are gone, and every number and date is
/// re-guessed from the reader's locale on open -- which in an office running both en-GB and en-US
/// silently turns 03/09 into two different days. A workbook carries its own formatting, so what
/// the reader opens is what was sent.</summary>
public static class ReportWorkbook
{
    private static readonly XLColor Navy = XLColor.FromArgb(0, 40, 80);
    private static readonly XLColor Brand = XLColor.FromArgb(175, 131, 46);

    /// <summary>A link a workbook can actually follow.
    ///
    /// Report links are site-relative ("/documents/25"), which a spreadsheet cannot resolve -- it
    /// has no page to be relative to. They are combined with the site's published address, the
    /// same constant the reminder emails build their links from. An href that is already absolute
    /// is taken as it stands; anything that will not parse gets no link rather than a broken
    /// one.</summary>
    private static Uri? Absolute(string href, string? baseUrl) =>
        Uri.TryCreate(href, UriKind.Absolute, out var absolute) ? absolute
        : Uri.TryCreate(new Uri(baseUrl is { Length: > 0 } b ? b : Branding.DefaultPublicBaseUrl), href, out var combined) ? combined
        : null;

    public static byte[] Build(ReportDocument report, string? baseUrl = null)
    {
        using var workbook = new XLWorkbook();
        // Excel refuses a sheet name over 31 characters, or one containing : \ / ? * [ ] -- and
        // refuses it by throwing while saving, long after the name was chosen.
        var sheet = workbook.AddWorksheet(SheetName(report.Title));

        var columnCount = Math.Max(1, report.Columns.Count);
        var row = 1;

        sheet.Cell(row, 1).Value = report.Title;
        sheet.Cell(row, 1).Style.Font.SetBold().Font.SetFontSize(14).Font.SetFontColor(Navy);
        sheet.Range(row, 1, row, columnCount).Merge();
        row++;

        if (report.Subtitle is { Length: > 0 } subtitle)
        {
            sheet.Cell(row, 1).Value = subtitle;
            sheet.Cell(row, 1).Style.Font.SetItalic().Font.SetFontColor(XLColor.Gray);
            sheet.Range(row, 1, row, columnCount).Merge();
            row++;
        }

        sheet.Cell(row, 1).Value =
            $"Prepared by {report.PreparedBy} on {report.GeneratedAt:dd MMM yyyy HH:mm} · {report.Count} record(s)";
        sheet.Cell(row, 1).Style.Font.SetFontColor(XLColor.Gray);
        sheet.Range(row, 1, row, columnCount).Merge();
        row++;

        sheet.Cell(row, 1).Value = $"Filters: {(report.FilterStatement is { Length: > 0 } f ? f : "none")}";
        sheet.Cell(row, 1).Style.Font.SetFontColor(XLColor.Gray);
        sheet.Range(row, 1, row, columnCount).Merge();
        row += 2;

        // The figures the screen opens with, laid out down the sheet rather than across it: a
        // stat band merged across the table's columns would fight the column widths below it.
        if (report.Stats.Count > 0)
        {
            foreach (var stat in report.Stats)
            {
                sheet.Cell(row, 1).Value = stat.Label;
                sheet.Cell(row, 1).Style.Font.SetBold();
                sheet.Cell(row, 2).Value = stat.Value;
                if (stat.Sub is { Length: > 0 } sub)
                {
                    sheet.Cell(row, 3).Value = sub;
                    sheet.Cell(row, 3).Style.Font.SetFontColor(XLColor.Gray);
                }
                row++;
            }
            row++;
        }

        if (report.IsDetail)
        {
            WriteRecords(sheet, report, ref row, baseUrl);

            sheet.Columns(1, 6).AdjustToContents(1, 8, 60);

            using var detailStream = new MemoryStream();
            workbook.SaveAs(detailStream);
            return detailStream.ToArray();
        }

        var headerRow = row;
        for (var c = 0; c < report.Columns.Count; c++)
        {
            var cell = sheet.Cell(headerRow, c + 1);
            cell.Value = report.Columns[c].Header;
            cell.Style.Font.SetBold().Font.SetFontColor(XLColor.White);
            cell.Style.Fill.SetBackgroundColor(Navy);
            cell.Style.Alignment.SetWrapText();
            cell.Style.Alignment.SetVertical(XLAlignmentVerticalValues.Center);
        }
        row++;

        foreach (var data in report.Rows)
        {
            for (var c = 0; c < report.Columns.Count && c < data.Length; c++)
            {
                var cell = sheet.Cell(row, c + 1);

                // A numeric column is written as a number so the reader can sum it in place.
                // Anything that will not parse is written as it stands rather than as 0, because a
                // dash meaning "not applicable" is not zero and must not be added up as one.
                if (report.Columns[c].Numeric && decimal.TryParse(data[c], out var number))
                {
                    cell.Value = number;
                    cell.Style.NumberFormat.SetFormat("#,##0.##");
                }
                else
                {
                    cell.SetValue(data[c]);
                }
            }
            row++;
        }

        if (report.Rows.Count > 0)
        {
            var table = sheet.Range(headerRow, 1, row - 1, columnCount);
            table.Style.Border.SetInsideBorder(XLBorderStyleValues.Thin);
            table.Style.Border.SetInsideBorderColor(XLColor.LightGray);
        }

        // Drawn after the body's gridlines, not alongside the header. The pass above covers the
        // boundary between the header and the first row, so a rule set with the header is
        // overwritten by it and never reaches the file -- the export had no rule at all.
        sheet.Range(headerRow, 1, headerRow, columnCount).Style.Border.SetBottomBorder(XLBorderStyleValues.Medium);
        sheet.Range(headerRow, 1, headerRow, columnCount).Style.Border.SetBottomBorderColor(Brand);

        // Freeze the header so it stays visible, and filter it so the reader can narrow the sheet
        // further without coming back to ask for another version of the report.
        sheet.SheetView.FreezeRows(headerRow);
        sheet.Range(headerRow, 1, Math.Max(headerRow, row - 1), columnCount).SetAutoFilter();

        sheet.Columns(1, columnCount).AdjustToContents(headerRow, 8, 60);

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    /// <summary>A detail report written down the sheet rather than across it: one block per
    /// person, their facts as label/value pairs, then each grid under its own heading. One sheet
    /// rather than one tab per person, because a workbook with 304 tabs is not usable.</summary>
    private static void WriteRecords(IXLWorksheet sheet, ReportDocument report, ref int row, string? baseUrl)
    {
        foreach (var record in report.Records)
        {
            sheet.Cell(row, 1).Value = record.Title;
            sheet.Cell(row, 1).Style.Font.SetBold().Font.SetFontSize(12).Font.SetFontColor(Navy);
            row++;

            if (record.Subtitle is { Length: > 0 } sub)
            {
                sheet.Cell(row, 1).Value = sub;
                sheet.Cell(row, 1).Style.Font.SetItalic().Font.SetFontColor(XLColor.Gray);
                row++;
            }

            foreach (var fact in record.Facts)
            {
                sheet.Cell(row, 1).Value = fact.Label;
                sheet.Cell(row, 1).Style.Font.SetBold();
                sheet.Cell(row, 2).SetValue(fact.Value);
                row++;
            }

            foreach (var table in record.Tables)
            {
                row++;
                sheet.Cell(row, 1).Value = table.Heading;
                sheet.Cell(row, 1).Style.Font.SetBold().Font.SetFontColor(Navy);
                row++;

                if (table.Rows.Count == 0)
                {
                    sheet.Cell(row, 1).SetValue(table.EmptyNote ?? "None.");
                    sheet.Cell(row, 1).Style.Font.SetItalic().Font.SetFontColor(XLColor.Gray);
                    row++;
                    continue;
                }

                for (var c = 0; c < table.Columns.Count; c++)
                {
                    var cell = sheet.Cell(row, c + 1);
                    cell.Value = table.Columns[c].Header;
                    cell.Style.Font.SetBold().Font.SetFontColor(XLColor.White);
                    cell.Style.Fill.SetBackgroundColor(Navy);
                }
                row++;

                var tableRow = 0;
                foreach (var data in table.Rows)
                {
                    for (var c = 0; c < table.Columns.Count && c < data.Length; c++)
                    {
                        var cell = sheet.Cell(row, c + 1);
                        if (table.Columns[c].Numeric && decimal.TryParse(data[c], out var number))
                        {
                            cell.Value = number;
                            cell.Style.NumberFormat.SetFormat("#,##0.##");
                        }
                        else
                        {
                            cell.SetValue(data[c]);
                        }

                        // A cell drawn as a view icon on screen says "View", which in a workbook
                        // is a word that does nothing. The link goes on the cell instead, so the
                        // document is one click from the sheet as well.
                        if (c == table.LinkColumn
                            && table.RowLinks is { } links
                            && tableRow < links.Count
                            && links[tableRow] is { Length: > 0 } href
                            && Absolute(href, baseUrl) is { } uri)
                        {
                            cell.SetHyperlink(new XLHyperlink(uri));
                        }
                    }
                    row++;
                    tableRow++;
                }
            }

            // A blank row and a rule between people, so a reader scrolling finds the boundaries.
            row++;
            sheet.Range(row, 1, row, 6).Style.Border.SetTopBorder(XLBorderStyleValues.Thin);
            sheet.Range(row, 1, row, 6).Style.Border.SetTopBorderColor(Brand);
            row += 2;
        }
    }

    private static string SheetName(string title)
    {
        var cleaned = new string(title.Select(c => "\\/?*[]:".Contains(c) ? ' ' : c).ToArray()).Trim();
        if (cleaned.Length == 0) cleaned = "Report";
        return cleaned.Length <= 31 ? cleaned : cleaned[..31].TrimEnd();
    }
}
