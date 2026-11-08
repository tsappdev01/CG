using CGTOOL.Web.Data.Governance;

namespace CGTOOL.Web.Components.Account;

/// <summary>Delivers a stored document.
///
/// One endpoint for all of them, which is the point: while documents were files under wwwroot they
/// were fetched by path, and a path cannot be asked who the document belongs to. Everything now
/// goes through here, so the rule about who may read what is written once (IDocumentAccess) and
/// applies to every document the system holds.
///
/// An endpoint rather than a Blazor component because the response is a file, not a page: headers
/// and a body, written before any circuit exists.</summary>
internal static class DocumentEndpoints
{
    public static IEndpointConventionBuilder MapDocumentEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var group = endpoints.MapGroup("/documents").RequireAuthorization();

        group.MapGet("/{blobId:int}", async (
            int blobId,
            HttpContext context,
            IDocumentAccess access,
            IDocumentStore store,
            CancellationToken ct) =>
        {
            // Asked before the document is opened, so a refusal costs a lookup rather than a read
            // of the file.
            if (!await access.MayReadAsync(context.User, blobId, ct))
            {
                // 404, not 403: "you may not read document 4312" confirms document 4312 exists and
                // belongs to someone. Refusing and not-found look the same from outside, so ids
                // cannot be probed for which ones are real.
                return Results.NotFound();
            }

            var document = await store.OpenAsync(blobId, ct);
            if (document is null) return Results.NotFound();

            var descriptor = document.Descriptor;

            // The content hash doubles as the ETag: the bytes of a stored document never change --
            // a replacement is a new row with a new id -- so this is exact rather than a heuristic,
            // and it gives back the browser caching that serving files from disk used to provide
            // for free. Private, because these are one person's documents and a shared cache must
            // not keep them.
            if (descriptor.ContentSha256 is { Length: > 0 } hash)
            {
                var etag = $"\"{hash}\"";
                if (context.Request.Headers.IfNoneMatch.Contains(etag))
                {
                    await document.DisposeAsync();
                    return Results.StatusCode(StatusCodes.Status304NotModified);
                }

                context.Response.Headers.ETag = etag;
            }

            context.Response.Headers.CacheControl = "private, max-age=0, must-revalidate";

            // Inline so the existing previews (an <img> for a scan, an <iframe> for a PDF) keep
            // working; the file name is still carried for a download. Content-Type is the one
            // recorded at upload, which came from an allow-list, so this cannot be talked into
            // serving something as text/html.
            return Results.File(
                document.Stream,
                descriptor.ContentType,
                descriptor.FileName,
                enableRangeProcessing: true);
        });

        return group;
    }
}
