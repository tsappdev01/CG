using System.Data;
using System.Globalization;
using ExcelDataReader;

namespace CGTOOL.Web.Data.Governance;

/// <summary>One parsed .xlsx upload -- Records holds every row that had a usable NIN;
/// SkippedRows counts rows under the header that were blank or missing a NIN and were left out.</summary>
public record ExcelParseResult<T>(List<T> Records, int SkippedRows);

/// <summary>Parses the two Investor Relations upload formats. Both source files carry a few banner
/// rows before the real header (title/date/total-shares lines for the Share Register, none observed
/// for the DFM extract but tolerated all the same), so the header row is located by content --
/// matching known column names -- rather than assumed to be row 1. Columns are then read by matching
/// normalized header text rather than fixed position, so a reordered or slightly re-worded export
/// still parses.</summary>
public static class InvestorRelationsUploadParser
{
    public static ExcelParseResult<ShareholderRecord> ParseShareholderRegister(Stream fileStream)
    {
        var table = ReadFirstSheet(fileStream);
        var headerRowIndex = FindHeaderRow(table, "nin", "serialno");
        var columns = MapColumns(table, headerRowIndex);

        var records = new List<ShareholderRecord>();
        var skipped = 0;

        for (var i = headerRowIndex + 1; i < table.Rows.Count; i++)
        {
            var row = table.Rows[i];
            var nin = Text(row, columns, "nin");
            if (string.IsNullOrWhiteSpace(nin))
            {
                if (!IsBlankRow(row)) skipped++;
                continue;
            }

            records.Add(new ShareholderRecord
            {
                SerialNo = Int(row, columns, "serialno"),
                Nin = nin.Trim(),
                CdsUpdated = Text(row, columns, "cdsupdated?"),
                Name = Text(row, columns, "name"),
                EnglishName = Text(row, columns, "englishname"),
                LifeStatus = Text(row, columns, "lifestatus"),
                ClientType = Text(row, columns, "clienttype"),
                PassportNo = Text(row, columns, "passportno"),
                FamilyId = Text(row, columns, "familyid"),
                NationalId = Text(row, columns, "nationalid"),
                VisaNo = Text(row, columns, "visano"),
                CommercialLicenseNo = Text(row, columns, "commerciallicenseno"),
                TradeRegistrationNo = Text(row, columns, "traderegistrationno"),
                Citizenship = Text(row, columns, "citizenship"),
                CitizenshipDescription = Text(row, columns, "citizenshipdescp"),
                PoBox = Text(row, columns, "pobox"),
                City = Text(row, columns, "city"),
                CountryCode = Text(row, columns, "countrycode"),
                CountryName = Text(row, columns, "countryname"),
                Address1 = Text(row, columns, "address1"),
                Address2 = Text(row, columns, "address2"),
                Address3 = Text(row, columns, "address3"),
                Phone1 = Text(row, columns, "phone1"),
                Phone2 = Text(row, columns, "phone2"),
                Fax = Text(row, columns, "fax"),
                Email = Text(row, columns, "email"),
                Qty = Decimal(row, columns, "qty"),
                QtyPercent = Decimal(row, columns, "%qty"),
                Frozen = Decimal(row, columns, "frozen"),
                LastTransDate = DateFromYyyyMmDd(Value(row, columns, "lasttransdate")),
                PaymentPreference = TextByPrefix(row, columns, "paymentpreference"),
                LinkedNinsReference = Text(row, columns, "linkedninsreference"),
                LinkedNins = Text(row, columns, "linkednins"),
            });
        }

        return new ExcelParseResult<ShareholderRecord>(records, skipped);
    }

