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
/// Employees are matched to a Company by Azure AD's "companyName" user attribute equalling the
/// Company's Name.
/// </summary>
public class GraphDirectoryEmployeeProvider(IConfiguration configuration, IHttpClientFactory httpClientFactory) : IDirectoryEmployeeProvider
{
    public Task<bool> IsSyncedAsync(Company company, CancellationToken ct = default)
        => Task.FromResult(IsConfigured());

    public async Task<IReadOnlyList<DirectoryEmployeeRecord>> GetEmployeesAsync(Company company, CancellationToken ct = default)
    {
        if (!IsConfigured()) return [];

        var token = await AcquireAppOnlyTokenAsync(ct);
        if (token is null) return [];

        var client = httpClientFactory.CreateClient();
        client.BaseAddress = new Uri("https://graph.microsoft.com/v1.0/");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var filter = Uri.EscapeDataString($"companyName eq '{company.Name}'");
        var response = await client.GetAsync($"users?$filter={filter}&$select=id,displayName,mail,jobTitle,department", ct);
        if (!response.IsSuccessStatusCode) return [];

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

        var results = new List<DirectoryEmployeeRecord>();
        if (doc.RootElement.TryGetProperty("value", out var values))
        {
            foreach (var user in values.EnumerateArray())
            {
                results.Add(new DirectoryEmployeeRecord(
                    FullName: user.GetProperty("displayName").GetString() ?? string.Empty,
                    JobTitle: user.TryGetProperty("jobTitle", out var jt) ? jt.GetString() : null,
                    DepartmentName: user.TryGetProperty("department", out var dept) ? dept.GetString() : null,
                    Email: user.TryGetProperty("mail", out var mail) ? mail.GetString() : null,
                    Signature: null,
                    ReportingManagerName: null,
                    SystemId: user.TryGetProperty("id", out var id) ? id.GetString() : null));
            }
        }

        return results;
    }

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
