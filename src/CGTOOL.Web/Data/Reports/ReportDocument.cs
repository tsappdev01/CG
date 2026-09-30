namespace CGTOOL.Web.Data.Reports;

/// <summary>One column of a report: its heading, and the share of the sheet's width it takes.
/// Width is a fraction rather than a pixel count because the same definition is laid out three
/// times -- on an A4 landscape sheet on screen, on an A4 landscape PDF page, and in a spreadsheet
/// whose column widths are measured in characters -- and only the proportions survive all three.</summary>
/// <param name="Numeric">Written as a number rather than text in the spreadsheet, so the reader can
/// sum the column in place instead of retyping it. On the sheet and in the PDF it reads the same as
/// any other column -- these tables are read down, not totalled.</param>
public sealed record ReportColumn(string Header, double Width, bool Numeric = false);

/// <summary>One of the headline figures a report opens with.</summary>
public sealed record ReportStat(string Label, string Value, string? Sub = null);

/// <summary>A report, described once and rendered four ways: on screen, on paper, as a PDF and as
/// a spreadsheet. The four must agree -- a PDF that shows different numbers from the screen it was
/// generated from is worse than no PDF -- so each report page builds one of these and hands the
/// same instance to whichever writer the reader asked for.
///
/// It holds rows already rendered to strings rather than the entities behind them, so that how a
/// date or a flag is spelled is decided once, by the report, and not three times by three writers.</summary>
public sealed class ReportDocument
{
    public required string Title { get; init; }

    /// <summary>The line under the title -- what the report answers, in a sentence.</summary>
    public string? Subtitle { get; init; }

    public required string PreparedBy { get; init; }

    public DateTime GeneratedAt { get; init; } = DateTime.Now;

    public IReadOnlyList<ReportStat> Stats { get; init; } = [];

    /// <summary>What the report was filtered to, in the reader's words ("Al Mal Capital · Q3 2026
    /// · Search "vig""). Printed on every output. A report that does not say what it excluded
    /// cannot be told apart from one that found nothing.</summary>
    public string FilterStatement { get; init; } = string.Empty;

    public required IReadOnlyList<ReportColumn> Columns { get; init; }

    public required IReadOnlyList<string[]> Rows { get; init; }

    /// <summary>A file name for a download: the report and the day it was taken, which is what
    /// makes one of these findable again in a folder of them six months later.</summary>
    public string FileName(string extension)
    {
        var safe = new string(Title.Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray());
        while (safe.Contains("--")) safe = safe.Replace("--", "-");
        return $"{safe.Trim('-')}-{GeneratedAt:yyyyMMdd}.{extension}";
    }
}
