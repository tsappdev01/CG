using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using CGTOOL.Web.Data;

namespace CGTOOL.Web.Data.Governance;

/// <summary>Runs the app's write-path stored procedures (see scripts/stored-procedures.sql) against the
/// same connection/transaction the current DbContext is using.</summary>
public interface IStoredProcedureExecutor
{
    /// <summary>Runs an insert procedure that returns the new row's identity via an "@NewId int OUTPUT" parameter.</summary>
    Task<int> InsertAsync(string procedureName, params SqlParameter[] parameters);

    /// <summary>Runs a procedure with no output value (update/delete/set-active).</summary>
    Task ExecuteAsync(string procedureName, params SqlParameter[] parameters);

    /// <summary>Same as ExecuteAsync, but with a longer-than-default command timeout -- for
    /// bulk/table-valued-parameter procedures (e.g. Investor Relations upload inserts) whose row
    /// counts can run well past what the default 30s SqlCommand timeout allows.</summary>
    Task ExecuteAsync(string procedureName, int commandTimeoutSeconds, params SqlParameter[] parameters);
}

public class StoredProcedureExecutor(ApplicationDbContext db) : IStoredProcedureExecutor
{
    public async Task<int> InsertAsync(string procedureName, params SqlParameter[] parameters)
    {
        var newIdParam = new SqlParameter("@NewId", SqlDbType.Int) { Direction = ParameterDirection.Output };
        await ExecuteAsync(procedureName, [.. parameters, newIdParam]);
        return (int)newIdParam.Value!;
    }

    public Task ExecuteAsync(string procedureName, params SqlParameter[] parameters) =>
        ExecuteInternalAsync(procedureName, null, parameters);

    public Task ExecuteAsync(string procedureName, int commandTimeoutSeconds, params SqlParameter[] parameters) =>
        ExecuteInternalAsync(procedureName, commandTimeoutSeconds, parameters);

    // Database.OpenConnectionAsync/CloseConnectionAsync (not the raw SqlConnection's own
    // OpenAsync/CloseAsync) go through EF Core's reference-counted connection management. This
    // DbContext is scoped to the whole Blazor Server circuit and reused across every page/component
    // that runs during it, so EF's own LINQ queries can legitimately be opening/closing this same
    // connection around the same time as this method. Calling the raw connection's OpenAsync/CloseAsync
    // directly (as this used to) bypasses that ref-count entirely: whichever side closes the
    // connection first pulls it out from under the other, throwing "Invalid operation. The connection
    // is closed" on whichever command was still in flight. OpenConnectionAsync/CloseConnectionAsync
    // instead only actually close the connection once every open reference (ours and EF's) has been
    // released, so overlapping/nested usage is safe.
    private async Task ExecuteInternalAsync(string procedureName, int? commandTimeoutSeconds, SqlParameter[] parameters)
    {
        await db.Database.OpenConnectionAsync();
        try
        {
            var connection = (SqlConnection)db.Database.GetDbConnection();
            using var command = connection.CreateCommand();
            command.CommandType = CommandType.StoredProcedure;
            command.CommandText = procedureName;
            if (commandTimeoutSeconds is not null)
            {
                command.CommandTimeout = commandTimeoutSeconds.Value;
            }

            var currentTransaction = db.Database.CurrentTransaction;
            if (currentTransaction is not null)
            {
                command.Transaction = (SqlTransaction)currentTransaction.GetDbTransaction();
            }

            foreach (var parameter in parameters)
            {
                command.Parameters.Add(parameter);
            }

            await command.ExecuteNonQueryAsync();
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }
    }
}
