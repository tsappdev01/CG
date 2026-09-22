using System.Data;
using Microsoft.Data.SqlClient;

namespace CGTOOL.Web.Data.Governance;

public interface IShareholderRegisterWriter
{
    Task<int> InsertUploadAsync(DateOnly asOnDate, string fileName, int recordCount, string uploadedByName);

    Task BulkInsertRecordsAsync(int uploadId, IReadOnlyList<ShareholderRecord> records);
}

/// <summary>Records are inserted as one table-valued-parameter batch (usp_ShareholderRecord_BulkInsert)
/// rather than one stored-procedure call per row -- a Share Register upload commonly runs into the
/// tens of thousands of rows, and a round trip per row at that scale would take minutes.</summary>
public class ShareholderRegisterWriter(IStoredProcedureExecutor sp) : IShareholderRegisterWriter
{
    public Task<int> InsertUploadAsync(DateOnly asOnDate, string fileName, int recordCount, string uploadedByName) => sp.InsertAsync(
        "dbo.usp_ShareholderRegisterUpload_Insert",
        DateParam("@AsOnDate", asOnDate),
        new SqlParameter("@FileName", fileName),
        new SqlParameter("@RecordCount", recordCount),
        new SqlParameter("@UploadedByName", uploadedByName));

    // The default 30s SqlCommand timeout isn't enough for a full register upload (tens of
    // thousands of rows in one set-based INSERT) -- 10 minutes gives real production files
    // plenty of headroom without letting a genuinely stuck command hang forever.
    public Task BulkInsertRecordsAsync(int uploadId, IReadOnlyList<ShareholderRecord> records) => sp.ExecuteAsync(
        "dbo.usp_ShareholderRecord_BulkInsert",
        600,
        new SqlParameter("@ShareholderRegisterUploadId", uploadId),
        BuildTvp(records));

    private static SqlParameter DateParam(string name, DateOnly value) =>
        new(name, SqlDbType.Date) { Value = value.ToDateTime(TimeOnly.MinValue) };

    private static SqlParameter BuildTvp(IReadOnlyList<ShareholderRecord> records)
    {
        var table = new DataTable();
        table.Columns.Add("SerialNo", typeof(int));
        table.Columns.Add("Nin", typeof(string));
        table.Columns.Add("CdsUpdated", typeof(string));
        table.Columns.Add("Name", typeof(string));
        table.Columns.Add("EnglishName", typeof(string));
        table.Columns.Add("LifeStatus", typeof(string));
        table.Columns.Add("ClientType", typeof(string));
        table.Columns.Add("PassportNo", typeof(string));
        table.Columns.Add("FamilyId", typeof(string));
        table.Columns.Add("NationalId", typeof(string));
        table.Columns.Add("VisaNo", typeof(string));
        table.Columns.Add("CommercialLicenseNo", typeof(string));
        table.Columns.Add("TradeRegistrationNo", typeof(string));
        table.Columns.Add("Citizenship", typeof(string));
        table.Columns.Add("CitizenshipDescription", typeof(string));
        table.Columns.Add("PoBox", typeof(string));
        table.Columns.Add("City", typeof(string));
        table.Columns.Add("CountryCode", typeof(string));
        table.Columns.Add("CountryName", typeof(string));
        table.Columns.Add("Address1", typeof(string));
        table.Columns.Add("Address2", typeof(string));
        table.Columns.Add("Address3", typeof(string));
        table.Columns.Add("Phone1", typeof(string));
        table.Columns.Add("Phone2", typeof(string));
        table.Columns.Add("Fax", typeof(string));
        table.Columns.Add("Email", typeof(string));
        table.Columns.Add("Qty", typeof(decimal));
        table.Columns.Add("QtyPercent", typeof(decimal));
        table.Columns.Add("Frozen", typeof(decimal));
        table.Columns.Add("LastTransDate", typeof(DateTime));
        table.Columns.Add("PaymentPreference", typeof(string));
        table.Columns.Add("LinkedNinsReference", typeof(string));
        table.Columns.Add("LinkedNins", typeof(string));

        foreach (var r in records)
        {
            table.Rows.Add(
                (object?)r.SerialNo ?? DBNull.Value,
                r.Nin,
                (object?)r.CdsUpdated ?? DBNull.Value,
                (object?)r.Name ?? DBNull.Value,
                (object?)r.EnglishName ?? DBNull.Value,
                (object?)r.LifeStatus ?? DBNull.Value,
                (object?)r.ClientType ?? DBNull.Value,
                (object?)r.PassportNo ?? DBNull.Value,
                (object?)r.FamilyId ?? DBNull.Value,
                (object?)r.NationalId ?? DBNull.Value,
                (object?)r.VisaNo ?? DBNull.Value,
                (object?)r.CommercialLicenseNo ?? DBNull.Value,
                (object?)r.TradeRegistrationNo ?? DBNull.Value,
                (object?)r.Citizenship ?? DBNull.Value,
                (object?)r.CitizenshipDescription ?? DBNull.Value,
                (object?)r.PoBox ?? DBNull.Value,
                (object?)r.City ?? DBNull.Value,
                (object?)r.CountryCode ?? DBNull.Value,
                (object?)r.CountryName ?? DBNull.Value,
                (object?)r.Address1 ?? DBNull.Value,
                (object?)r.Address2 ?? DBNull.Value,
                (object?)r.Address3 ?? DBNull.Value,
                (object?)r.Phone1 ?? DBNull.Value,
                (object?)r.Phone2 ?? DBNull.Value,
                (object?)r.Fax ?? DBNull.Value,
                (object?)r.Email ?? DBNull.Value,
                r.Qty,
                r.QtyPercent,
                r.Frozen,
                (object?)r.LastTransDate?.ToDateTime(TimeOnly.MinValue) ?? DBNull.Value,
                (object?)r.PaymentPreference ?? DBNull.Value,
                (object?)r.LinkedNinsReference ?? DBNull.Value,
                (object?)r.LinkedNins ?? DBNull.Value);
        }

        return new SqlParameter("@Rows", SqlDbType.Structured) { TypeName = "dbo.ShareholderRecordTableType", Value = table };
    }
}
