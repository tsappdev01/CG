using System.Data;
using System.Security.Claims;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using CGTOOL.Web.Data;

namespace CGTOOL.Web.Data.Governance;

/// <summary>What kind of document a blob is, which is what decides who may read it.</summary>
public enum DocumentAudience
{
    /// <summary>No row points at this blob. Nothing to serve.</summary>
    Unknown,

    /// <summary>Belongs to one member: their identity documents, their declarations, their family
    /// and company records. Readable by that member, an Administrator, and anyone they have
    /// approved to act on their behalf.</summary>
    Member,

    /// <summary>Published to everyone who works here -- policy documents, company logos, profile
    /// pictures. Readable by any signed-in user.</summary>
    Organisation,

    /// <summary>Administrative material that is not any one member's: the share register and
    /// trading workbooks. Administrators only.</summary>
    Administrative,
}

public sealed record DocumentOwner(DocumentAudience Audience, int? MemberId);

public interface IDocumentAccess
{
    Task<DocumentOwner> OwnerOfAsync(int blobId, CancellationToken ct = default);

    /// <summary>Whether this caller may read this document.</summary>
    Task<bool> MayReadAsync(ClaimsPrincipal user, int blobId, CancellationToken ct = default);
}

/// <summary>Decides who may read a stored document.
///
/// This is the rule that did not exist while documents were files under wwwroot: a static file is
/// served by path, and a path knows nothing about who the document belongs to. Now that every
/// document is delivered through one endpoint, the question is asked once, here, for all 21 places
/// a document can be attached.
///
/// The lookup is a union over those 21 columns rather than a column on DocumentBlobs saying who
/// owns it. A column would have to be written correctly by 22 upload paths and would be wrong the
/// first time one of them forgot; the union is derived from the references that actually exist, so
/// it cannot disagree with them. It is one indexed read, on the path that is about to stream a
/// file, so the cost is not worth trading for that risk.</summary>
public class DocumentAccess(ApplicationDbContext db) : IDocumentAccess
{
    // Every blob reference in the model, resolved back to the member it belongs to. Two hops at
    // most. Kept as one statement so a new attachment point is one line here and is then covered
    // by the endpoint without anything else changing.
    private const string OwningMemberSql = """
        SELECT TOP 1 MemberId FROM (
            SELECT MemberId FROM dbo.MemberDocuments WHERE BlobId = @BlobId
            UNION ALL
            SELECT MemberId FROM dbo.InsiderDeclarations
                WHERE EmiratesIdBlobId = @BlobId OR PassportBlobId = @BlobId
                   OR TradeLicenceBlobId = @BlobId OR OtherDocumentBlobId = @BlobId
            UNION ALL
            SELECT d.MemberId FROM dbo.InsiderDeclarationDocuments doc
                JOIN dbo.InsiderDeclarations d ON d.Id = doc.InsiderDeclarationId
                WHERE doc.BlobId = @BlobId
            UNION ALL
            SELECT MemberId FROM dbo.RelatedPartyCoiDeclarations WHERE SignatureBlobId = @BlobId
            UNION ALL
            SELECT c.MemberId FROM dbo.RelatedPartyCoiDeclarationDocuments doc
                JOIN dbo.RelatedPartyCoiDeclarations c ON c.Id = doc.RelatedPartyCoiDeclarationId
                WHERE doc.BlobId = @BlobId
            UNION ALL
            SELECT c.MemberId FROM dbo.CoiTradeLicenseDocuments t
                JOIN dbo.CoiCompanyEntries e ON e.Id = t.CoiCompanyEntryId
                JOIN dbo.RelatedPartyCoiDeclarations c ON c.Id = e.RelatedPartyCoiDeclarationId
                WHERE t.BlobId = @BlobId
            UNION ALL
            SELECT MemberId FROM dbo.FamilyMembers
                WHERE EmiratesIdBlobId = @BlobId OR PassportBlobId = @BlobId OR TradeLicenceBlobId = @BlobId
            UNION ALL
            SELECT f.MemberId FROM dbo.FamilyMemberHoldings h
                JOIN dbo.FamilyMembers f ON f.Id = h.FamilyMemberId
                WHERE h.TradeLicenceBlobId = @BlobId
            UNION ALL
            SELECT MemberId FROM dbo.OwnedCompanies
                WHERE TradeLicenseBlobId = @BlobId OR MoaBlobId = @BlobId OR PoaBlobId = @BlobId
            UNION ALL
            SELECT t.MemberId FROM dbo.RelatedPartyTransactionDocuments doc
                JOIN dbo.RelatedPartyTransactions t ON t.Id = doc.RelatedPartyTransactionId
                WHERE doc.BlobId = @BlobId
        ) owners
        """;

