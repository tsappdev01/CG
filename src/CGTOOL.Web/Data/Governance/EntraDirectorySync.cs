using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Identity.Client;
using CGTOOL.Web.Data;

namespace CGTOOL.Web.Data.Governance;

/// <summary>One person as Entra ID holds them.</summary>
public record EntraUser(
    string ObjectId,
    string DisplayName,
    string? Mail,
    string? UserPrincipalName,
    string? JobTitle,
    string? Department,
    string? CompanyName,
    bool AccountEnabled)
{
    /// <summary>Entra allows a user with no mailbox, in which case the UPN is the address we have.</summary>
    public string? Address => string.IsNullOrWhiteSpace(Mail) ? UserPrincipalName : Mail;
}

public record EntraSyncResult(int Created, int Updated, int Skipped, int PhotosStored, IReadOnlyList<string> Problems)
{
    public static EntraSyncResult Empty(string problem) => new(0, 0, 0, 0, [problem]);
    public int Total => Created + Updated;
}

public interface IEntraDirectorySync
{
    bool IsConfigured { get; }

    /// <summary>Reads every user from Entra ID and writes them into the member directory.</summary>
    Task<EntraSyncResult> SyncAsync(string actorName, CancellationToken ct = default);
}

/// <summary>
/// Loads the member directory from Entra ID: every user in the tenant, with the profile fields the
/// governance screens display -- full name, email, company, department, designation and photo.
///
/// This is a one-way import. Entra is the system of record for who exists and what their profile
/// says, so a synced member's identity fields are refreshed on every run; everything the governance
/// process owns (declaration access flags, RP transaction role, reporting manager, impersonation
/// approvals) is left exactly as an administrator set it.
///
/// Writes go through the stored-procedure writers, like every other write in this application --
/// see scripts/stored-procedures.sql. Identity's own tables are the documented exception and use
/// UserManager directly.
/// </summary>
public class EntraDirectorySync(
    IConfiguration configuration,
    IHttpClientFactory httpClientFactory,
    ApplicationDbContext db,
    ICompanyWriter companyWriter,
    IDepartmentWriter departmentWriter,
    IMemberWriter memberWriter,
    UserManager<ApplicationUser> userManager,
    IAuditLogger auditLog,
    IWebHostEnvironment environment,
    ILogger<EntraDirectorySync> logger) : IEntraDirectorySync
{
    private const string GraphBase = "https://graph.microsoft.com/v1.0/";

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(configuration["AzureAd:TenantId"])
        && !string.IsNullOrWhiteSpace(configuration["AzureAd:ClientId"])
        && !string.IsNullOrWhiteSpace(configuration["AzureAd:ClientSecret"]);

    public async Task<EntraSyncResult> SyncAsync(string actorName, CancellationToken ct = default)
    {
        if (!IsConfigured)
        {
            return EntraSyncResult.Empty(
                "Entra ID is not configured. Set AzureAd:TenantId, AzureAd:ClientId and AzureAd:ClientSecret, " +
                "and grant the application the User.Read.All Graph permission.");
        }

        var token = await AcquireAppOnlyTokenAsync(ct);
        if (token is null)
        {
            return EntraSyncResult.Empty("Could not acquire a Microsoft Graph token. Check the client secret and admin consent.");
        }

        using var client = httpClientFactory.CreateClient();
        client.BaseAddress = new Uri(GraphBase);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        List<EntraUser> users;
        try
        {
            users = await ReadAllUsersAsync(client, ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Reading users from Entra ID failed.");
            return EntraSyncResult.Empty($"Reading users from Entra ID failed: {ex.Message}");
        }

        int created = 0, updated = 0, skipped = 0, photos = 0;
        var problems = new List<string>();

        // Loaded once and kept in step as we go, so a run that introduces a new company or
        // department does not re-create it for every later user that shares it.
        var companies = await db.Companies.ToListAsync(ct);
        var departments = await db.Departments.ToListAsync(ct);
        var members = await db.Members.ToListAsync(ct);

        foreach (var user in users)
        {
            ct.ThrowIfCancellationRequested();

            if (string.IsNullOrWhiteSpace(user.DisplayName) || string.IsNullOrWhiteSpace(user.Address))
            {
                skipped++;
                continue;
            }

            try
            {
                var companyId = await ResolveCompanyAsync(user.CompanyName, companies);
                if (companyId is null)
                {
                    // Every member belongs to an entity, so a user with no companyName in Entra
                    // cannot be placed. Reported rather than guessed at.
                    problems.Add($"{user.DisplayName}: no companyName in Entra ID, so there is no entity to file them under.");
                    skipped++;
                    continue;
                }

                var departmentId = await ResolveDepartmentAsync(user.Department, departments);

                var member = members.FirstOrDefault(m => m.AzureAdObjectId == user.ObjectId)
                             ?? members.FirstOrDefault(m => m.Email != null && m.Email.Equals(user.Address, StringComparison.OrdinalIgnoreCase));

                if (member is null)
                {
                    member = new Member
                    {
                        CompanyId = companyId.Value,
                        DepartmentId = departmentId,
                        FullName = user.DisplayName,
                        JobTitle = user.JobTitle,
                        Email = user.Address,
                        AzureAdObjectId = user.ObjectId,
                        IsManualEntry = false,
                        Active = user.AccountEnabled,
                    };
                    member.Id = await memberWriter.InsertAsync(member);
                    members.Add(member);
                    created++;
                }
                else
                {
                    // Entra owns identity; the governance flags on the row are left untouched.
                    member.CompanyId = companyId.Value;
                    member.DepartmentId = departmentId;
                    member.FullName = user.DisplayName;
                    member.JobTitle = user.JobTitle;
                    member.Email = user.Address;
                    member.AzureAdObjectId = user.ObjectId;
                    member.IsManualEntry = false;
                    member.Active = user.AccountEnabled;
                    await memberWriter.UpdateAsync(member);
                    updated++;
                }

                var appUser = await EnsureLoginAccountAsync(user, member, ct);
                if (appUser is not null && await StorePhotoAsync(client, user, appUser, ct)) photos++;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Importing {DisplayName} from Entra ID failed.", user.DisplayName);
                problems.Add($"{user.DisplayName}: {ex.Message}");
                skipped++;
            }
        }

        await auditLog.LogAsync(
            actorName,
            AuditAction.Update,
            nameof(Member),
            "EntraSync",
            $"Entra ID directory sync: {created} created, {updated} updated, {skipped} skipped, {photos} photo(s) stored.");

        return new EntraSyncResult(created, updated, skipped, photos, problems);
    }

    // ---------------------------------------------------------------- Graph reads

    private static async Task<List<EntraUser>> ReadAllUsersAsync(HttpClient client, CancellationToken ct)
    {
        var results = new List<EntraUser>();

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
                    results.Add(new EntraUser(
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
    private async Task<bool> StorePhotoAsync(HttpClient client, EntraUser user, ApplicationUser appUser, CancellationToken ct)
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

    private async Task<int?> ResolveCompanyAsync(string? companyName, List<Company> companies)
    {
        if (string.IsNullOrWhiteSpace(companyName)) return null;

        var existing = companies.FirstOrDefault(c => c.Name.Equals(companyName, StringComparison.OrdinalIgnoreCase));
        if (existing is not null) return existing.Id;

        var company = new Company
        {
            Name = companyName.Trim(),
            ShortCode = UniqueShortCode(companyName, companies.Select(c => c.ShortCode)),
        };
        company.Id = await companyWriter.InsertAsync(company);
        companies.Add(company);
        return company.Id;
    }

    private async Task<int?> ResolveDepartmentAsync(string? departmentName, List<Department> departments)
    {
        if (string.IsNullOrWhiteSpace(departmentName)) return null;

        var existing = departments.FirstOrDefault(d => d.Name.Equals(departmentName, StringComparison.OrdinalIgnoreCase));
        if (existing is not null) return existing.Id;

        var department = new Department
        {
            Name = departmentName.Trim(),
            Code = UniqueShortCode(departmentName, departments.Select(d => d.Code)),
        };
        department.Id = await departmentWriter.InsertAsync(department);
        departments.Add(department);
        return department.Id;
    }

    /// <summary>Company.ShortCode and Department.Code are unique, and Entra has no equivalent field,
    /// so one is derived from the name and suffixed until it does not collide.</summary>
    private static string UniqueShortCode(string name, IEnumerable<string> taken)
    {
        var letters = new string(name.Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();
        var seed = (letters.Length == 0 ? "ORG" : letters[..Math.Min(8, letters.Length)]);

        var existing = taken.Where(t => t is not null).Select(t => t.ToUpperInvariant()).ToHashSet();
        if (!existing.Contains(seed)) return seed;

        for (var n = 2; n < 1000; n++)
        {
            var candidate = $"{seed[..Math.Min(6, seed.Length)]}{n}";
            if (!existing.Contains(candidate)) return candidate;
        }

        return $"{seed[..Math.Min(4, seed.Length)]}{Guid.NewGuid().ToString("N")[..4].ToUpperInvariant()}";
    }

    /// <summary>
    /// Every Entra user gets a login account so single sign-on lands on a member record rather than
    /// provisioning a stranger on first use. The account carries no password -- there is nothing to
    /// sign in with except Entra.
    /// </summary>
    private async Task<ApplicationUser?> EnsureLoginAccountAsync(EntraUser user, Member member, CancellationToken ct)
    {
        var address = user.Address!;
        var appUser = await userManager.FindByNameAsync(address) ?? await userManager.FindByEmailAsync(address);

        if (appUser is null)
        {
            appUser = new ApplicationUser
            {
                UserName = address,
                Email = user.Mail ?? address,
                EmailConfirmed = true,
            };

            var created = await userManager.CreateAsync(appUser);
            if (!created.Succeeded)
            {
                logger.LogWarning(
                    "Could not create a login account for {Address}: {Errors}",
                    address, string.Join("; ", created.Errors.Select(e => e.Description)));
                return null;
            }

            await userManager.AddToRoleAsync(appUser, GovernanceRoles.NormalUser);
        }

        // A disabled Entra account must not be able to sign in here either.
        await userManager.SetLockoutEnabledAsync(appUser, true);
        await userManager.SetLockoutEndDateAsync(appUser, user.AccountEnabled ? null : DateTimeOffset.MaxValue);

        if (member.ApplicationUserId != appUser.Id)
        {
            member.ApplicationUserId = appUser.Id;
            await memberWriter.UpdateAsync(member);
        }

        return appUser;
    }

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

    public Task<EntraSyncResult> SyncAsync(string actorName, CancellationToken ct = default) =>
        Task.FromResult(EntraSyncResult.Empty("Entra ID is not configured for this deployment."));
}
