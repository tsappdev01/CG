namespace CGTOOL.Web.Data.Governance;

public record DirectoryEmployeeRecord(
    string FullName,
    string? JobTitle,
    string? DepartmentName,
    string? Email,
    string? Signature,
    string? ReportingManagerName,
    string? SystemId = null);

/// <summary>
/// Looks up an entity's employees from the corporate directory (Azure AD in Production, via
/// Microsoft Graph). Real implementations require a real Azure AD app registration and network
/// access, same constraint as UATWEB01. This no-op implementation always reports "not synced" so
/// the admin UI falls back to manual entry, matching the design doc's "if the entity AD is not
/// sync with DI Data center" case.
/// </summary>
public interface IDirectoryEmployeeProvider
{
    Task<bool> IsSyncedAsync(Company company, CancellationToken ct = default);

    /// <summary>The directory's record for one person, or null when it has none for them.
    ///
    /// Asked for by identity, never by position in a list. "Fill from directory" used to read the
    /// entity's whole employee list and take the first row in it, so it filled in whoever the
    /// directory happened to return first for that company -- a different person's name, title and
    /// address written over this one's, with nothing on screen to say so. There is deliberately no
    /// "give me the entity's people" method any more: nothing needed one, and its only use was the
    /// call that produced that bug.</summary>
    Task<DirectoryEmployeeRecord?> FindEmployeeAsync(
        string? companyName, string? objectId, string? email, string? fullName, CancellationToken ct = default);

    /// <summary>Their photograph, and when there is none, why.
    ///
    /// The reason is returned rather than swallowed because every way this can come back empty
    /// looks identical on screen -- no photo in the directory, a tenant that will not hand photos
    /// to this app registration, a person with no mailbox -- and "nothing happened" is not
    /// something an administrator can act on.</summary>
    Task<DirectoryPhoto> GetPhotoAsync(string objectId, CancellationToken ct = default);
}

public class NotConfiguredDirectoryEmployeeProvider : IDirectoryEmployeeProvider
{
    public Task<bool> IsSyncedAsync(Company company, CancellationToken ct = default) => Task.FromResult(false);

    public Task<DirectoryEmployeeRecord?> FindEmployeeAsync(
        string? companyName, string? objectId, string? email, string? fullName, CancellationToken ct = default)
        => Task.FromResult<DirectoryEmployeeRecord?>(null);

    public Task<DirectoryPhoto> GetPhotoAsync(string objectId, CancellationToken ct = default)
        => Task.FromResult(DirectoryPhoto.Unavailable(DirectoryPhotoOutcome.NotConfigured));
}

public enum DirectoryPhotoOutcome
{
    /// <summary>Bytes came back.</summary>
    Fetched,

    /// <summary>No tenant is configured, so nothing was asked.</summary>
    NotConfigured,

    /// <summary>We hold no directory id for this person, so there was nobody to ask about.</summary>
    NoDirectoryAccount,

    /// <summary>The directory answered, and this person has no photograph in it. In a tenant
    /// whose photos live in Exchange, a person with no mailbox answers this way too.</summary>
    NoPhoto,

    /// <summary>The directory refused. The app registration is missing User.Read.All (or
    /// ProfilePhoto.Read.All) as an APPLICATION permission with admin consent granted -- the
    /// delegated grant of the same name is not enough for an app-only token.</summary>
    Refused,

    /// <summary>The request failed for some other reason; the status code is in the log.</summary>
    Failed,
}

/// <summary>A photograph, or the reason there is not one.</summary>
public readonly record struct DirectoryPhoto(DirectoryPhotoOutcome Outcome, byte[]? Content, string? ContentType)
{
    public static DirectoryPhoto Unavailable(DirectoryPhotoOutcome outcome) => new(outcome, null, null);

    public bool HasContent => Outcome == DirectoryPhotoOutcome.Fetched && Content is { Length: > 0 };

    /// <summary>What to tell an administrator who pressed the button and saw no picture.</summary>
    public string? Explain(string who) => Outcome switch
    {
        DirectoryPhotoOutcome.Fetched => null,
        DirectoryPhotoOutcome.NotConfigured => "No company directory is configured, so no picture could be fetched.",
        DirectoryPhotoOutcome.NoDirectoryAccount =>
            $"{who} is not linked to a directory account yet, so there is no picture to fetch.",
        DirectoryPhotoOutcome.NoPhoto => $"The directory holds no photograph for {who}.",
        DirectoryPhotoOutcome.Refused =>
            "The directory refused the photo request. The app registration needs User.Read.All as an "
            + "application permission, with admin consent granted.",
        _ => "The directory could not be asked for the photograph just now.",
    };
}
