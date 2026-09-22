using System.DirectoryServices.Protocols;
using System.Net;

namespace CGTOOL.Web.Data.Governance;

/// <summary>
/// Production implementation: performs an LDAP simple bind against the configured domain
/// controller. Only reachable when this app is deployed on the corporate network/domain --
/// cannot be exercised from an isolated build/sandbox environment.
/// </summary>
public class LdapActiveDirectoryAuthenticator(IConfiguration configuration) : IActiveDirectoryAuthenticator
{
    public Task<AdAuthResult> AuthenticateAsync(string username, string password, CancellationToken ct = default)
    {
        var server = configuration["ActiveDirectory:Server"];
        var domain = configuration["ActiveDirectory:Domain"];

        if (string.IsNullOrWhiteSpace(server))
        {
            return Task.FromResult(new AdAuthResult(false, null, null, "Active Directory server is not configured (ActiveDirectory:Server)."));
        }

        try
        {
            using var connection = new LdapConnection(new LdapDirectoryIdentifier(server))
            {
                AuthType = AuthType.Negotiate,
                Credential = new NetworkCredential(username, password, domain),
            };
            connection.Bind();

            var searchRequest = new SearchRequest(
                null,
                $"(sAMAccountName={username})",
                SearchScope.Subtree,
                "displayName", "mail");

            var response = (SearchResponse)connection.SendRequest(searchRequest);
            var entry = response.Entries.Count > 0 ? response.Entries[0] : null;
            var displayName = entry?.Attributes["displayName"]?[0]?.ToString();
            var email = entry?.Attributes["mail"]?[0]?.ToString();

            return Task.FromResult(new AdAuthResult(true, displayName, email, null));
        }
        catch (LdapException ex)
        {
            return Task.FromResult(new AdAuthResult(false, null, null, $"Invalid corporate credentials ({ex.Message})."));
        }
    }
}
