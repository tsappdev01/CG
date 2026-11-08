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
                $"(sAMAccountName={EscapeFilterValue(username)})",
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

    /// <summary>Escapes the characters that would otherwise end the assertion and start another one
    /// (RFC 4515). The bind above has already succeeded by the time this filter runs, so a crafted
    /// username has to be one the directory accepted -- but a filter built by string concatenation
    /// is a defect whether or not today's call path can reach it, and "(sAMAccountName=*)" matching
    /// every account is not a search this code should be able to make.</summary>
    private static string EscapeFilterValue(string value)
    {
        var escaped = new System.Text.StringBuilder(value.Length);
        foreach (var c in value)
        {
            switch (c)
            {
                case '\\': escaped.Append("\\5c"); break;
                case '*': escaped.Append("\\2a"); break;
                case '(': escaped.Append("\\28"); break;
                case ')': escaped.Append("\\29"); break;
                case '\0': escaped.Append("\\00"); break;
                case '/': escaped.Append("\\2f"); break;
                default: escaped.Append(c); break;
            }
        }

        return escaped.ToString();
    }
}
