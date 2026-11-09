using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Identity.Client;

namespace CGTOOL.Web.Data.Governance;

/// <summary>
/// Production implementation of UM-01: looks up an entity's employees from Azure AD via
/// Microsoft Graph, using an app-only (client credentials) token. Requires a real Azure AD app
/// registration with the Graph "User.Read.All" application permission granted -- only reachable
/// once configured against the organization's real tenant, same constraint as UATWEB01/LDAP.
///
/// People are matched to a Company by Azure AD's "companyName" user attribute equalling the
/// Company's Name -- compared here rather than by Graph, because companyName is not an ordinary
/// filterable property on /users: a `$filter=companyName eq '...'` is answered 400 unless the
/// request is an advanced query, and is case-sensitive even then. That filter is why every
/// lookup came back empty and the screen reported no records for the entity.
/// </summary>
public class GraphDirectoryEmployeeProvider(IConfiguration configuration, IHttpClientFactory httpClientFactory) : IDirectoryEmployeeProvider
{
    public Task<bool> IsSyncedAsync(Company company, CancellationToken ct = default)
        => Task.FromResult(IsConfigured());

    private static bool Matches(string? left, string? right) =>
        !string.IsNullOrWhiteSpace(left)
        && !string.IsNullOrWhiteSpace(right)
        && left.Trim().Equals(right.Trim(), StringComparison.OrdinalIgnoreCase);

    public async Task<DirectoryEmployeeRecord?> FindEmployeeAsync(
        string? companyName, string? objectId, string? email, string? fullName, CancellationToken ct = default)
    {
        if (!IsConfigured()) return null;
        if (string.IsNullOrWhiteSpace(objectId) && string.IsNullOrWhiteSpace(email) && string.IsNullOrWhiteSpace(fullName))
        {
            return null;
        }

        var client = await CreateClientAsync(ct);
        if (client is null) return null;

        // Exact, and one request: the object id is the directory's own key for this person.
        if (!string.IsNullOrWhiteSpace(objectId)
            && await ReadOneAsync(client, $"users/{Uri.EscapeDataString(objectId)}?$select={UserFields}", ct) is { } byId)
        {
            return byId.Record;
        }

        // Both mail and the sign-in name, because a tenant may populate only one of them.
        if (!string.IsNullOrWhiteSpace(email))
        {
            var escaped = email.Trim().Replace("'", "''", StringComparison.Ordinal);
            var filter = Uri.EscapeDataString($"mail eq '{escaped}' or userPrincipalName eq '{escaped}'");
            var matches = await ReadAllAsync(client, $"users?$filter={filter}&$select={UserFields}", ct);
            if (matches.Count == 1) return matches[0].Record;
        }

        // Last, and only when it is unambiguous. Two people in the entity with the same display
        // name is exactly the case where guessing writes one person's title onto the other, so a
        // tie fills nothing rather than filling the wrong person.
        if (!string.IsNullOrWhiteSpace(fullName))
        {
            var everyone = await ReadAllAsync(client, $"users?$select={UserFields}&$top=999", ct);
            var named = everyone
                .Where(p => Matches(p.Record.FullName, fullName))
                .Where(p => string.IsNullOrWhiteSpace(companyName) || Matches(p.CompanyName, companyName))
                .ToList();
            if (named.Count == 1) return named[0].Record;
        }

        return null;
    }

    public async Task<(byte[] Content, string ContentType)?> GetPhotoAsync(string objectId, CancellationToken ct = default)
    {
        if (!IsConfigured() || string.IsNullOrWhiteSpace(objectId)) return null;

        var client = await CreateClientAsync(ct);
        if (client is null) return null;

        // A user with no photograph answers 404, which is an answer and not a failure.
        using var response = await client.GetAsync($"users/{Uri.EscapeDataString(objectId)}/photo/$value", ct);
        if (!response.IsSuccessStatusCode) return null;

        var bytes = await response.Content.ReadAsByteArrayAsync(ct);
        if (bytes.Length == 0) return null;

        return (bytes, response.Content.Headers.ContentType?.MediaType ?? "image/jpeg");
    }

