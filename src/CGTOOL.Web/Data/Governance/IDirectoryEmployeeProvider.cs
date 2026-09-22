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

    Task<IReadOnlyList<DirectoryEmployeeRecord>> GetEmployeesAsync(Company company, CancellationToken ct = default);
}

public class NotConfiguredDirectoryEmployeeProvider : IDirectoryEmployeeProvider
{
    public Task<bool> IsSyncedAsync(Company company, CancellationToken ct = default) => Task.FromResult(false);

    public Task<IReadOnlyList<DirectoryEmployeeRecord>> GetEmployeesAsync(Company company, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<DirectoryEmployeeRecord>>([]);
}
