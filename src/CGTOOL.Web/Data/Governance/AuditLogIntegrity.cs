using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using CGTOOL.Web.Data;

namespace CGTOOL.Web.Data.Governance;

/// <summary>One thing wrong with the chain, as the verify procedure reports it.</summary>
public record AuditChainProblem(int Id, long Sequence, string Problem);

/// <summary>
/// The outcome of checking the log. <see cref="Problems"/> empty and <see cref="Sealed"/> greater
/// than zero is the only state that means "verified".
/// </summary>
public record AuditChainStatus(
    long Sealed,
    long? FirstSequence,
    long? LastSequence,
    int Unsealed,
    IReadOnlyList<AuditChainProblem> Problems)
{
    public bool Verified => Problems.Count == 0 && Unsealed == 0 && Sealed > 0;

    /// <summary>Gaps are counted separately because a missing record reads differently to an
    /// altered one: the record is not there to show anybody.</summary>
    public int Gaps => Problems.Count(p => p.Problem.Contains("removed", StringComparison.OrdinalIgnoreCase));
}

public interface IAuditLogIntegrity
{
    Task<AuditChainStatus> VerifyAsync(CancellationToken ct = default);

    /// <summary>Seals any rows written before the chain existed. Safe to run more than once.</summary>
    Task<int> BackfillAsync(CancellationToken ct = default);
}

/// <summary>
/// Checks the audit log has not been altered.
///
/// The work is all in SQL (usp_AuditLog_Verify), which matters: the seal and the check share one
/// definition of the hash, so a difference in how C# and T-SQL render a date or a null cannot make
/// a sound chain look broken. This class runs it and reports what came back.
/// </summary>
public class AuditLogIntegrity(IDbContextFactory<ApplicationDbContext> dbFactory) : IAuditLogIntegrity
{
    public async Task<AuditChainStatus> VerifyAsync(CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var problems = await db.Database
            .SqlQueryRaw<AuditChainProblem>("EXEC dbo.usp_AuditLog_Verify")
            .ToListAsync(ct);

        var sealedCount = await db.AuditLogEntries.CountAsync(a => a.Sequence != null, ct);
        var unsealed = await db.AuditLogEntries.CountAsync(a => a.Sequence == null, ct);

        var first = sealedCount == 0 ? null : await db.AuditLogEntries.Where(a => a.Sequence != null).MinAsync(a => a.Sequence, ct);
        var last = sealedCount == 0 ? null : await db.AuditLogEntries.Where(a => a.Sequence != null).MaxAsync(a => a.Sequence, ct);

        return new AuditChainStatus(sealedCount, first, last, unsealed, problems);
    }

    public async Task<int> BackfillAsync(CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var sealedParam = new SqlParameter("@Sealed", System.Data.SqlDbType.Int)
        {
            Direction = System.Data.ParameterDirection.Output,
        };

        await db.Database.ExecuteSqlRawAsync("EXEC dbo.usp_AuditLog_BackfillChain @Sealed OUTPUT", [sealedParam], ct);
        return (int)sealedParam.Value!;
    }
}
