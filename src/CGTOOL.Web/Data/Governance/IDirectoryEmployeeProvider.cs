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

    /// <summary>Their photograph, as the bytes and the type it came back as.</summary>
    Task<(byte[] Content, string ContentType)?> GetPhotoAsync(string objectId, CancellationToken ct = default);
}

public class NotConfiguredDirectoryEmployeeProvider : IDirectoryEmployeeProvider
{
    public Task<bool> IsSyncedAsync(Company company, CancellationToken ct = default) => Task.FromResult(false);

    public Task<DirectoryEmployeeRecord?> FindEmployeeAsync(
        string? companyName, string? objectId, string? email, string? fullName, CancellationToken ct = default)
        => Task.FromResult<DirectoryEmployeeRecord?>(null);

    public Task<(byte[] Content, string ContentType)?> GetPhotoAsync(string objectId, CancellationToken ct = default)
        => Task.FromResult<(byte[], string)?>(null);
}
