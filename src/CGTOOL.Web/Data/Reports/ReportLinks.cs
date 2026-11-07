namespace CGTOOL.Web.Data.Reports;

/// <summary>Where a summary report's row goes when someone wants to see what is behind it.
///
/// The summary reports say who has declared; the detailed ones say what they declared. Getting
/// from one to the other used to mean reading a name off one page and typing it into another's
/// search box, with the period set by hand to match. These links carry both.
///
/// In one place because three reports send people to two detail pages, and a route spelled
/// slightly differently in one of them is a link that lands on an empty report.</summary>
public static class ReportLinks
{
    public const string InsiderDetail = "/reports/insider-submission-detail";

    public const string RpCoiDetail = "/reports/rp-coi-submission-detail";

    /// <summary>One declarant's sheet, on the period the row came from. The period travels with
    /// the name: a report headed "Q1 2026" that opens on the newest quarter has answered a
    /// different question from the one that was asked.</summary>
    public static string Declarant(string detailPage, string memberName, int year, int quarter) =>
        $"{detailPage}?name={Uri.EscapeDataString(memberName)}&year={year}&quarter={quarter}";
}
