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

    /// <summary>The machine-readable zone, read positionally. Returns null when the text holds no
    /// zone this recognises -- a photo of only the front of a card, or OCR that lost the rows.</summary>
    public static MachineReadableZone? FindMrz(string text) => MachineReadableZoneParser.Parse(text);
}

/// <summary>What the machine-readable zone holds: the rows of capitals and chevrons at the foot of a
/// passport and on the back of an Emirates ID.</summary>
public record MachineReadableZone(
    string? DocumentNumber,
    string? Nationality,
    DateTime? DateOfBirth,
    DateTime? DateOfExpiry,
    string? Surname,
    string? GivenNames)
{
    public string? FullName =>
        string.Join(" ", new[] { GivenNames, Surname }.Where(p => !string.IsNullOrWhiteSpace(p))) is { Length: > 0 } name
            ? name
            : null;
}

/// <summary>Reads the machine-readable zone (ICAO 9303) out of the text an analyze call returns.
///
/// Worth doing because every value in the zone sits at a fixed offset: there is no label to find and
/// no choosing between the two or three dates printed on the card. That is precisely what fails on
/// these documents otherwise -- prebuilt-idDocument leaves DateOfExpiration empty on a bilingual
/// Emirates ID, and a label search has to guess whether the date it found is the issue date or the
/// expiry.
///
/// Two layouts are read: TD3, the passport's two rows of 44, and TD1, the Emirates ID's three rows
/// of 30. Rows longer than the layout are accepted and the extra ignored, since OCR is readier to
/// append noise than to drop characters -- but a row that came back short is rejected, because
/// everything after the missing character would be read from the wrong offset.</summary>
internal static class MachineReadableZoneParser
{
    // How much of each row has to survive OCR. Every field read here sits in the leading, fixed part
    // of its row -- the rest is filler -- so a row is long enough once it reaches the last field,
    // and trailing characters lost off the end cost nothing. A row SHORTER than this is rejected
    // rather than padded, because everything after a missing character would be read from the wrong
    // offset and quietly produce a wrong date.
    private const int Td3NameRow = 6;      // P<ISS then the name, which is read to whatever length survived
    private const int Td3DataRow = 27;     // ...through the expiry date
    private const int Td1IdRow = 20;       // ...through the document number and the UAE's optional data
    private const int Td1DataRow = 18;     // ...through the nationality
    private const int Td1NameRow = 1;

    public static MachineReadableZone? Parse(string text)
    {
        var rows = text.Split('\n').Select(Clean).ToArray();

        for (var i = 0; i < rows.Length; i++)
        {
            if (i + 1 < rows.Length && Accept(ParseTd3(rows[i], rows[i + 1])) is { } passport) return passport;
            if (i + 2 < rows.Length && Accept(ParseTd1(rows[i], rows[i + 1], rows[i + 2])) is { } card) return card;
        }
        return null;
    }

    /// <summary>What separates a real zone from two lines of prose that happen to start with the
    /// right letter: an expiry date that parses, and a three-letter nationality where one belongs.
    /// A wrong date read out of ordinary text would be worse than no date at all.</summary>
    private static MachineReadableZone? Accept(MachineReadableZone? zone) =>
        zone is { DateOfExpiry: not null, Nationality.Length: 3 } && zone.Nationality.All(char.IsAsciiLetter)
            ? zone
            : null;

    /// <summary>OCR reads the zone's chevrons as any of a handful of look-alikes and sprinkles spaces
    /// through it; both are put back before anything is read by position.</summary>
    private static string Clean(string line)
    {
        var chars = line
            .Where(c => !char.IsWhiteSpace(c))
            .Select(c => c is '\u00ab' or '\u2039' or '\u226a' or '\u02c2' ? '<' : char.ToUpperInvariant(c))
            .Where(c => c is '<' || char.IsAsciiLetterOrDigit(c))
            .ToArray();
        return new string(chars);
    }

