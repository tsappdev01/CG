using ClosedXML.Excel;

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
    private static readonly XLColor Navy = XLColor.FromArgb(14, 42, 71);
    private static readonly XLColor Gold = XLColor.FromArgb(217, 182, 90);

    public static byte[] Build(ReportDocument report)
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
            $"Prepared by {report.PreparedBy} on {report.GeneratedAt:dd MMM yyyy HH:mm} · {report.Rows.Count} record(s)";
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
        sheet.Range(headerRow, 1, headerRow, columnCount).Style.Border.SetBottomBorder(XLBorderStyleValues.Medium);
        sheet.Range(headerRow, 1, headerRow, columnCount).Style.Border.SetBottomBorderColor(Gold);
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

        // Freeze the header so it stays visible, and filter it so the reader can narrow the sheet
        // further without coming back to ask for another version of the report.
        sheet.SheetView.FreezeRows(headerRow);
        sheet.Range(headerRow, 1, Math.Max(headerRow, row - 1), columnCount).SetAutoFilter();

        sheet.Columns(1, columnCount).AdjustToContents(headerRow, 8, 60);

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private static string SheetName(string title)
    {
        var cleaned = new string(title.Select(c => "\\/?*[]:".Contains(c) ? ' ' : c).ToArray()).Trim();
        if (cleaned.Length == 0) cleaned = "Report";
        return cleaned.Length <= 31 ? cleaned : cleaned[..31].TrimEnd();
    }
}
