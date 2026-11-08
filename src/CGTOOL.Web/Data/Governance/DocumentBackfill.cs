using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using CGTOOL.Web.Data;

namespace CGTOOL.Web.Data.Governance;

/// <summary>What one pass over one column did.</summary>
public sealed record DocumentBackfillColumn(
    string Table, string PathColumn, int Rows, int Imported, int Missing, int AlreadyDone)
{
    public bool NeedsAttention => Missing > 0;
}

public sealed record DocumentBackfillReport(IReadOnlyList<DocumentBackfillColumn> Columns)
{
    public int Imported => Columns.Sum(c => c.Imported);
    public int Missing => Columns.Sum(c => c.Missing);
    public int AlreadyDone => Columns.Sum(c => c.AlreadyDone);
}

public interface IDocumentBackfill
{
    Task<DocumentBackfillReport> RunAsync(bool dryRun, CancellationToken ct = default);
}

/// <summary>
/// Moves the documents that are still files under wwwroot into dbo.DocumentBlobs.
///
/// Uploads write to the database from here on, so this only has to deal with what was already on
/// disk when that changed. It reads each row's path column, loads the file, stores it, and points
/// the row's blob column at it.
///
/// Three things it deliberately does:
///
/// It is re-runnable. A row whose blob column is already set is skipped, so a run that fails
/// halfway can simply be run again, and running it twice does not store anything twice.
///
/// It leaves the path column alone. The file stays where it is and the row keeps pointing at it
/// until someone has checked the documents open from the database; dropping the paths is a
/// separate change, deliberately after that.
///
/// It expects to find files missing, and says so rather than failing. A path in the database and a
/// file under wwwroot/uploads come apart whenever a database is shared between machines -- a
/// restored backup, a second web server, a developer's clone pointed at UAT. That is the whole
/// reason ProfilePictureResolver exists. Those rows are counted and reported per column, because
/// "this many documents could not be found" is a fact somebody has to see, not an error to stop on.
/// </summary>
public class DocumentBackfill(
    IDbContextFactory<ApplicationDbContext> dbFactory,
    IWebHostEnvironment environment,
    IDocumentProtector protector,
    ILogger<DocumentBackfill> logger) : IDocumentBackfill
{
    /// <summary>Every place a document is attached: the table, the column holding the old path, and
    /// the column that should end up holding the blob id.</summary>
    private static readonly (string Table, string PathColumn, string BlobColumn)[] Columns =
    [
        ("MemberDocuments", "FilePath", "BlobId"),
        ("InsiderDeclarations", "EmiratesIdPath", "EmiratesIdBlobId"),
        ("InsiderDeclarations", "PassportPath", "PassportBlobId"),
        ("InsiderDeclarations", "TradeLicencePath", "TradeLicenceBlobId"),
        ("InsiderDeclarations", "OtherDocumentPath", "OtherDocumentBlobId"),
        ("InsiderDeclarationDocuments", "FilePath", "BlobId"),
        ("RelatedPartyCoiDeclarations", "SignaturePath", "SignatureBlobId"),
        ("RelatedPartyCoiDeclarationDocuments", "FilePath", "BlobId"),
        ("CoiTradeLicenseDocuments", "FilePath", "BlobId"),
        ("RelatedPartyTransactionDocuments", "FilePath", "BlobId"),
        ("FamilyMembers", "EmiratesIdPath", "EmiratesIdBlobId"),
        ("FamilyMembers", "PassportPath", "PassportBlobId"),
        ("FamilyMembers", "TradeLicencePath", "TradeLicenceBlobId"),
        ("FamilyMemberHoldings", "TradeLicencePath", "TradeLicenceBlobId"),
        ("OwnedCompanies", "TradeLicensePath", "TradeLicenseBlobId"),
        ("OwnedCompanies", "MoaPath", "MoaBlobId"),
        ("OwnedCompanies", "PoaPath", "PoaBlobId"),
        ("PolicyDocumentVersions", "FilePath", "BlobId"),
        ("ShareholderRegisterUploads", "FilePath", "BlobId"),
        ("Companies", "LogoPath", "LogoBlobId"),
        ("AspNetUsers", "ProfilePicturePath", "ProfilePictureBlobId"),
    ];

    public async Task<DocumentBackfillReport> RunAsync(bool dryRun, CancellationToken ct = default)
    {
        var results = new List<DocumentBackfillColumn>();

        foreach (var (table, pathColumn, blobColumn) in Columns)
        {
            results.Add(await RunColumnAsync(table, pathColumn, blobColumn, dryRun, ct));
        }

        var report = new DocumentBackfillReport(results);
        logger.LogInformation(
            "Document backfill {Mode}: {Imported} imported, {Missing} files not found, {Already} already done.",
            dryRun ? "(dry run)" : "complete", report.Imported, report.Missing, report.AlreadyDone);

        return report;
    }

    private async Task<DocumentBackfillColumn> RunColumnAsync(
        string table, string pathColumn, string blobColumn, bool dryRun, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        // The identity column is "Id" everywhere except AspNetUsers, whose key is a string.
        var keyIsText = table == "AspNetUsers";

        // Table and column names are not parameters -- they cannot be. They come from the array
        // above, which is source code, never from a caller or a row, so there is nothing here that
        // an input could steer.
        var select = $"SELECT Id, {pathColumn} FROM dbo.{table} " +
                     $"WHERE {blobColumn} IS NULL AND {pathColumn} IS NOT NULL AND LEN({pathColumn}) > 0";

        var pending = new List<(object Id, string Path)>();
        var alreadyDone = 0;

        await db.Database.OpenConnectionAsync(ct);
        try
        {
            var connection = (SqlConnection)db.Database.GetDbConnection();

            using (var countCommand = connection.CreateCommand())
            {
                countCommand.CommandText =
                    $"SELECT COUNT(*) FROM dbo.{table} WHERE {blobColumn} IS NOT NULL";
                alreadyDone = Convert.ToInt32(await countCommand.ExecuteScalarAsync(ct));
            }

            using var command = connection.CreateCommand();
            command.CommandText = select;
            await using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                object id = keyIsText ? reader.GetString(0) : reader.GetInt32(0);
                pending.Add((id, reader.GetString(1)));
            }
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }

        var imported = 0;
        var missing = 0;

        foreach (var (id, storedPath) in pending)
        {
            var file = ResolveFile(storedPath);
            if (file is null)
            {
                missing++;
                logger.LogWarning(
                    "Backfill: {Table}.{Column} row {Id} points at '{Path}', which is not on this server.",
                    table, pathColumn, id, storedPath);
                continue;
            }

            if (dryRun)
            {
                imported++;
                continue;
            }

            var content = await File.ReadAllBytesAsync(file, ct);
            var blobId = await InsertBlobAsync(db, content, Path.GetFileName(file), ContentTypeOf(file), ct);
            await SetBlobIdAsync(db, table, blobColumn, id, blobId, keyIsText, ct);
            imported++;
        }

        return new DocumentBackfillColumn(table, pathColumn, pending.Count, imported, missing, alreadyDone);
    }

    /// <summary>The file a stored path points at, or null when it is not there.
    ///
    /// Contained to wwwroot: these values come from database rows, and a row reading "../.." should
    /// not make this read something else off the server. The same check the report builder and the
    /// picture resolver each had to make for themselves.</summary>
    private string? ResolveFile(string storedPath)
    {
        var relative = storedPath.Split('?', 2)[0].TrimStart('/', '\\');
        if (relative.Length == 0) return null;

        var webRoot = Path.GetFullPath(environment.WebRootPath);
        var full = Path.GetFullPath(Path.Combine(webRoot, relative.Replace('/', Path.DirectorySeparatorChar)));

        if (!full.StartsWith(webRoot + Path.DirectorySeparatorChar, StringComparison.Ordinal)) return null;
        return File.Exists(full) ? full : null;
    }

    private async Task<int> InsertBlobAsync(
        ApplicationDbContext db, byte[] content, string fileName, string contentType, CancellationToken ct)
    {
        await db.Database.OpenConnectionAsync(ct);
        try
        {
            var connection = (SqlConnection)db.Database.GetDbConnection();
            using var command = connection.CreateCommand();
            command.CommandType = CommandType.StoredProcedure;
            command.CommandText = "dbo.usp_DocumentBlob_Insert";
            command.Parameters.AddRange(
            [
                new SqlParameter("@FileName", SqlDbType.NVarChar, 255) { Value = fileName },
                new SqlParameter("@ContentType", SqlDbType.NVarChar, 100) { Value = contentType },
                new SqlParameter("@SizeBytes", SqlDbType.BigInt) { Value = (long)content.Length },
                new SqlParameter("@ContentProtected", SqlDbType.VarBinary, -1) { Value = protector.Protect(content) },
                new SqlParameter("@ProtectionScheme", SqlDbType.NVarChar, 20) { Value = protector.Scheme },
                new SqlParameter("@ContentSha256", SqlDbType.NVarChar, 64) { Value = IDocumentProtector.Sha256Hex(content) },
                new SqlParameter("@IpAddress", SqlDbType.NVarChar, 64) { Value = DBNull.Value },
                new SqlParameter("@UploadedAtUtc", SqlDbType.DateTime2) { Value = DateTime.UtcNow },
            ]);

            var newId = new SqlParameter("@NewId", SqlDbType.Int) { Direction = ParameterDirection.Output };
            command.Parameters.Add(newId);
            await command.ExecuteNonQueryAsync(ct);
            return (int)newId.Value!;
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }
    }

    private static async Task SetBlobIdAsync(
        ApplicationDbContext db, string table, string blobColumn, object id, int blobId, bool keyIsText, CancellationToken ct)
    {
        await db.Database.OpenConnectionAsync(ct);
        try
        {
            var connection = (SqlConnection)db.Database.GetDbConnection();
            using var command = connection.CreateCommand();
            command.CommandText = $"UPDATE dbo.{table} SET {blobColumn} = @BlobId WHERE Id = @Id";
            command.Parameters.Add(new SqlParameter("@BlobId", SqlDbType.Int) { Value = blobId });
            command.Parameters.Add(keyIsText
                ? new SqlParameter("@Id", SqlDbType.NVarChar, 450) { Value = id }
                : new SqlParameter("@Id", SqlDbType.Int) { Value = id });
            await command.ExecuteNonQueryAsync(ct);
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }
    }

    /// <summary>The type to serve a backfilled document as. Taken from the extension because that
    /// is all a file on disk tells us -- the content type the uploader sent was never stored, which
    /// is one of the things keeping these as files cost.</summary>
    private static string ContentTypeOf(string file) => Path.GetExtension(file).ToLowerInvariant() switch
    {
        ".pdf" => "application/pdf",
        ".png" => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".gif" => "image/gif",
        ".webp" => "image/webp",
        ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        ".xls" => "application/vnd.ms-excel",
        ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        _ => "application/octet-stream",
    };
}
