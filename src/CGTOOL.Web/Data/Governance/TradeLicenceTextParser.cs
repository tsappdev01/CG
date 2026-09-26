using System.Globalization;
using System.Text.RegularExpressions;

namespace CGTOOL.Web.Data.Governance;

/// <summary>Pulls the licence number, business name and expiry date out of the plain text Document
/// Intelligence's prebuilt-layout model returns for a trade licence.
///
/// Its own class rather than private methods on the service so it can be exercised against real
/// licence text without an Azure resource: everything here is a pure function of the OCR text.</summary>
public static class TradeLicenceTextParser
{
    // Label fragments (matched case-insensitively) a trade licence is likely to print next to each
    // field -- order matters within each array, first match wins, and the longer, more specific
    // label goes first so "expiry date" is tried before the bare "expiry".
    private static readonly string[] LicenceNumberLabels =
        ["licence no", "license no", "licence number", "license number", "reg no", "registration no"];

    private static readonly string[] BusinessNameLabels =
        ["trade name", "business name", "legal name", "company name", "establishment name"];

    // The Arabic labels are here because a Dubai DED licence prints both, and OCR reads the line as
    // "Expiry Date <arabic>" -- so the English label is found but what follows it on the line is the
    // Arabic label rather than a date. See FindDate.
    private static readonly string[] ExpiryLabels =
    [
        "expiry date", "expiration date", "date of expiry", "expires on", "expiry",
        "valid until", "valid till", "valid upto", "valid up to", "renewal date",
        "تاريخ الانتهاء", "تاريخ الإنتهاء",
    ];

    /// <summary>Dates as licences print them. Day-first is tried before month-first: these are UAE
    /// documents, where 03/04/2026 means 3 April. Parsing is explicit and invariant rather than
    /// DateTime.TryParse, which follows the server's culture -- on an en-US server that reads a
    /// day-first date as month-first, so 14/03/2026 fails outright and 03/04/2026 silently comes
    /// back as 4 March. A wrong expiry date that looks right is worse than none.</summary>
    private static readonly string[] DateFormats =
    [
        "dd/MM/yyyy", "d/M/yyyy", "dd-MM-yyyy", "d-M-yyyy", "dd.MM.yyyy", "d.M.yyyy",
        "yyyy-MM-dd", "yyyy/MM/dd",
        "dd MMM yyyy", "d MMM yyyy", "dd-MMM-yyyy", "d-MMM-yyyy",
        "dd MMMM yyyy", "d MMMM yyyy",
        "MM/dd/yyyy", "M/d/yyyy",
    ];

    private static readonly Regex DateLike = new(
        @"\d{1,4}[/\-.\s][A-Za-z]{3,9}[/\-.\s]\d{2,4}|\d{1,4}[/\-.]\d{1,2}[/\-.]\d{2,4}",
        RegexOptions.Compiled);

    public static TradeLicenceExtraction Parse(string text) => new(
        LicenceNumber: FindValue(text, LicenceNumberLabels),
        BusinessName: FindValue(text, BusinessNameLabels),
        ExpiryDate: FindDate(text, ExpiryLabels));

    /// <summary>Looks for a label on each line and returns what follows it on that same line (after
    /// trimming the colon/dash/dot that usually separates label from value); if nothing usable
    /// follows -- the label sits alone as a column header, or, on a bilingual licence, the Arabic
    /// label follows the English one -- looks down the next few lines instead.</summary>
    private static string? FindValue(string text, string[] labelFragments)
    {
        var lines = Lines(text);

        foreach (var label in labelFragments)
        {
            for (var i = 0; i < lines.Length; i++)
            {
                var idx = lines[i].IndexOf(label, StringComparison.OrdinalIgnoreCase);
                if (idx < 0) continue;

                var afterLabel = lines[i][(idx + label.Length)..].Trim().TrimStart(':', '-', '.').Trim();
                if (IsValue(afterLabel)) return afterLabel;

                for (var j = i + 1; j < Math.Min(i + 4, lines.Length); j++)
                {
                    if (IsValue(lines[j])) return lines[j];
                }
            }
        }
        return null;
    }

    /// <summary>Whether a fragment is the value rather than the other half of a bilingual label.
    /// On a Dubai licence the line reads "License No. رقم الرخصة" and the number is in the row
    /// below, so taking whatever follows the English label returns the Arabic label -- which then
    /// shows up in the form as the licence number. The licence number and the English trade name
    /// both carry Latin letters or digits; an Arabic-only fragment is a label, not the answer.</summary>
    private static bool IsValue(string fragment) =>
        fragment.Length > 0 && fragment.Any(char.IsAsciiLetterOrDigit);

    /// <summary>Finds the expiry as a date rather than as "whatever follows the label", which is what
    /// a bilingual licence breaks: the Arabic label follows the English one, and the date is a line
    /// or two further down once OCR has flattened the table. So: take the part of the label's own
    /// line after the label and look for a date in it, then look down the next few lines.
    ///
    /// Searching after the label on its own line, rather than the whole line, keeps
    /// "Issue Date 01/01/2025 Expiry Date 01/01/2026" from returning the issue date.</summary>
    private static DateTime? FindDate(string text, string[] labelFragments)
    {
        var lines = Lines(text);

        foreach (var label in labelFragments)
        {
            for (var i = 0; i < lines.Length; i++)
            {
                var idx = lines[i].IndexOf(label, StringComparison.OrdinalIgnoreCase);
                if (idx < 0) continue;

                if (TryReadDate(lines[i][(idx + label.Length)..]) is { } onSameLine) return onSameLine;

                for (var j = i + 1; j < Math.Min(i + 4, lines.Length); j++)
                {
                    if (TryReadDate(lines[j]) is { } below) return below;
                }
            }
        }
        return null;
    }

    /// <summary>Pulls the first date-looking run of characters out of a fragment and parses it. The
    /// fragment is rarely just a date -- it is "‎: 14/03/2026 تاريخ الانتهاء" or a whole table row.</summary>
    public static DateTime? TryReadDate(string? fragment)
    {
        if (string.IsNullOrWhiteSpace(fragment)) return null;

        foreach (Match match in DateLike.Matches(fragment))
        {
            var candidate = match.Value.Replace('.', '/').Replace('-', '/').Replace(' ', '/');
            foreach (var format in DateFormats)
            {
                var normalized = format.Replace('.', '/').Replace('-', '/').Replace(' ', '/');
                if (DateTime.TryParseExact(candidate, normalized, CultureInfo.InvariantCulture,
                        DateTimeStyles.None, out var date))
                {
                    return date.Date;
                }
            }
        }
        return null;
    }

    private static string[] Lines(string text) => text
        .Split(['\n', '\r'], StringSplitOptions.RemoveEmptyEntries)
        .Select(l => l.Trim())
        .Where(l => l.Length > 0)
        .ToArray();
}