    // companyName is selected as well as read: the entity a person belongs to is matched here
    // rather than by Graph (see GetEmployeesAsync), so it has to come back with every row.
    private const string UserFields = "id,displayName,mail,userPrincipalName,jobTitle,department,companyName";

    /// <summary>A directory row: the record we expose, plus the entity it was filed under.</summary>
    private readonly record struct DirectoryRow(DirectoryEmployeeRecord Record, string? CompanyName);

    private async Task<HttpClient?> CreateClientAsync(CancellationToken ct)
    {
        var token = await AcquireAppOnlyTokenAsync(ct);
        if (token is null) return null;

        var client = httpClientFactory.CreateClient();
        client.BaseAddress = new Uri("https://graph.microsoft.com/v1.0/");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Required by Graph for the advanced queries some of these reads use, and harmless on the
        // rest. Without it a $filter on a property like companyName is rejected outright.
        client.DefaultRequestHeaders.Add("ConsistencyLevel", "eventual");

        return client;
    }

    private static async Task<DirectoryRow?> ReadOneAsync(HttpClient client, string url, CancellationToken ct)
    {
        using var response = await client.GetAsync(url, ct);
        if (!response.IsSuccessStatusCode) return null;

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        return Read(doc.RootElement);
    }

    /// <summary>Every page, not just the first. /users answers 100 rows by default and hands back
    /// an @odata.nextLink for the rest; a reader that ignores it reports a directory that stops
    /// partway through, which looks exactly like a person who is not in it.</summary>
    private static async Task<List<DirectoryRow>> ReadAllAsync(HttpClient client, string url, CancellationToken ct)
    {
        var results = new List<DirectoryRow>();
        var next = url;

        while (!string.IsNullOrEmpty(next))
        {
            using var response = await client.GetAsync(next, ct);
            if (!response.IsSuccessStatusCode) return results;

            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

            if (doc.RootElement.TryGetProperty("value", out var values))
            {
                foreach (var user in values.EnumerateArray())
                {
                    if (Read(user) is { } row) results.Add(row);
                }
            }

            next = doc.RootElement.TryGetProperty("@odata.nextLink", out var link) ? link.GetString() : null;
        }

        return results;
    }

    private static DirectoryRow? Read(JsonElement user)
    {
        if (Str(user, "displayName") is not { Length: > 0 } name) return null;

        return new DirectoryRow(
            new DirectoryEmployeeRecord(
                FullName: name,
                JobTitle: Str(user, "jobTitle"),
                DepartmentName: Str(user, "department"),
                Email: Str(user, "mail") ?? Str(user, "userPrincipalName"),
                Signature: null,
                ReportingManagerName: null,
                SystemId: Str(user, "id")),
            Str(user, "companyName"));
    }

    private static string? Str(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private bool IsConfigured()
        => !string.IsNullOrWhiteSpace(configuration["AzureAd:TenantId"])
        && !string.IsNullOrWhiteSpace(configuration["AzureAd:ClientId"])
        && !string.IsNullOrWhiteSpace(configuration["AzureAd:ClientSecret"]);

    private async Task<string?> AcquireAppOnlyTokenAsync(CancellationToken ct)
    {
        var app = ConfidentialClientApplicationBuilder.Create(configuration["AzureAd:ClientId"])
            .WithClientSecret(configuration["AzureAd:ClientSecret"])
            .WithAuthority($"https://login.microsoftonline.com/{configuration["AzureAd:TenantId"]}/v2.0")
            .Build();

        try
        {
            var result = await app.AcquireTokenForClient(["https://graph.microsoft.com/.default"]).ExecuteAsync(ct);
            return result.AccessToken;
        }
        catch (MsalException)
        {
            return null;
        }
    }
}
