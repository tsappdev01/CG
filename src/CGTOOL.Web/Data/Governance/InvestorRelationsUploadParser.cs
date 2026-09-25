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
        var table = WorkbookTable.ReadFirstSheet(fileStream);
        var headerRowIndex = WorkbookTable.FindHeaderRow(table, "nin", "serialno");
        var columns = WorkbookTable.MapColumns(table, headerRowIndex);

        var records = new List<ShareholderRecord>();
        var skipped = 0;

        for (var i = headerRowIndex + 1; i < table.Rows.Count; i++)
        {
            var row = table.Rows[i];
            var nin = WorkbookTable.Text(row, columns, "nin");
            if (string.IsNullOrWhiteSpace(nin))
            {
                if (!WorkbookTable.IsBlankRow(row)) skipped++;
                continue;
            }

            records.Add(new ShareholderRecord
            {
                SerialNo = Int(row, columns, "serialno"),
                Nin = nin.Trim(),
                CdsUpdated = WorkbookTable.Text(row, columns, "cdsupdated?"),
                Name = WorkbookTable.Text(row, columns, "name"),
                EnglishName = WorkbookTable.Text(row, columns, "englishname"),
                LifeStatus = WorkbookTable.Text(row, columns, "lifestatus"),
                ClientType = WorkbookTable.Text(row, columns, "clienttype"),
                PassportNo = WorkbookTable.Text(row, columns, "passportno"),
                FamilyId = WorkbookTable.Text(row, columns, "familyid"),
                NationalId = WorkbookTable.Text(row, columns, "nationalid"),
                VisaNo = WorkbookTable.Text(row, columns, "visano"),
                CommercialLicenseNo = WorkbookTable.Text(row, columns, "commerciallicenseno"),
                TradeRegistrationNo = WorkbookTable.Text(row, columns, "traderegistrationno"),
                Citizenship = WorkbookTable.Text(row, columns, "citizenship"),
                CitizenshipDescription = WorkbookTable.Text(row, columns, "citizenshipdescp"),
                PoBox = WorkbookTable.Text(row, columns, "pobox"),
                City = WorkbookTable.Text(row, columns, "city"),
                CountryCode = WorkbookTable.Text(row, columns, "countrycode"),
                CountryName = WorkbookTable.Text(row, columns, "countryname"),
                Address1 = WorkbookTable.Text(row, columns, "address1"),
                Address2 = WorkbookTable.Text(row, columns, "address2"),
                Address3 = WorkbookTable.Text(row, columns, "address3"),
                Phone1 = WorkbookTable.Text(row, columns, "phone1"),
                Phone2 = WorkbookTable.Text(row, columns, "phone2"),
                Fax = WorkbookTable.Text(row, columns, "fax"),
                Email = WorkbookTable.Text(row, columns, "email"),
                Qty = Decimal(row, columns, "qty"),
                QtyPercent = Decimal(row, columns, "%qty"),
                Frozen = Decimal(row, columns, "frozen"),
                LastTransDate = DateFromYyyyMmDd(WorkbookTable.Value(row, columns, "lasttransdate")),
                PaymentPreference = TextByPrefix(row, columns, "paymentpreference"),
                LinkedNinsReference = WorkbookTable.Text(row, columns, "linkedninsreference"),
                LinkedNins = WorkbookTable.Text(row, columns, "linkednins"),
            });
        }

        return new ExcelParseResult<ShareholderRecord>(records, skipped);
    }

    public static ExcelParseResult<ShareTradingRecord> ParseShareTrading(Stream fileStream)
    {
        var table = WorkbookTable.ReadFirstSheet(fileStream);
        var headerRowIndex = WorkbookTable.FindHeaderRow(table, "reportdate", "investornumber");
        var columns = WorkbookTable.MapColumns(table, headerRowIndex);

        var records = new List<ShareTradingRecord>();
        var skipped = 0;

        for (var i = headerRowIndex + 1; i < table.Rows.Count; i++)
        {
            var row = table.Rows[i];
            var nin = WorkbookTable.Text(row, columns, "investornumber");
            var reportDate = DateFromYyyyMmDd(WorkbookTable.Value(row, columns, "reportdate"));
            if (string.IsNullOrWhiteSpace(nin) || reportDate is null)
            {
                if (!WorkbookTable.IsBlankRow(row)) skipped++;
                continue;
            }

            records.Add(new ShareTradingRecord
            {
                ReportDate = reportDate.Value,
                Symbol = WorkbookTable.Text(row, columns, "symbol") ?? string.Empty,
                Nin = nin.Trim(),
                InvestorName = WorkbookTable.Text(row, columns, "investorname"),
                ClientType = WorkbookTable.Text(row, columns, "clienttype"),
                Nationality = WorkbookTable.Text(row, columns, "nationality"),
                PreviousOwnQty = Decimal(row, columns, "previousownqty"),
                CurrentOwnQty = Decimal(row, columns, "currentownqty"),
                OwnedQtyChange = Decimal(row, columns, "ownedqtychange"),
            });
        }

        return new ExcelParseResult<ShareTradingRecord>(records, skipped);
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
        var v = WorkbookTable.Value(row, columns, key);
        return v is null ? null : (int)Math.Round(Convert.ToDouble(v, CultureInfo.InvariantCulture));
    }

    private static decimal Decimal(DataRow row, Dictionary<string, int> columns, string key)
    {
        var v = WorkbookTable.Value(row, columns, key);
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
