using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using CGTOOL.Web.Data;

namespace CGTOOL.Web.Data.Governance;

/// <summary>One row of the employee spreadsheet, after parsing.</summary>
public sealed record IdentityImportRow(
    string Email, string? FullName, string? CompanyName,
    string? Nin, string? NameOnEmiratesId, string? EmiratesIdNumber, string? Nationality,
    string? NameOnEmiratesIdArabic = null, DateTime? EmiratesIdExpiryDate = null);

/// <summary>What an import did, or would do.</summary>
public sealed record IdentityImportResult(
    int RowsRead,
    int Skipped,
    int ReferencesInserted,
    int ReferencesUpdated,
    int MembersMatched,
    int MembersMissing,
    int MembersCreated,
    IReadOnlyList<string> Problems)
{
    public bool DryRun { get; init; }
}

public interface IMemberIdentityImporter
{
    /// <summary>Email domains left out of the import.</summary>
    IReadOnlyList<string> ExcludedDomains { get; }

    Task<IdentityImportResult> ImportAsync(
        IReadOnlyList<IdentityImportRow> rows, string sourceFileName, bool dryRun, bool createMissingMembers,
        string actorName, CancellationToken ct = default);
}

/// <summary>
/// Loads the employee spreadsheet into the identity reference table, once.
///
/// Two things it is careful about.
///
/// It is re-runnable, and matches on email. A second run updates the rows it already created
/// rather than doubling them, so a spreadsheet corrected and loaded again does the obvious thing.
///
/// It does not create members unless asked. The file holds nearly four thousand people, and
/// creating a member for each one is not a data load -- it changes who the system believes its
/// population is, which is what declaration cycles, reminder emails and every report are counted
/// against. So the reference data loads on its own by default, and creating the missing members is
/// a separate decision taken deliberately, with a dry run that says how many there would be first.
/// </summary>
public class MemberIdentityImporter(
    IDbContextFactory<ApplicationDbContext> dbFactory,
    IAuditLogger auditLog,
    IConfiguration configuration,
    ILogger<MemberIdentityImporter> logger) : IMemberIdentityImporter
{
    /// <summary>Email domains whose people are left out of the import entirely.
    ///
    /// The spreadsheet is an employee list for the whole group, and not every company in it has
    /// anyone who declares. Loading those people would put identity records in the system for
    /// people onboarding will never ask about, which is data held for no reason.
    ///
    /// Configuration rather than a constant, because which companies are in scope is a decision
    /// that changes without the code needing to. The default is the one known today.</summary>
    public IReadOnlyList<string> ExcludedDomains =>
        configuration.GetSection("Onboarding:ExcludedEmailDomains").Get<string[]>() is { Length: > 0 } configured
            ? configured
            : ["saglass.com"];

    private bool IsExcluded(string email)
    {
        var at = email.LastIndexOf('@');
        if (at < 0) return false;

        var domain = email[(at + 1)..];
        return ExcludedDomains.Any(d => domain.Equals(d, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>The columns the file is expected to have, by header name. Matched on a normalised
    /// header rather than position, so a reordered or re-exported file still loads -- and a file
    /// that is missing one says which.</summary>


    public async Task<IdentityImportResult> ImportAsync(
        IReadOnlyList<IdentityImportRow> rows, string sourceFileName, bool dryRun, bool createMissingMembers,
        string actorName, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(rows);

        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var existingRefs = await db.MemberIdentityReferences.AsNoTracking()
            .ToDictionaryAsync(r => r.Email, r => r.Id, StringComparer.OrdinalIgnoreCase, ct);

        var memberEmails = await db.Members.AsNoTracking()
            .Where(m => m.Email != null && m.Email != "")
            .Select(m => m.Email!)
            .ToListAsync(ct);
        var members = new HashSet<string>(memberEmails, StringComparer.OrdinalIgnoreCase);

        int inserted = 0, updated = 0, matched = 0, missing = 0, skipped = 0;
        var problems = new List<string>();

        foreach (var row in rows)
        {
            // A row with neither an Emirates ID nor a NIN carries nothing onboarding can show.
            if (string.IsNullOrWhiteSpace(row.EmiratesIdNumber) && string.IsNullOrWhiteSpace(row.Nin))
            {
                skipped++;
                continue;
            }

            if (members.Contains(row.Email)) matched++;
            else missing++;

            if (row.EmiratesIdNumber is { Length: > 0 } && !LooksLikeEmiratesId(row.EmiratesIdNumber))
            {
                // A residence number rather than an Emirates ID card number. Perfectly good data
                // and loaded as it stands -- the only consequence worth saying out loud is that no
                // bulk-uploaded scan will match it, because those are named after card numbers.
                problems.Add(
                    $"{row.Email}: '{row.EmiratesIdNumber}' is not in Emirates ID card format " +
                    "(15 digits beginning 784), so no uploaded scan will match it.");
            }

            if (existingRefs.TryGetValue(row.Email, out var id))
            {
                if (!dryRun) await UpsertAsync(db, id, row, sourceFileName, ct);
                updated++;
            }
            else
            {
                if (!dryRun) await UpsertAsync(db, null, row, sourceFileName, ct);
                inserted++;
            }
        }

        var created = 0;
        if (createMissingMembers && !dryRun)
        {
            problems.Add(
                "Creating members from this file was requested but is not done here: a member needs an " +
                "entity, a declaration type and a decision about what they declare, none of which the " +
                "file carries. Load them through User Management's import instead.");
        }
        else if (createMissingMembers)
        {
            problems.Add($"{missing} people in the file have no member record. See the note on creating members.");
        }

        if (!dryRun)
        {
            await auditLog.LogAsync(actorName, AuditAction.Update, nameof(MemberIdentityReference), null,
                $"Imported identity reference data from '{sourceFileName}': " +
                $"{inserted} added, {updated} updated, {skipped} rows without an Emirates ID or NIN skipped.");
            logger.LogInformation(
                "Identity reference import: {Inserted} added, {Updated} updated, {Skipped} skipped.",
                inserted, updated, skipped);
        }

        return new IdentityImportResult(
            rows.Count, skipped, inserted, updated, matched, missing, created, problems) { DryRun = dryRun };
    }

    private static async Task UpsertAsync(
        ApplicationDbContext db, int? id, IdentityImportRow row, string sourceFileName, CancellationToken ct)
    {
        await db.Database.OpenConnectionAsync(ct);
        try
        {
            var connection = (SqlConnection)db.Database.GetDbConnection();
            using var command = connection.CreateCommand();
            command.CommandType = CommandType.StoredProcedure;
            command.CommandText = "dbo.usp_MemberIdentityReference_Upsert";
            command.Parameters.AddRange(
            [
                new SqlParameter("@Email", SqlDbType.NVarChar, 320) { Value = row.Email },
                new SqlParameter("@FullName", SqlDbType.NVarChar, 200) { Value = (object?)row.FullName ?? DBNull.Value },
                new SqlParameter("@CompanyName", SqlDbType.NVarChar, 200) { Value = (object?)row.CompanyName ?? DBNull.Value },
                new SqlParameter("@Nin", SqlDbType.NVarChar, 100) { Value = (object?)Nin.Normalise(row.Nin) ?? DBNull.Value },
                new SqlParameter("@NameOnEmiratesId", SqlDbType.NVarChar, 200) { Value = (object?)row.NameOnEmiratesId ?? DBNull.Value },
                new SqlParameter("@EmiratesIdNumber", SqlDbType.NVarChar, 100) { Value = (object?)row.EmiratesIdNumber ?? DBNull.Value },
                // Resolved, not stored as written: the card export writes "State of Palestine"
                // where the dropdown holds "Palestine", and an unresolvable spelling is better
                // left empty than saved as a nationality no screen can show.
                new SqlParameter("@Nationality", SqlDbType.NVarChar, 80) { Value = (object?)Countries.Resolve(row.Nationality) ?? DBNull.Value },
                new SqlParameter("@NameOnEmiratesIdArabic", SqlDbType.NVarChar, 200) { Value = (object?)row.NameOnEmiratesIdArabic ?? DBNull.Value },
                new SqlParameter("@EmiratesIdExpiryDate", SqlDbType.DateTime2) { Value = (object?)row.EmiratesIdExpiryDate ?? DBNull.Value },
                new SqlParameter("@SourceFileName", SqlDbType.NVarChar, 260) { Value = (object?)sourceFileName ?? DBNull.Value },
                new SqlParameter("@ImportedAtUtc", SqlDbType.DateTime2) { Value = DateTime.UtcNow },
            ]);
            await command.ExecuteNonQueryAsync(ct);
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }
    }


    /// <summary>Whether a value is in Emirates ID card format: fifteen digits beginning 784.
    /// Used only to say which rows no uploaded scan can match -- a residence number in this column
    /// is valid data and is loaded as it stands.</summary>
    private static bool LooksLikeEmiratesId(string? value) =>
        value is { Length: 15 } && value.All(char.IsDigit) && value.StartsWith("784", StringComparison.Ordinal);

}
