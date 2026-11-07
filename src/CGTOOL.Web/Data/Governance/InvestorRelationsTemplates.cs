using ClosedXML.Excel;

namespace CGTOOL.Web.Data.Governance;

/// <summary>Blank workbooks with the headers the Investor Relations uploads are read by.
///
/// The parser finds its header row by content and reads columns by name, so a file that came
/// straight from the market parses whatever order its columns are in. That tolerance is no help to
/// someone building a file by hand, who has to know what to call things before there is anything
/// to tolerate -- and finds out by uploading and being told the header row could not be found.
///
/// The headers here are the ones the parser normalizes to its own keys: it lowercases and drops
/// spaces, hyphens and full stops, so "Serial No" and "CDS Updated?" arrive as "serialno" and
/// "cdsupdated?". Every header below is spelled to normalize onto a key the parser asks for, and a
/// template whose headers drifted from the parser would be checked by uploading it.</summary>
public static class InvestorRelationsTemplates
{
    /// <summary>The Share Register columns, in the order the market's own export carries them. NIN
    /// is what identifies a row: a row without one is skipped, which is why it is first after the
    /// serial number and marked required.</summary>
    private static readonly (string Header, double Width, bool Required)[] ShareRegisterColumns =
    [
        ("Serial No", 10, false),
        ("NIN", 14, true),
        ("CDS Updated?", 14, false),
        ("Name", 28, false),
        ("English Name", 28, false),
        ("Life Status", 12, false),
        ("Client Type", 14, false),
        ("Passport No", 16, false),
        ("Family ID", 14, false),
        ("National ID", 20, false),
        ("Visa No", 14, false),
        ("Commercial License No", 20, false),
        ("Trade Registration No", 20, false),
        ("Citizenship", 14, false),
        ("Citizenship Descp", 20, false),
        ("PO Box", 12, false),
        ("City", 16, false),
        ("Country Code", 12, false),
        ("Country Name", 18, false),
        ("Address1", 24, false),
        ("Address2", 24, false),
        ("Address3", 24, false),
        ("Phone1", 16, false),
        ("Phone2", 16, false),
        ("Fax", 16, false),
        ("Email", 26, false),
        ("Qty", 14, false),
        ("% Qty", 10, false),
        ("Frozen", 12, false),
        ("Last Trans Date", 16, false),
        ("Payment Preference", 20, false),
        ("Linked NINs Reference", 20, false),
        ("Linked NINs", 20, false),
    ];

    /// <summary>The Shares Trading columns. A row needs both the report date and the investor
    /// number: without either there is nothing to attribute the movement to, or to date it.</summary>
    private static readonly (string Header, double Width, bool Required)[] ShareTradingColumns =
    [
        ("Report Date", 14, true),
        ("Symbol", 12, false),
        ("Investor Number", 16, true),
        ("Investor Name", 28, false),
        ("Client Type", 14, false),
        ("Nationality", 16, false),
        ("Previous Own Qty", 18, false),
        ("Current Own Qty", 18, false),
        ("Owned Qty Change", 18, false),
    ];

    public static byte[] ShareRegister() => Build("Share Register", ShareRegisterColumns);

    public static byte[] ShareTrading() => Build("Shares Trading", ShareTradingColumns);

    private static byte[] Build(string sheetName, (string Header, double Width, bool Required)[] columns)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add(sheetName);

        for (var i = 0; i < columns.Length; i++)
        {
            var cell = sheet.Cell(1, i + 1);
            cell.Value = columns[i].Header;
            cell.Style.Font.Bold = true;
            cell.Style.Font.FontColor = XLColor.White;
            cell.Style.Fill.BackgroundColor = XLColor.FromArgb(0, 40, 80);
            cell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;

            // Marked on the header itself rather than in a legend underneath: a legend is a row
            // someone has to delete before the file will parse.
            if (columns[i].Required) cell.Style.Fill.BackgroundColor = XLColor.FromArgb(146, 64, 14);

            sheet.Column(i + 1).Width = columns[i].Width;
        }

        sheet.Row(1).Height = 22;
        sheet.SheetView.FreezeRows(1);

        // Text, so a NIN with leading zeros or a long national ID is not read back as a number in
        // scientific notation by the spreadsheet the person fills it in with.
        foreach (var i in Enumerable.Range(1, columns.Length))
        {
            sheet.Column(i).Style.NumberFormat.Format = "@";
        }

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }
}
