using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using CGTOOL.Web.Data;

namespace CGTOOL.Web.Data.Governance;

/// <summary>What a document is, without its bytes. Enough to render a link, show a size, or set
/// the response headers before deciding to read the content at all.</summary>
public sealed record DocumentDescriptor(
    int Id, string FileName, string ContentType, long SizeBytes, string? ContentSha256, DateTime UploadedAtUtc);

/// <summary>An open document: the descriptor, and a stream positioned at the first byte. The
/// caller disposes it.</summary>
public sealed record DocumentContent(DocumentDescriptor Descriptor, Stream Stream) : IAsyncDisposable
{
    public ValueTask DisposeAsync() => Stream.DisposeAsync();
}

public interface IDocumentStore
{
    /// <summary>Writes a document and returns its id, for the owning row to point at.</summary>
    Task<int> SaveAsync(byte[] content, string fileName, string contentType, string? ipAddress, CancellationToken ct = default);

    Task<DocumentDescriptor?> DescribeAsync(int blobId, CancellationToken ct = default);

    /// <summary>Opens a document for delivery. Null when there is no such row.</summary>
    Task<DocumentContent?> OpenAsync(int blobId, CancellationToken ct = default);
}

/// <summary>Reads and writes dbo.DocumentBlobs.
///
/// The read path is the part with a trap in it. A varbinary(max) comes back as one value, so the
/// obvious "SELECT ContentProtected" materialises the whole file in memory before a single byte
/// reaches the browser -- ten megabytes per concurrent download, from the managed heap's large
/// object segment, which is not compacted. With CommandBehavior.SequentialAccess and GetStream the
/// bytes go from the connection to the response as they arrive and nothing is held.
///
/// That only works for a document stored as uploaded. An encrypted one has to be whole before it
/// can be decrypted, so those are read into memory -- bounded by the upload limits, and the cost
/// of choosing encryption. Which path a row takes is its ProtectionScheme, not a setting, so the
/// two kinds coexist while a backfill runs.</summary>
public class DocumentStore(ApplicationDbContext db, IDocumentProtector protector) : IDocumentStore
{
    public async Task<int> SaveAsync(byte[] content, string fileName, string contentType, string? ipAddress, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(content);

        // The hash is of the file as uploaded, not of what gets stored, so it still identifies the
        // document if the protection scheme changes under it.
        var sha256 = IDocumentProtector.Sha256Hex(content);
        var stored = protector.Protect(content);

        await db.Database.OpenConnectionAsync(ct);
        try
        {
            var connection = (SqlConnection)db.Database.GetDbConnection();
            using var command = connection.CreateCommand();
            command.CommandType = CommandType.StoredProcedure;
            command.CommandText = "dbo.usp_DocumentBlob_Insert";
            if (db.Database.CurrentTransaction is { } tx)
            {
                command.Transaction = (SqlTransaction)tx.GetDbTransaction();
            }

            command.Parameters.AddRange(
            [
                new SqlParameter("@FileName", SqlDbType.NVarChar, 255) { Value = Truncate(fileName, 255) },
                new SqlParameter("@ContentType", SqlDbType.NVarChar, 100) { Value = Truncate(contentType, 100) },
                new SqlParameter("@SizeBytes", SqlDbType.BigInt) { Value = (long)content.Length },
                new SqlParameter("@ContentProtected", SqlDbType.VarBinary, -1) { Value = stored },
                new SqlParameter("@ProtectionScheme", SqlDbType.NVarChar, 20) { Value = protector.Scheme },
                new SqlParameter("@ContentSha256", SqlDbType.NVarChar, 64) { Value = sha256 },
                new SqlParameter("@IpAddress", SqlDbType.NVarChar, 64) { Value = (object?)ipAddress ?? DBNull.Value },
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

    public async Task<DocumentDescriptor?> DescribeAsync(int blobId, CancellationToken ct = default) =>
        await db.DocumentBlobs
            .AsNoTracking()
            .Where(b => b.Id == blobId)
            // Projected, never the entity: selecting the entity would bring ContentProtected with it.
            .Select(b => new DocumentDescriptor(b.Id, b.FileName, b.ContentType, b.SizeBytes, b.ContentSha256, b.UploadedAtUtc))
            .FirstOrDefaultAsync(ct);

    public async Task<DocumentContent?> OpenAsync(int blobId, CancellationToken ct = default)
    {
        await db.Database.OpenConnectionAsync(ct);
        var connection = (SqlConnection)db.Database.GetDbConnection();

        SqlDataReader? reader = null;
        try
        {
            using var command = connection.CreateCommand();
            command.CommandText =
                "SELECT Id, FileName, ContentType, SizeBytes, ContentSha256, UploadedAtUtc, ProtectionScheme, ContentProtected " +
                "FROM dbo.DocumentBlobs WHERE Id = @Id";
            command.Parameters.Add(new SqlParameter("@Id", SqlDbType.Int) { Value = blobId });
            if (db.Database.CurrentTransaction is { } tx)
            {
                command.Transaction = (SqlTransaction)tx.GetDbTransaction();
            }

            // ContentProtected is read last in the column list on purpose: SequentialAccess allows
            // columns to be read once, in order, so every cheap column has to come before the blob.
            reader = await command.ExecuteReaderAsync(CommandBehavior.SequentialAccess, ct);
            if (!await reader.ReadAsync(ct))
            {
                await reader.DisposeAsync();
                await db.Database.CloseConnectionAsync();
                return null;
            }

            var descriptor = new DocumentDescriptor(
                reader.GetInt32(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetInt64(3),
                await reader.IsDBNullAsync(4, ct) ? null : reader.GetString(4),
                reader.GetDateTime(5));
            var scheme = reader.GetString(6);

            if (scheme == DocumentProtection.None)
            {
                // Straight through: the stream owns the reader and the connection, and releasing it
                // is what closes them, so the caller disposing the DocumentContent is the whole
                // lifetime. Nothing is buffered.
                var sqlStream = reader.GetStream(7);
                return new DocumentContent(descriptor, new ReaderOwningStream(sqlStream, reader, db));
            }

            // Encrypted: it has to be whole to be decrypted, so read it, then let the reader go.
            using var buffer = new MemoryStream();
            await using (var raw = reader.GetStream(7))
            {
                await raw.CopyToAsync(buffer, ct);
            }

            await reader.DisposeAsync();
            reader = null;
            await db.Database.CloseConnectionAsync();

            var plaintext = protector.Unprotect(buffer.ToArray(), scheme);
            return new DocumentContent(descriptor, new MemoryStream(plaintext, writable: false));
        }
        catch
        {
            if (reader is not null) await reader.DisposeAsync();
            await db.Database.CloseConnectionAsync();
            throw;
        }
    }

    private static string Truncate(string? value, int max) =>
        string.IsNullOrEmpty(value) ? string.Empty : value.Length <= max ? value : value[..max];

    /// <summary>Keeps the reader and the connection alive for exactly as long as the stream is, so
    /// a streamed document does not need the caller to know there is a reader behind it.</summary>
    private sealed class ReaderOwningStream(Stream inner, SqlDataReader reader, ApplicationDbContext db) : Stream
    {
        public override bool CanRead => inner.CanRead;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default) => inner.ReadAsync(buffer, ct);
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct) => inner.ReadAsync(buffer, offset, count, ct);
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override async ValueTask DisposeAsync()
        {
            await inner.DisposeAsync();
            await reader.DisposeAsync();
            await db.Database.CloseConnectionAsync();
            await base.DisposeAsync();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                inner.Dispose();
                reader.Dispose();
                db.Database.CloseConnectionAsync().GetAwaiter().GetResult();
            }

            base.Dispose(disposing);
        }
    }
}
