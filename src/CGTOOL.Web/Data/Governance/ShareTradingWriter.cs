using System.Data;
using Microsoft.Data.SqlClient;

namespace CGTOOL.Web.Data.Governance;

public interface IShareTradingWriter
{
    Task<int> InsertUploadAsync(string fileName, int recordCount, string uploadedByName);

    Task BulkInsertRecordsAsync(int uploadId, IReadOnlyList<ShareTradingRecord> records);
}

/// <summary>Same table-valued-parameter bulk insert approach as ShareholderRegisterWriter, for the
/// same reason -- a DFM trading extract can carry thousands of rows per upload.</summary>
public class ShareTradingWriter(IStoredProcedureExecutor sp) : IShareTradingWriter
{
    public Task<int> InsertUploadAsync(string fileName, int recordCount, string uploadedByName) => sp.InsertAsync(
        "dbo.usp_ShareTradingUpload_Insert",
        new SqlParameter("@FileName", fileName),
        new SqlParameter("@RecordCount", recordCount),
        new SqlParameter("@UploadedByName", uploadedByName));

    // See ShareholderRegisterWriter.BulkInsertRecordsAsync for why this needs a longer-than-default
    // command timeout.
    public Task BulkInsertRecordsAsync(int uploadId, IReadOnlyList<ShareTradingRecord> records) => sp.ExecuteAsync(
        "dbo.usp_ShareTradingRecord_BulkInsert",
        600,
        new SqlParameter("@ShareTradingUploadId", uploadId),
        BuildTvp(records));

    private static SqlParameter BuildTvp(IReadOnlyList<ShareTradingRecord> records)
    {
        var table = new DataTable();
        table.Columns.Add("ReportDate", typeof(DateTime));
        table.Columns.Add("Symbol", typeof(string));
        table.Columns.Add("Nin", typeof(string));
        table.Columns.Add("InvestorName", typeof(string));
        table.Columns.Add("ClientType", typeof(string));
        table.Columns.Add("Nationality", typeof(string));
        table.Columns.Add("PreviousOwnQty", typeof(decimal));
        table.Columns.Add("CurrentOwnQty", typeof(decimal));
        table.Columns.Add("OwnedQtyChange", typeof(decimal));

        foreach (var r in records)
        {
            table.Rows.Add(
                r.ReportDate.ToDateTime(TimeOnly.MinValue),
                r.Symbol,
                r.Nin,
                (object?)r.InvestorName ?? DBNull.Value,
                (object?)r.ClientType ?? DBNull.Value,
                (object?)r.Nationality ?? DBNull.Value,
                r.PreviousOwnQty,
                r.CurrentOwnQty,
                r.OwnedQtyChange);
        }

        return new SqlParameter("@Rows", SqlDbType.Structured) { TypeName = "dbo.ShareTradingRecordTableType", Value = table };
    }
}
