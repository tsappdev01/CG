using Microsoft.AspNetCore.Identity;

namespace CGTOOL.Web.Data.Governance;

public enum DirectoryPhotoResult
{
    /// <summary>Nothing was written: there was no photo, or the write failed.</summary>
    None,

    /// <summary>The stored picture already is this photo, byte for byte.</summary>
    Unchanged,

    /// <summary>A new blob was written and the account now points at it.</summary>
    Stored,
}

/// <summary>Storing a directory profile photo against a sign-in account.
///
/// Shared so the scheduled Entra sync, the user-list upload and the "Fill from directory" button
/// all behave the same way. In particular they all compare before writing: the sync runs on a
/// schedule and most runs fetch a photo that has not changed, and a blob written every time would
/// add a row per user per run and grow without bound.</summary>
public static class DirectoryPhotos
{
    public static async Task<DirectoryPhotoResult> StoreAsync(
        byte[] bytes,
        string contentType,
        ApplicationUser appUser,
        IDocumentStore documents,
        UserManager<ApplicationUser> userManager,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(appUser);
        ArgumentNullException.ThrowIfNull(documents);
        ArgumentNullException.ThrowIfNull(userManager);

        if (bytes is null || bytes.Length == 0) return DirectoryPhotoResult.None;

        var hash = IDocumentProtector.Sha256Hex(bytes);
        if (appUser.ProfilePictureBlobId is int existingId)
        {
            var existing = await documents.DescribeAsync(existingId, ct);
            if (existing?.ContentSha256 == hash) return DirectoryPhotoResult.Unchanged;
        }

        var extension = contentType switch
        {
            "image/png" => ".png",
            "image/webp" => ".webp",
            _ => ".jpg",
        };

        appUser.ProfilePictureBlobId = await documents.SaveAsync(
            bytes, $"{appUser.Id}{extension}", contentType, ipAddress: null, ct);

        // The old file-path column is cleared, not left beside the blob id: a reader that still
        // prefers the path would otherwise go on showing the picture this one replaced.
        appUser.ProfilePicturePath = null;

        var result = await userManager.UpdateAsync(appUser);
        return result.Succeeded ? DirectoryPhotoResult.Stored : DirectoryPhotoResult.None;
    }
}