    public static ExcelParseResult<ShareTradingRecord> ParseShareTrading(Stream fileStream)
    {
        var table = ReadFirstSheet(fileStream);
        var headerRowIndex = FindHeaderRow(table, "reportdate", "investornumber");
        var columns = MapColumns(table, headerRowIndex);

        var records = new List<ShareTradingRecord>();
        var skipped = 0;

        for (var i = headerRowIndex + 1; i < table.Rows.Count; i++)
        {
            var row = table.Rows[i];
            var nin = Text(row, columns, "investornumber");
            var reportDate = DateFromYyyyMmDd(Value(row, columns, "reportdate"));
            if (string.IsNullOrWhiteSpace(nin) || reportDate is null)
            {
                if (!IsBlankRow(row)) skipped++;
                continue;
            }

            records.Add(new ShareTradingRecord
            {
                ReportDate = reportDate.Value,
                Symbol = Text(row, columns, "symbol") ?? string.Empty,
                Nin = nin.Trim(),
                InvestorName = Text(row, columns, "investorname"),
                ClientType = Text(row, columns, "clienttype"),
                Nationality = Text(row, columns, "nationality"),
                PreviousOwnQty = Decimal(row, columns, "previousownqty"),
                CurrentOwnQty = Decimal(row, columns, "currentownqty"),
                OwnedQtyChange = Decimal(row, columns, "ownedqtychange"),
            });
        }

        return new ExcelParseResult<ShareTradingRecord>(records, skipped);
    }

    private static DataTable ReadFirstSheet(Stream fileStream)
    {
        using var reader = ExcelReaderFactory.CreateReader(fileStream);
        var dataSet = reader.AsDataSet();
        if (dataSet.Tables.Count == 0) throw new InvalidDataException("The uploaded file has no worksheets.");
        return dataSet.Tables[0];
    }

    /// <summary>Scans the first 30 rows for the one containing every marker column (normalized,
    /// case-insensitive) -- source files carry a handful of title/date banner rows above the real
    /// header, so row 0 can't be assumed.</summary>
    private static int FindHeaderRow(DataTable table, params string[] markers)
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

    private static Dictionary<string, int> MapColumns(DataTable table, int headerRowIndex)
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

    private static string Normalize(string? header) =>
        (header ?? string.Empty).Trim().ToLowerInvariant().Replace(" ", "").Replace("-", "").Replace(".", "");

    private static bool IsBlankRow(DataRow row) => row.ItemArray.All(v => v is null or DBNull || string.IsNullOrWhiteSpace(v.ToString()));

    private static object? Value(DataRow row, Dictionary<string, int> columns, string key) =>
        columns.TryGetValue(key, out var col) && row[col] is not DBNull ? row[col] : null;

    private static string? Text(DataRow row, Dictionary<string, int> columns, string key)
    {
        var v = Convert.ToString(Value(row, columns, key), CultureInfo.InvariantCulture)?.Trim();
        return string.IsNullOrEmpty(v) ? null : v;
    }

    /// <summary>For the one header whose text carries a variable date suffix ("Payment Preference as
    /// on 16/04/2019") -- matches by normalized-key prefix instead of an exact key.</summary>
    private static string? TextByPrefix(DataRow row, Dictionary<string, int> columns, string prefix)
    {
        var col = columns.Keys.FirstOrDefault(k => k.StartsWith(prefix, StringComparison.Ordinal));
        if (col is null) return null;
        var v = Convert.ToString(row[columns[col]] is DBNull ? null : row[columns[col]], CultureInfo.InvariantCulture)?.Trim();
        return string.IsNullOrEmpty(v) ? null : v;
    }

    private static int? Int(DataRow row, Dictionary<string, int> columns, string key)
    {
        var v = Value(row, columns, key);
        return v is null ? null : (int)Math.Round(Convert.ToDouble(v, CultureInfo.InvariantCulture));
    }

    private static decimal Decimal(DataRow row, Dictionary<string, int> columns, string key)
    {
        var v = Value(row, columns, key);
        if (v is null) return 0m;
        if (v is IConvertible) return Convert.ToDecimal(v, CultureInfo.InvariantCulture);
        return decimal.TryParse(Convert.ToString(v, CultureInfo.InvariantCulture), NumberStyles.Any, CultureInfo.InvariantCulture, out var d) ? d : 0m;
    }

    /// <summary>Both source files encode dates as an 8-digit yyyyMMdd number/string rather than a
    /// real Excel date, except when ExcelDataReader itself already resolved a formatted date cell to
    /// a DateTime.</summary>
    private static DateOnly? DateFromYyyyMmDd(object? value)
    {
        switch (value)
        {
            case null:
                return null;
            case DateTime dt:
                return DateOnly.FromDateTime(dt);
        }

        var text = Convert.ToString(value, CultureInfo.InvariantCulture)?.Trim();
        if (string.IsNullOrEmpty(text)) return null;
        if (text.Contains('.')) text = text[..text.IndexOf('.')];
        return text.Length == 8 && DateOnly.TryParseExact(text, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
            ? parsed
            : null;
    }
}
