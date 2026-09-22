using Microsoft.Data.SqlClient;

namespace CGTOOL.Web.Data.Governance;

public interface IAuditLogger
{
    Task LogAsync(string actorDisplayName, AuditAction action, string entityType, string? entityId, string? details, string? actingOnBehalfOf = null, string? justification = null);
}

/// <summary>Auto-attributes every entry with the circuit's captured IP/user agent (see ClientContext).
/// Inserts via dbo.usp_AuditLogEntry_Insert (see scripts/stored-procedures.sql).</summary>
public class AuditLogger(IStoredProcedureExecutor sp, ClientContext clientContext) : IAuditLogger
{
    public async Task LogAsync(string actorDisplayName, AuditAction action, string entityType, string? entityId, string? details, string? actingOnBehalfOf = null, string? justification = null)
    {
        await sp.InsertAsync("dbo.usp_AuditLogEntry_Insert",
            new SqlParameter("@ActorDisplayName", actorDisplayName),
            new SqlParameter("@ActingOnBehalfOf", (object?)actingOnBehalfOf ?? DBNull.Value),
            new SqlParameter("@Action", (int)action),
            new SqlParameter("@EntityType", entityType),
            new SqlParameter("@EntityId", (object?)entityId ?? DBNull.Value),
            new SqlParameter("@Details", (object?)details ?? DBNull.Value),
            new SqlParameter("@Justification", (object?)justification ?? DBNull.Value),
            new SqlParameter("@IpAddress", (object?)clientContext.IpAddress ?? DBNull.Value),
            new SqlParameter("@UserAgent", (object?)clientContext.UserAgent ?? DBNull.Value));
    }
}
