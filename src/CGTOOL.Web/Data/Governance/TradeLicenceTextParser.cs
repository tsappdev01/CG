using System.Globalization;
using System.Text.RegularExpressions;

namespace CGTOOL.Web.Data.Governance;

/// <summary>Label-and-value handling over the plain text Document Intelligence returns for a
/// document. Shared by the trade licence parser and by the fallbacks the ID document parser uses
/// when the prebuilt model leaves a field empty.
///
/// Its own class rather than private methods on the service so it can be exercised against real
/// document text without an Azure resource: everything here is a pure function of the text.</summary>
public static class DocumentTextParser
{
    /// <summary>Dates as these documents print them. Day-first is tried before month-first: these
    /// are UAE documents, where 03/04/2026 means 3 April. Parsing is explicit and invariant rather
    /// than DateTime.TryParse, which follows the server's culture -- on an en-US server that reads a
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

    /// <summary>Looks for a label on each line and returns what follows it on that same line (after
    /// trimming the colon/dash/dot that usually separates label from value); if nothing usable
    /// follows -- the label sits alone as a column header, or, on a bilingual document, the Arabic
    /// label follows the English one -- looks down the next few lines instead.</summary>
    public static string? FindValue(string text, string[] labelFragments)
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

    /// <summary>Finds a labelled date as a date rather than as "whatever follows the label", which
    /// is what a bilingual document breaks: the Arabic label follows the English one, and the date
    /// is a line or two further down once OCR has flattened the table. So: take the part of the
    /// label's own line after the label and look for a date in it, then look down the next few
    /// lines.
    ///
    /// Searching after the label on its own line, rather than the whole line, keeps
    /// "Issue Date 01/01/2025 Expiry Date 01/01/2026" from returning the issue date.</summary>
    public static DateTime? FindDate(string text, string[] labelFragments)
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
    /// fragment is rarely just a date -- it is ": 14/03/2026 تاريخ الانتهاء" or a whole table row.</summary>
    public static DateTime? TryReadDate(string? fragment)
    {
        if (string.IsNullOrWhiteSpace(fragment)) return null;

        foreach (Match match in DateLike.Matches(fragment))
        {
            var candidate = Normalize(match.Value);
            foreach (var format in DateFormats)
            {
                if (DateTime.TryParseExact(candidate, Normalize(format), CultureInfo.InvariantCulture,
                        DateTimeStyles.None, out var date))
                {
                    return date.Date;
                }
            }
        }
        return null;
    }

    /// <summary>One separator for candidate and format alike, so "14-03-2026", "14.03.2026" and
    /// "14 Mar 2026" are all matched by the day-first formats without listing each separator.</summary>
    private static string Normalize(string value) => value.Replace('.', '/').Replace('-', '/').Replace(' ', '/');

    /// <summary>Whether a fragment is the value rather than the other half of a bilingual label.
    /// On a Dubai licence the line reads "License No. رقم الرخصة" and the number is in the row
    /// below, so taking whatever follows the English label returns the Arabic label -- which then
    /// shows up in the form as the licence number. The values these documents are read for all
    /// carry Latin letters or digits; an Arabic-only fragment is a label, not the answer.</summary>
    private static bool IsValue(string fragment) =>
        fragment.Length > 0 && fragment.Any(char.IsAsciiLetterOrDigit);

    public static string[] Lines(string text) => text
        .Split(['\n', '\r'], StringSplitOptions.RemoveEmptyEntries)
        .Select(l => l.Trim())
        .Where(l => l.Length > 0)
        .ToArray();
}

/// <summary>Pulls the licence number, business name and expiry date out of the plain text
/// prebuilt-layout returns for a trade licence.</summary>
public static class TradeLicenceTextParser
{
    // Label fragments (matched case-insensitively) a trade licence is likely to print next to each
    // field -- order matters within each array, first match wins, and the longer, more specific
    // label goes first so "expiry date" is tried before the bare "expiry".
    private static readonly string[] LicenceNumberLabels =
        ["licence no", "license no", "licence number", "license number", "reg no", "registration no"];

    private static readonly string[] BusinessNameLabels =
        ["trade name", "business name", "legal name", "company name", "establishment name"];

    private static readonly string[] ExpiryLabels =
    [
        "expiry date", "expiration date", "date of expiry", "expires on", "expiry",
        "valid until", "valid till", "valid upto", "valid up to", "renewal date",
        "تاريخ الانتهاء", "تاريخ الإنتهاء",
    ];

    private static readonly string[] ActivityLabels =
        ["license activities", "licence activities", "business activities", "activities", "activity",
         "الأنشطة", "النشاط", "نشاط"];

    /// <summary>The headings that follow the activities on these licences. Reaching one means the
    /// activities are over -- and, when it is what sits after the label, that the label's own line
    /// held no value at all.</summary>
    private static readonly string[] SectionLabels =
    [
        "address", "remarks", "license members", "licence members", "license details",
        "licence details", "phone no", "fax no", "mobile no", "p.o. box", "po box", "email",
        "parcel id", "issue date", "expiry date", "register no", "dcci",
    ];

    private static readonly Regex ArabicRun = new(@"[؀-ۿݐ-ݿﭐ-﷿ﹰ-﻿]+", RegexOptions.Compiled);

