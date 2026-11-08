using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using CGTOOL.Web.Data;

namespace CGTOOL.Web.Data.Governance;

/// <summary>One row of the employee spreadsheet, after parsing.</summary>
public sealed record IdentityImportRow(
    string Email, string? FullName, string? CompanyName,
    string? Nin, string? NameOnEmiratesId, string? EmiratesIdNumber, string? Nationality);

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
    IReadOnlyList<IdentityImportRow> Parse(Stream workbook, out IReadOnlyList<string> problems);

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
    ILogger<MemberIdentityImporter> logger) : IMemberIdentityImporter
{
    /// <summary>The columns the file is expected to have, by header name. Matched on a normalised
    /// header rather than position, so a reordered or re-exported file still loads -- and a file
    /// that is missing one says which.</summary>
    private static readonly (string Key, string[] Accepts)[] Headers =
    [
        ("email", ["emailaddress", "email"]),
        ("name", ["name", "fullname"]),
        ("company", ["company", "companyname"]),
        ("nin", ["nin", "ninnumber"]),
        ("nameonid", ["nameonemiratesid", "nameonid"]),
        ("emiratesid", ["emiratesid", "emiratesidnumber"]),
        ("nationality", ["nationality"]),
    ];

    public IReadOnlyList<IdentityImportRow> Parse(Stream workbook, out IReadOnlyList<string> problems)
    {
        ArgumentNullException.ThrowIfNull(workbook);

        var found = new List<string>();
        var rows = new List<IdentityImportRow>();

        using var book = new ClosedXML.Excel.XLWorkbook(workbook);
        var sheet = book.Worksheets.First();
        var used = sheet.RangeUsed();
        if (used is null)
        {
            problems = ["The sheet is empty."];
            return rows;
        }

        // Header row: the first row of the used range.
        var headerRow = used.FirstRow();
        var columns = new Dictionary<string, int>();
        foreach (var cell in headerRow.Cells())
        {
            var normalised = Normalise(cell.GetString());
            foreach (var (key, accepts) in Headers)
            {
                // First match wins, so the file's duplicate "Name" column does not displace the
                // one already mapped.
                if (!columns.ContainsKey(key) && accepts.Contains(normalised))
                {
                    columns[key] = cell.Address.ColumnNumber;
                }
            }
        }

        if (!columns.ContainsKey("email"))
        {
            problems = ["No email column was found. The file needs a column headed \"EmailAddress\"."];
            return rows;
        }

        foreach (var key in new[] { "nin", "emiratesid" })
        {
            if (!columns.ContainsKey(key))
            {
                found.Add($"No \"{key}\" column was found, so that half of the reference data will be blank.");
            }
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in used.RowsUsed().Skip(1))
        {
            var email = Value(row, columns, "email");
            if (string.IsNullOrWhiteSpace(email)) continue;

            email = email.Trim();
            if (!seen.Add(email))
            {
                // The last row for an address would otherwise silently win. Saying so is cheap and
                // the duplicate is usually a mistake in the file.
                found.Add($"Row {row.RowNumber()}: '{email}' appears more than once; the first row is used.");
                continue;
            }

            rows.Add(new IdentityImportRow(
                email,
                Value(row, columns, "name"),
                Value(row, columns, "company"),
                Value(row, columns, "nin"),
                Value(row, columns, "nameonid"),
                Value(row, columns, "emiratesid"),
                Value(row, columns, "nationality")));
        }

        problems = found;
        return rows;
    }

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
                // Loaded anyway -- it is what is held for that person, and the confirmation step is
                // exactly where a wrong one should surface. Worth saying out loud, though: a number
                // in this shape will never match one of the bulk-uploaded scans, which are named
                // after fifteen-digit numbers.
                problems.Add(
                    $"{row.Email}: '{row.EmiratesIdNumber}' is not a 15-digit Emirates ID beginning 784. " +
                    "It is loaded, but no uploaded card will match it.");
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
                new SqlParameter("@Nin", SqlDbType.NVarChar, 100) { Value = (object?)row.Nin ?? DBNull.Value },
                new SqlParameter("@NameOnEmiratesId", SqlDbType.NVarChar, 200) { Value = (object?)row.NameOnEmiratesId ?? DBNull.Value },
                new SqlParameter("@EmiratesIdNumber", SqlDbType.NVarChar, 100) { Value = (object?)row.EmiratesIdNumber ?? DBNull.Value },
                new SqlParameter("@Nationality", SqlDbType.NVarChar, 80) { Value = (object?)row.Nationality ?? DBNull.Value },
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

    /// <summary>A cell's value, or null when there is nothing in it.
    ///
    /// "Nothing in it" includes the literal text NULL. The file is a SQL export and writes NULL
    /// where a value is missing, in 3,726 of its 3,961 rows -- so a reader that only checks for an
    /// empty string loads the word itself, and onboarding then asks somebody to confirm that their
    /// National Investor Number is "NULL".</summary>
    private static string? Value(ClosedXML.Excel.IXLRangeRow row, Dictionary<string, int> columns, string key)
    {
        if (!columns.TryGetValue(key, out var column)) return null;

        var raw = row.Cell(column).GetString()?.Trim();
        if (string.IsNullOrEmpty(raw)) return null;

        return raw.Equals("NULL", StringComparison.OrdinalIgnoreCase) ? null : raw;
    }

    /// <summary>Whether a value looks like an Emirates ID number: fifteen digits beginning 784.
    /// Used to report the ones that do not rather than to refuse them -- the number is still what
    /// is on file for that person, and onboarding asking them to confirm it is how a wrong one
    /// gets found.</summary>
    private static bool LooksLikeEmiratesId(string? value) =>
        value is { Length: 15 } && value.All(char.IsDigit) && value.StartsWith("784", StringComparison.Ordinal);

    /// <summary>Lowercase, with spaces, hyphens and dots removed -- the same rule the Investor
    /// Relations workbook parser uses, so "Emirates ID", "EmiratesID" and "emirates-id" all land
    /// on the same column.</summary>
    private static string Normalise(string? header) =>
        new([.. (header ?? string.Empty).ToLowerInvariant().Where(c => c is not (' ' or '-' or '.' or '_'))]);
}
