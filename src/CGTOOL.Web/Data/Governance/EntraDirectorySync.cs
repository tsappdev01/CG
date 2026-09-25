using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Identity.Client;
using CGTOOL.Web.Data;

namespace CGTOOL.Web.Data.Governance;

/// <summary>
/// One person as a directory holds them -- Entra ID, or a spreadsheet standing in for it. The
/// fields up to AccountEnabled are exactly what the Graph query selects; the three after it only
/// ever come from a spreadsheet, because Entra has no notion of this application's roles and the
/// reporting line is governance data rather than directory data.
/// </summary>
public record DirectoryPerson(
    string? ObjectId,
    string DisplayName,
    string? Mail,
    string? UserPrincipalName,
    string? JobTitle,
    string? Department,
    string? CompanyName,
    bool AccountEnabled,
    string? RoleName = null,
    string? ReportingManagerEmail = null,
    string? WindowsUserId = null)
{
    /// <summary>Entra allows a user with no mailbox, in which case the UPN is the address we have.</summary>
    public string? Address => string.IsNullOrWhiteSpace(Mail) ? UserPrincipalName : Mail;
}

public record DirectorySyncResult(int Created, int Updated, int Skipped, int PhotosStored, IReadOnlyList<string> Problems)
{
    public static DirectorySyncResult Empty(string problem) => new(0, 0, 0, 0, [problem]);
    public int Total => Created + Updated;
}

public interface IEntraDirectorySync
{
    bool IsConfigured { get; }

    /// <summary>Reads every user from Entra ID and writes them into the member directory.</summary>
    Task<DirectorySyncResult> SyncAsync(string actorName, CancellationToken ct = default);
}

/// <summary>
/// Reads the member directory out of Entra ID: every user in the tenant, with the profile fields
/// the governance screens display -- full name, email, company, department, designation and photo.
///
/// Everything after the reading is DirectoryImporter, shared with the spreadsheet upload that
/// stands in for this where a deployment has no tenant connection. What stays here is what only
/// Entra can do: acquiring an application token, paging /users, and fetching each profile photo.
/// </summary>
public class EntraDirectorySync(
    IConfiguration configuration,
    IHttpClientFactory httpClientFactory,
    IDirectoryImporter importer,
    UserManager<ApplicationUser> userManager,
    IWebHostEnvironment environment,
    ILogger<EntraDirectorySync> logger) : IEntraDirectorySync
{
    private const string GraphBase = "https://graph.microsoft.com/v1.0/";

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(configuration["AzureAd:TenantId"])
        && !string.IsNullOrWhiteSpace(configuration["AzureAd:ClientId"])
        && !string.IsNullOrWhiteSpace(configuration["AzureAd:ClientSecret"]);

    public async Task<DirectorySyncResult> SyncAsync(string actorName, CancellationToken ct = default)
    {
        if (!IsConfigured)
        {
            return DirectorySyncResult.Empty(
                "Entra ID is not configured. Set AzureAd:TenantId, AzureAd:ClientId and AzureAd:ClientSecret, " +
                "and grant the application the User.Read.All Graph permission.");
        }

        var token = await AcquireAppOnlyTokenAsync(ct);
        if (token is null)
        {
            return DirectorySyncResult.Empty("Could not acquire a Microsoft Graph token. Check the client secret and admin consent.");
        }

        using var client = httpClientFactory.CreateClient();
        client.BaseAddress = new Uri(GraphBase);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        List<DirectoryPerson> users;
        try
        {
            users = await ReadAllUsersAsync(client, ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Reading users from Entra ID failed.");
            return DirectorySyncResult.Empty($"Reading users from Entra ID failed: {ex.Message}");
        }

        return await importer.ImportAsync(
            users,
            actorName,
            "Entra ID directory sync",
            afterAccount: (person, appUser, token) => StorePhotoAsync(client, person, appUser, token),
            ct: ct);
    }

    // ---------------------------------------------------------------- Graph reads

    private static async Task<List<DirectoryPerson>> ReadAllUsersAsync(HttpClient client, CancellationToken ct)
    {
        var results = new List<DirectoryPerson>();

        // $top=999 is Graph's maximum page size for /users; nextLink is followed until the tenant
        // is exhausted, so this does not quietly stop at the first page on a large directory.
        var next = "users?$select=id,displayName,mail,userPrincipalName,jobTitle,department,companyName,accountEnabled&$top=999";

        while (!string.IsNullOrEmpty(next))
        {
            var response = await client.GetAsync(next, ct);
            response.EnsureSuccessStatusCode();

            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

            if (doc.RootElement.TryGetProperty("value", out var values))
            {
                foreach (var u in values.EnumerateArray())
                {
                    results.Add(new DirectoryPerson(
                        ObjectId: Str(u, "id") ?? string.Empty,
                        DisplayName: Str(u, "displayName") ?? string.Empty,
                        Mail: Str(u, "mail"),
                        UserPrincipalName: Str(u, "userPrincipalName"),
                        JobTitle: Str(u, "jobTitle"),
                        Department: Str(u, "department"),
                        CompanyName: Str(u, "companyName"),
                        AccountEnabled: !u.TryGetProperty("accountEnabled", out var enabled)
                                        || enabled.ValueKind != JsonValueKind.False));
                }
            }

            next = doc.RootElement.TryGetProperty("@odata.nextLink", out var link) ? link.GetString() : null;
        }

        return results;
    }

    private static string? Str(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    /// <summary>Stores the Entra profile photo under wwwroot so the nav and tables can show it.
    /// A user with no photo returns 404, which is normal and not an error.</summary>
    private async Task<bool> StorePhotoAsync(HttpClient client, DirectoryPerson user, ApplicationUser appUser, CancellationToken ct)
    {
        try
        {
            using var response = await client.GetAsync($"users/{user.ObjectId}/photo/$value", ct);
            if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.NoContent) return false;
            if (!response.IsSuccessStatusCode) return false;

            var bytes = await response.Content.ReadAsByteArrayAsync(ct);
            if (bytes.Length == 0) return false;

            var directory = Path.Combine(environment.WebRootPath, "uploads", "profile-pictures");
            Directory.CreateDirectory(directory);

            // Deterministic name per account, so a re-run replaces rather than accumulating files.
            var fileName = $"{appUser.Id}.jpg";
            await File.WriteAllBytesAsync(Path.Combine(directory, fileName), bytes, ct);

            var webPath = $"/uploads/profile-pictures/{fileName}";
            if (appUser.ProfilePicturePath != webPath)
            {
                appUser.ProfilePicturePath = webPath;
                await userManager.UpdateAsync(appUser);
            }

            return true;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not store the Entra photo for {DisplayName}.", user.DisplayName);
            return false;
        }
    }

    // ---------------------------------------------------------------- upserts

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
        catch (MsalException ex)
        {
            logger.LogError(ex, "Acquiring an application token for Microsoft Graph failed.");
            return null;
        }
    }
}

public class NotConfiguredEntraDirectorySync : IEntraDirectorySync
{
    public bool IsConfigured => false;

    public Task<DirectorySyncResult> SyncAsync(string actorName, CancellationToken ct = default) =>
        Task.FromResult(DirectorySyncResult.Empty("Entra ID is not configured for this deployment."));
}
