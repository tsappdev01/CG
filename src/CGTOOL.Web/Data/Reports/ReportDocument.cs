namespace CGTOOL.Web.Data.Reports;

/// <summary>One column of a report: its heading, and the share of the sheet's width it takes.
/// Width is a fraction rather than a pixel count because the same definition is laid out three
/// times -- on an A4 landscape sheet on screen, on an A4 landscape PDF page, and in a spreadsheet
/// whose column widths are measured in characters -- and only the proportions survive all three.</summary>
/// <param name="Numeric">Written as a number rather than text in the spreadsheet, so the reader can
/// sum the column in place instead of retyping it. On the sheet and in the PDF it reads the same as
/// any other column -- these tables are read down, not totalled.</param>
public sealed record ReportColumn(string Header, double Width, bool Numeric = false);

/// <summary>How a report's subtitle should read. Warning is for a sentence the reader must not
/// skim past -- the audit trail saying its own record may have been tampered with.</summary>
public enum ReportTone { Normal, Warning }

/// <summary>One of the headline figures a report opens with.</summary>
public sealed record ReportStat(string Label, string Value, string? Sub = null);

/// <summary>A label and its value on a detail sheet -- "Employee Name", "Has NIN?".</summary>
public sealed record ReportFact(string Label, string Value);

/// <summary>A grid inside a detail sheet: the NIN holders under one declarant, the related parties
/// under one member. Its own columns, because a detail sheet carries several grids that have
/// nothing to do with each other.</summary>
/// <param name="EmptyNote">What to print instead of an empty grid. A declaration that says
/// "nothing to declare" and one that was never filled in look identical as a blank table, and they
/// are not the same thing.</param>
/// <param name="LinkColumn">Which column, if any, is a link on screen -- the uploaded document a
/// reviewer opens. The cell's text is written to stay useful without it: the PDF and the
/// spreadsheet leave the building and a relative path would not resolve from either, so the text
/// is the file's own name rather than the word "View".</param>
/// <param name="RowLinks">One href per row, in row order, or null where that row has no file.</param>
public sealed record ReportSubTable(
    string Heading,
    IReadOnlyList<ReportColumn> Columns,
    IReadOnlyList<string[]> Rows,
    string? EmptyNote = null,
    int? LinkColumn = null,
    IReadOnlyList<string?>? RowLinks = null);

/// <summary>One sheet of a detail report: a person, their facts, and the grids beneath them.
///
/// Detail reports are a different shape from the tabular ones -- the legacy Insider Submission
/// Detail ran to 304 pages, one per declarant -- so a report carries either rows or records, never
/// both.</summary>
public sealed record ReportRecord(
    string Title,
    string? Subtitle,
    IReadOnlyList<ReportFact> Facts,
    IReadOnlyList<ReportSubTable> Tables);

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

    /// <summary>Whether that line is ordinary or a warning.</summary>
    public ReportTone SubtitleTone { get; init; } = ReportTone.Normal;

    public required string PreparedBy { get; init; }

    public DateTime GeneratedAt { get; init; } = DateTime.Now;

    public IReadOnlyList<ReportStat> Stats { get; init; } = [];

    /// <summary>What the report was filtered to, in the reader's words ("Al Mal Capital · Q3 2026
    /// · Search "vig""). Printed on every output. A report that does not say what it excluded
    /// cannot be told apart from one that found nothing.</summary>
    public string FilterStatement { get; init; } = string.Empty;

    /// <summary>The columns of a tabular report. Empty on a detail report.</summary>
    public IReadOnlyList<ReportColumn> Columns { get; init; } = [];

    /// <summary>The rows of a tabular report. Empty on a detail report.</summary>
    public IReadOnlyList<string[]> Rows { get; init; } = [];

    /// <summary>The sheets of a detail report, one per person. Empty on a tabular report.</summary>
    public IReadOnlyList<ReportRecord> Records { get; init; } = [];

    /// <summary>Which of the two shapes this is. A report sets Rows or Records, never both, and
    /// every writer branches here rather than guessing from whichever list happens to be empty --
    /// a tabular report that legitimately found nothing would otherwise be drawn as a detail
    /// report with no sheets.</summary>
    public bool IsDetail => Records.Count > 0 || Columns.Count == 0;

    /// <summary>How many records or rows the report holds, for the masthead's count.</summary>
    public int Count => IsDetail ? Records.Count : Rows.Count;

    /// <summary>A file name for a download: the report and the day it was taken, which is what
    /// makes one of these findable again in a folder of them six months later.</summary>
    public string FileName(string extension)
    {
        var safe = new string(Title.Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray());
        while (safe.Contains("--")) safe = safe.Replace("--", "-");
        return $"{safe.Trim('-')}-{GeneratedAt:yyyyMMdd}.{extension}";
    }
}
