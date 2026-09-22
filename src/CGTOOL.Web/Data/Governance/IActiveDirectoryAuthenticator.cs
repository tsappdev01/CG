namespace CGTOOL.Web.Data.Governance;

public record AdAuthResult(bool Succeeded, string? DisplayName, string? Email, string? ErrorMessage);

/// <summary>
/// Validates a username/password against the corporate directory, matching the login mockup's
/// "use your corporate/windows credentials" prompt. Real AD/LDAP is only reachable from the
/// corporate network -- same constraint as UATWEB01 -- so this is pluggable per environment.
/// </summary>
public interface IActiveDirectoryAuthenticator
{
    Task<AdAuthResult> AuthenticateAsync(string username, string password, CancellationToken ct = default);
}
