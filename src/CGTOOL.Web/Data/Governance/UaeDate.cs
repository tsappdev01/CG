using System.Globalization;

namespace CGTOOL.Web.Data.Governance;

/// <summary>Dates as they are written here: dd/MM/yyyy, the format on an Emirates ID, a passport and
/// a trade licence.
///
/// Why these fields are text inputs rather than &lt;input type="date"&gt;: a native date input is
/// rendered in the *browser's* locale, which no attribute on the page can change -- on a machine set
/// to en-US it shows mm/dd/yyyy however the page is marked up. Typing a licence's 14/04/2025 into a
/// box labelled mm/dd/yyyy is a trap, and one the person only notices if the day happens to be above
/// twelve. A text box we format and parse ourselves says the same thing on every machine.</summary>
public static class UaeDate
{
    public const string Pattern = "dd/MM/yyyy";

    public static string? Show(DateTime? value) =>
        value?.ToString(Pattern, CultureInfo.InvariantCulture);

    /// <summary>Day-first, and tolerant of the separators people type. Returns null for anything it
    /// cannot read, so the caller decides whether that clears the field or is worth complaining
    /// about -- see SetDate in MyWorkspace.</summary>
    public static DateTime? Parse(string? text) => DocumentTextParser.TryReadDate(text);
}
