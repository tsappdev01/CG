using Microsoft.Data.SqlClient;

namespace CGTOOL.Web.Data.Governance;

public interface ITransactionWriter
{
    Task SetStatusAsync(int id, TransactionStatus status);
}

public class TransactionWriter(IStoredProcedureExecutor sp) : ITransactionWriter
{
    public Task SetStatusAsync(int id, TransactionStatus status) => sp.ExecuteAsync(
        "dbo.usp_Transaction_SetStatus",
        new SqlParameter("@Id", id), new SqlParameter("@Status", (int)status));
}