    /// <summary>Passport (TD3, two rows of 44). Row 1 is P&lt;ISS then the name; row 2 carries the
    /// number, the nationality and both dates.</summary>
    private static MachineReadableZone? ParseTd3(string row1, string row2)
    {
        if (row1.Length < Td3NameRow || row2.Length < Td3DataRow) return null;
        if (row1[0] != 'P') return null;

        var (surname, given) = SplitName(row1[5..]);
        return new MachineReadableZone(
            DocumentNumber: Field(row2[..9]),
            Nationality: Field(row2[10..13]),
            DateOfBirth: PastDate(row2[13..19]),
            DateOfExpiry: ExpiryDate(row2[21..27]),
            Surname: surname,
            GivenNames: given);
    }

    /// <summary>Emirates ID (TD1, three rows of 30). Row 1 is the document code, the issuing state
    /// and the number; row 2 carries both dates and the nationality; row 3 is the name.</summary>
    private static MachineReadableZone? ParseTd1(string row1, string row2, string row3)
    {
        if (row1.Length < Td1IdRow || row2.Length < Td1DataRow || row3.Length < Td1NameRow) return null;
        if (row1[0] is not ('I' or 'A' or 'C')) return null;

        var (surname, given) = SplitName(row3);
        return new MachineReadableZone(
            DocumentNumber: EmiratesIdNumber(row1) ?? Field(row1[5..14]),
            Nationality: Field(row2[15..18]),
            DateOfBirth: PastDate(row2[..6]),
            DateOfExpiry: ExpiryDate(row2[8..14]),
            Surname: surname,
            GivenNames: given);
    }

    /// <summary>The UAE writes the whole fifteen-digit Emirates ID number from position six, running
    /// past the nine characters ICAO reserves for a document number and into the optional data after
    /// it. That is the number printed on the front of the card and the one the form asks for, so it
    /// wins over the nine-character field where the row carries it.</summary>
    private static string? EmiratesIdNumber(string row1)
    {
        var digits = new string(row1[5..].TakeWhile(char.IsAsciiDigit).ToArray());
        return digits.Length >= 15 && digits.StartsWith("784", StringComparison.Ordinal)
            ? $"784-{digits[3..7]}-{digits[7..14]}-{digits[14]}"
            : null;
    }

    /// <summary>SURNAME&lt;&lt;GIVEN&lt;NAMES, padded out with chevrons.</summary>
    private static (string? Surname, string? Given) SplitName(string field)
    {
        var parts = field.Split("<<", 2);
        return (Words(parts[0]), parts.Length > 1 ? Words(parts[1]) : null);
    }

    private static string? Words(string field)
    {
        var words = field.Split('<', StringSplitOptions.RemoveEmptyEntries);
        return words.Length == 0 ? null : string.Join(" ", words);
    }

    private static string? Field(string raw)
    {
        var value = raw.Replace("<", "", StringComparison.Ordinal).Trim();
        return value.Length == 0 ? null : value;
    }

    /// <summary>A date of birth is always behind us, so a two-digit year that would put it in the
    /// future belongs to the last century.</summary>
    private static DateTime? PastDate(string yymmdd) =>
        ReadDate(yymmdd) is not { } date ? null : date > DateTime.Today ? date.AddYears(-100) : date;

    /// <summary>An expiry can fall either side of today -- an expired document is exactly what these
    /// screens want to catch -- so the century is chosen by distance instead: a year read as more
    /// than eighty years out is the last century's, which is right for anything but a document older
    /// than the people carrying it.</summary>
    private static DateTime? ExpiryDate(string yymmdd) =>
        ReadDate(yymmdd) is not { } date ? null : date.Year - DateTime.Today.Year > 80 ? date.AddYears(-100) : date;

    /// <summary>YYMMDD, read as 20YY. OCR confuses a handful of digits with the letters that look
    /// like them, and only ever in that direction -- this field is all digits on the document.</summary>
    private static DateTime? ReadDate(string raw)
    {
        var digits = new string(raw.Select(c => c switch
        {
            'O' or 'D' or 'Q' => '0',
            'I' or 'L' => '1',
            'Z' => '2',
            'S' => '5',
            'G' => '6',
            'B' => '8',
            _ => c,
        }).ToArray());

        return DateTime.TryParseExact(digits, "yyMMdd", CultureInfo.InvariantCulture,
            DateTimeStyles.None, out var date)
            ? date.Date
            : null;
    }
}