    private const string AudienceSql = """
        SELECT TOP 1 Audience FROM (
            SELECT 'Organisation' AS Audience FROM dbo.PolicyDocumentVersions WHERE BlobId = @BlobId
            UNION ALL
            SELECT 'Organisation' FROM dbo.Companies WHERE LogoBlobId = @BlobId
            UNION ALL
            SELECT 'Organisation' FROM dbo.AspNetUsers WHERE ProfilePictureBlobId = @BlobId
            UNION ALL
            SELECT 'Administrative' FROM dbo.ShareholderRegisterUploads WHERE BlobId = @BlobId
        ) audiences
        """;

    public async Task<DocumentOwner> OwnerOfAsync(int blobId, CancellationToken ct = default)
    {
        var memberId = await ScalarAsync(OwningMemberSql, blobId, ct);
        if (memberId is int id) return new DocumentOwner(DocumentAudience.Member, id);

        var audience = await ScalarAsync(AudienceSql, blobId, ct);
        return audience is string name && Enum.TryParse<DocumentAudience>(name, out var parsed)
            ? new DocumentOwner(parsed, null)
            // Nothing references it. Either the blob was orphaned by a failed write, or the id was
            // guessed. Both answer the same way: there is no such document.
            : new DocumentOwner(DocumentAudience.Unknown, null);
    }

    public async Task<bool> MayReadAsync(ClaimsPrincipal user, int blobId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(user);
        if (user.Identity?.IsAuthenticated != true) return false;

        var owner = await OwnerOfAsync(blobId, ct);
        var isAdministrator = user.IsInRole(GovernanceRoles.Administrator);

        return owner.Audience switch
        {
            DocumentAudience.Unknown => false,
            DocumentAudience.Organisation => true,
            DocumentAudience.Administrative => isAdministrator,
            DocumentAudience.Member => isAdministrator || await MayActForAsync(user, owner.MemberId!.Value, ct),
            _ => false,
        };
    }

    /// <summary>The caller's own documents, plus those of anyone who has approved them to act on
    /// their behalf. The grant is read here, on every request, rather than taken from the
    /// acting-as cookie: a document request is not a page load, and an approval withdrawn an hour
    /// ago should stop working now.</summary>
    private async Task<bool> MayActForAsync(ClaimsPrincipal user, int memberId, CancellationToken ct)
    {
        var userId = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (userId is null) return false;

        var me = await db.Members.AsNoTracking()
            .Where(m => m.ApplicationUserId == userId)
            .Select(m => (int?)m.Id)
            .FirstOrDefaultAsync(ct);
        if (me is null) return false;
        if (me == memberId) return true;

        return await db.MemberImpersonationApprovals.AsNoTracking()
            .AnyAsync(a => a.ImpersonatorId == me.Value && a.MemberId == memberId, ct);
    }

    private async Task<object?> ScalarAsync(string sql, int blobId, CancellationToken ct)
    {
        await db.Database.OpenConnectionAsync(ct);
        try
        {
            var connection = (SqlConnection)db.Database.GetDbConnection();
            using var command = connection.CreateCommand();
            command.CommandText = sql;
            command.Parameters.Add(new SqlParameter("@BlobId", SqlDbType.Int) { Value = blobId });
            var value = await command.ExecuteScalarAsync(ct);
            return value is null or DBNull ? null : value;
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }
    }
}
