using System.Globalization;
using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using CGTOOL.Web.Data;

namespace CGTOOL.Web.Data.Governance;

/// <summary>What happened to one uploaded file.</summary>
public sealed record EmiratesIdAttachOutcome(string FileName, string? Number, bool Attached, string? Problem);

public interface IEmiratesIdBulkAttach
{
    /// <summary>The Emirates ID number a file name carries, or null when it does not carry exactly
    /// one.
    ///
    /// Looks for a fifteen-digit run beginning 784 rather than stripping every digit out of the
    /// name. Stripping looked fine against a clean folder and fails on the names a real one
    /// collects: "784195681635397 (1).pdf" strips to sixteen digits and would be refused, even
    /// though the number in it is unambiguous.
    ///
    /// A name carrying two different such numbers is refused, because there is no way to tell
    /// which person it belongs to and filing it under either is worse than reporting it.</summary>
    static string? NumberFromFileName(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName)) return null;

        var stem = Path.GetFileNameWithoutExtension(fileName);
        var found = new HashSet<string>(StringComparer.Ordinal);

        for (var i = 0; i + 15 <= stem.Length; i++)
        {
            var candidate = stem.AsSpan(i, 15);
            if (!candidate.StartsWith("784", StringComparison.Ordinal)) continue;

            var allDigits = true;
            foreach (var c in candidate)
            {
                if (!char.IsAsciiDigit(c)) { allDigits = false; break; }
            }
            if (!allDigits) continue;

            // A digit immediately either side means this fifteen is part of something longer.
            if (i > 0 && char.IsAsciiDigit(stem[i - 1])) continue;
            if (i + 15 < stem.Length && char.IsAsciiDigit(stem[i + 15])) continue;

            found.Add(candidate.ToString());
        }

        return found.Count == 1 ? found.First() : null;
    }

    /// <param name="actorName">Who is uploading. Attaching somebody's identity document to their
    /// record is a change to that record, so it is recorded against them by name.</param>
    Task<EmiratesIdAttachOutcome> AttachAsync(
        string fileName, byte[] content, string contentType, string? ipAddress, string actorName,
        CancellationToken ct = default);
}

/// <summary>
/// Attaches a bulk-uploaded Emirates ID scan to the person it belongs to.
///
/// The files are named after the Emirates ID number, so the match is on that: the number out of
/// the file name against the number already loaded from the employee spreadsheet. Nothing else
/// about the file is trusted and no reference row is created from one -- a scan for a number
/// nobody is on file under is reported rather than filed somewhere plausible.
/// </summary>
public class EmiratesIdBulkAttach(
    IDbContextFactory<ApplicationDbContext> dbFactory,
    IDocumentStore documents,
    IAuditLogger auditLog,
    ILogger<EmiratesIdBulkAttach> logger) : IEmiratesIdBulkAttach
{
    public async Task<EmiratesIdAttachOutcome> AttachAsync(
        string fileName, byte[] content, string contentType, string? ipAddress, string actorName,
        CancellationToken ct = default)
    {
        var number = IEmiratesIdBulkAttach.NumberFromFileName(fileName);
        if (number is null)
        {
            return new EmiratesIdAttachOutcome(fileName, null, false,
                "The file name is not a 15-digit Emirates ID number beginning 784.");
        }

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var reference = await db.MemberIdentityReferences.AsNoTracking()
            .Where(r => r.EmiratesIdNumber == number)
            .Select(r => new { r.Id, r.Email, r.EmiratesIdBlobId })
            .FirstOrDefaultAsync(ct);

        if (reference is null)
        {
            return new EmiratesIdAttachOutcome(fileName, number, false,
                "No one is on file under that Emirates ID number. Load the employee spreadsheet first.");
        }

        if (reference.EmiratesIdBlobId is not null)
        {
            // Re-running the upload should not store a second copy of every card.
            return new EmiratesIdAttachOutcome(fileName, number, false, "Already attached.");
        }

        var blobId = await documents.SaveAsync(content, fileName, contentType, ipAddress, ct);
        var rows = await SetDocumentAsync(db, number, blobId, fileName, ct);

        if (rows == 0)
        {
            logger.LogWarning("Emirates ID {Number} matched a reference row but the update changed nothing.", number);
            return new EmiratesIdAttachOutcome(fileName, number, false, "The record could not be updated.");
        }

        // In the service rather than the screen, so an attachment cannot be made from anywhere
        // without leaving a record. One entry per card: this is somebody's identity document
        // being filed against their name, and "226 scans attached" does not say whose.
        await auditLog.LogAsync(
            actorName,
            AuditAction.Update,
            nameof(MemberIdentityReference),
            reference.Id.ToString(CultureInfo.InvariantCulture),
            $"Emirates ID scan attached for {reference.Email} from '{fileName}' ({content.Length} bytes).");

        return new EmiratesIdAttachOutcome(fileName, number, true, null);
    }

    private static async Task<int> SetDocumentAsync(
        ApplicationDbContext db, string number, int blobId, string fileName, CancellationToken ct)
    {
        await db.Database.OpenConnectionAsync(ct);
        try
        {
            var connection = (SqlConnection)db.Database.GetDbConnection();
            using var command = connection.CreateCommand();
            command.CommandType = CommandType.StoredProcedure;
            command.CommandText = "dbo.usp_MemberIdentityReference_SetEmiratesIdDocument";
            command.Parameters.AddRange(
            [
                new SqlParameter("@EmiratesIdNumber", SqlDbType.NVarChar, 100) { Value = number },
                new SqlParameter("@BlobId", SqlDbType.Int) { Value = blobId },
                new SqlParameter("@FileName", SqlDbType.NVarChar, 260) { Value = fileName },
            ]);

            var result = await command.ExecuteScalarAsync(ct);
            return result is null or DBNull ? 0 : Convert.ToInt32(result);
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }
    }
}