    /// <summary>What the licence says the company is permitted to do.
    ///
    /// Read on its own rather than through FindValue, because this is the one field whose cell OCR
    /// does not reliably put after its label. On the Dubai Economy and Tourism licence the banded
    /// row flattens to "Manufacturing of ... نشاط الرخصة التجارية / License Activities": the value
    /// is BEFORE the heading, what follows it is the Arabic heading, and taking the next line that
    /// carries Latin characters returns the next section's heading -- "العنوان / Address", which is
    /// what a member saw in this field. So: look after the label, then before it, then below it,
    /// and stop at the next section rather than accepting whatever comes first.
    ///
    /// The value also wraps -- "Manufacturing of Plating Products for Machinery and / Vehicles" is
    /// two OCR lines -- so continuation lines are joined until the section ends.</summary>
    public static string? FindActivities(string text)
    {
        var lines = DocumentTextParser.Lines(text);

        foreach (var label in ActivityLabels)
        {
            for (var i = 0; i < lines.Length; i++)
            {
                var idx = lines[i].IndexOf(label, StringComparison.OrdinalIgnoreCase);
                if (idx < 0) continue;

                var parts = new List<string>();
                var onOwnLine = Latin(lines[i][(idx + label.Length)..]) ?? Latin(lines[i][..idx]);
                if (onOwnLine is not null && !IsSectionHeading(onOwnLine) && !IsLabelRemnant(onOwnLine))
                {
                    parts.Add(onOwnLine);
                }

                // Below the label: the wrapped remainder, or the whole value where the cell did not
                // flatten onto the heading's line. An Arabic-only line is the translation -- skipped
                // while still looking for the English, and the end of it once the English is found.
                for (var j = i + 1; j < Math.Min(i + 6, lines.Length) && parts.Count < 3; j++)
                {
                    var candidate = Latin(lines[j]);
                    if (candidate is null)
                    {
                        if (parts.Count > 0) break;
                        continue;
                    }
                    if (IsSectionHeading(candidate) || DocumentTextParser.TryReadDate(candidate) is not null) break;
                    if (IsLabelRemnant(candidate)) continue;

                    parts.Add(candidate);
                }

                if (parts.Count > 0) return string.Join(" ", parts);
            }
        }
        return null;
    }

    /// <summary>The Latin half of a bilingual fragment, or null when there is none. These licences
    /// print the Arabic beside the English in the same cell, and OCR hands both over together.</summary>
    private static string? Latin(string fragment)
    {
        var latin = ArabicRun.Replace(fragment, " ").Trim();
        latin = string.Join(' ', latin.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        latin = latin.Trim(':', '-', '.', '/', '|', ' ').Trim();
        return latin.Any(char.IsAsciiLetterOrDigit) ? latin : null;
    }

    private static bool IsSectionHeading(string candidate) =>
        SectionLabels.Any(l => candidate.StartsWith(l, StringComparison.OrdinalIgnoreCase));

    /// <summary>Whether what was found is the rest of the heading rather than the value. The short
    /// labels are matched too -- "activities" matches inside "License Activities" -- and what sits
    /// before them on that line is then the word "License", not the activity.</summary>
    private static bool IsLabelRemnant(string candidate) =>
        ActivityLabels.Any(l => l.Contains(candidate, StringComparison.OrdinalIgnoreCase));

    public static TradeLicenceExtraction Parse(string text) => new(
        LicenceNumber: DocumentTextParser.FindValue(text, LicenceNumberLabels),
        BusinessName: DocumentTextParser.FindValue(text, BusinessNameLabels),
        ExpiryDate: DocumentTextParser.FindDate(text, ExpiryLabels),
        Activities: FindActivities(text));
}

/// <summary>Fallbacks for an Emirates ID or passport, used only where prebuilt-idDocument leaves a
/// field empty. The same analyze call returns the page's plain text beside its structured fields, so
/// these cost nothing extra -- and prebuilt-idDocument is known to be less consistent on Emirates ID
/// cards than on passports (bilingual layout, an ID number in a format it was not trained on).</summary>
public static class IdDocumentTextParser
{
    /// <summary>An Emirates ID number is 784-YYYY-NNNNNNN-C. OCR drops or varies the separators, so
    /// they are optional here and the number is put back into the printed form.</summary>
    private static readonly Regex EmiratesIdPattern = new(
        @"\b784[-\s]?(\d{4})[-\s]?(\d{7})[-\s]?(\d)\b", RegexOptions.Compiled);

    private static readonly string[] ExpiryLabels =
    [
        "date of expiry", "expiry date", "expires on", "expiry", "valid until", "valid till",
        "تاريخ الانتهاء", "تاريخ الإنتهاء",
    ];

    private static readonly string[] IdNumberLabels =
        ["id number", "identity number", "card number", "emirates id"];

    private static readonly string[] PassportNumberLabels =
        ["passport no", "passport number", "document no", "document number"];

    public static string? FindEmiratesIdNumber(string text)
    {
        var match = EmiratesIdPattern.Match(text);
        if (match.Success) return $"784-{match.Groups[1].Value}-{match.Groups[2].Value}-{match.Groups[3].Value}";

        return DocumentTextParser.FindValue(text, IdNumberLabels);
    }

    public static string? FindPassportNumber(string text) =>
        DocumentTextParser.FindValue(text, PassportNumberLabels);

    private static readonly string[] DateOfBirthLabels =
        ["date of birth", "birth date", "d.o.b", "dob", "تاريخ الميلاد"];

    private static readonly string[] IssueDateLabels =
        ["date of issue", "issue date", "issued on", "تاريخ الإصدار", "تاريخ الاصدار"];

    public static DateTime? FindExpiry(string text) => DocumentTextParser.FindDate(text, ExpiryLabels);

    public static DateTime? FindDateOfBirth(string text) => DocumentTextParser.FindDate(text, DateOfBirthLabels);

    public static DateTime? FindIssueDate(string text) => DocumentTextParser.FindDate(text, IssueDateLabels);
}
