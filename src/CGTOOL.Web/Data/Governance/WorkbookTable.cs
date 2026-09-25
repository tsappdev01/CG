using System.Data;
using System.Globalization;
using ExcelDataReader;

namespace CGTOOL.Web.Data.Governance;

/// <summary>
/// Reading a spreadsheet upload as a table: find the header row by its contents, map columns by
/// name, and pull typed values out of a row.
///
/// Columns are addressed by normalized header text rather than position, so a file with its columns
/// reordered, re-worded slightly, or preceded by banner rows still parses. These were the Investor
/// Relations parser's private helpers; the user-list import needs exactly the same ones, so they
/// live here now and both call them.
/// </summary>
public static class WorkbookTable
{
    /// <summary>The file name only decides which reader to use -- a .csv is a perfectly good way to
    /// hand over a list of people, and refusing one would be arbitrary.</summary>
    public static DataTable ReadFirstSheet(Stream fileStream, string? fileName = null)
    {
        using var reader = fileName is not null && fileName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase)
            ? ExcelReaderFactory.CreateCsvReader(fileStream)
            : ExcelReaderFactory.CreateReader(fileStream);
        var dataSet = reader.AsDataSet();
        if (dataSet.Tables.Count == 0) throw new InvalidDataException("The uploaded file has no worksheets.");
        return dataSet.Tables[0];
    }

    /// <summary>Scans the first 30 rows for the one containing every marker column (normalized,
    /// case-insensitive) -- source files carry a handful of title/date banner rows above the real
    /// header, so row 0 can't be assumed.</summary>
    public static int FindHeaderRow(DataTable table, params string[] markers)
    {
        var scanLimit = Math.Min(30, table.Rows.Count);
        for (var i = 0; i < scanLimit; i++)
        {
            var normalized = table.Rows[i].ItemArray
                .Select(v => Normalize(v?.ToString()))
                .ToHashSet();
            if (markers.All(m => normalized.Contains(m))) return i;
        }
        throw new InvalidDataException("Could not find the header row -- expected columns " + string.Join(", ", markers) + " were not found in the first 30 rows.");
    }

    public static Dictionary<string, int> MapColumns(DataTable table, int headerRowIndex)
    {
        var headerRow = table.Rows[headerRowIndex];
        var map = new Dictionary<string, int>();
        for (var col = 0; col < table.Columns.Count; col++)
        {
            var key = Normalize(headerRow[col]?.ToString());
            if (key.Length > 0 && !map.ContainsKey(key)) map[key] = col;
        }
        return map;
    }

    public static string Normalize(string? header) =>
        (header ?? string.Empty).Trim().ToLowerInvariant().Replace(" ", "").Replace("-", "").Replace(".", "");

    public static bool IsBlankRow(DataRow row) => row.ItemArray.All(v => v is null or DBNull || string.IsNullOrWhiteSpace(v.ToString()));

    public static object? Value(DataRow row, Dictionary<string, int> columns, string key) =>
        columns.TryGetValue(key, out var col) && row[col] is not DBNull ? row[col] : null;

    public static string? Text(DataRow row, Dictionary<string, int> columns, string key)
    {
        var v = Convert.ToString(Value(row, columns, key), CultureInfo.InvariantCulture)?.Trim();
        return string.IsNullOrEmpty(v) ? null : v;
    }

    /// <summary>The first of several candidate header names that the file actually has -- so a sheet
    /// saying "Email Address" and one saying "Email" both work without the caller caring.</summary>
    public static string? TextAny(DataRow row, Dictionary<string, int> columns, params string[] keys)
    {
        foreach (var key in keys)
        {
            var v = Text(row, columns, key);
            if (v is not null) return v;
        }
        return null;
    }

    public static bool HasAny(Dictionary<string, int> columns, params string[] keys) => keys.Any(columns.ContainsKey);
}
