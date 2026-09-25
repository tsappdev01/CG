using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace CGTOOL.Web.Data.Governance;

public interface IDirectoryImporter
{
    /// <summary>
    /// Writes a list of people into the member directory: the company and department they belong to,
    /// the Member record itself, the login account, and the role and reporting line where the source
    /// supplies them.
    /// </summary>
    /// <param name="sourceLabel">Named in the audit entry, e.g. "Entra ID directory sync".</param>
    /// <param name="afterAccount">Run once per person after their login account exists; returns true
    /// when it stored something worth counting. The Entra path uses it to fetch the profile photo,
    /// which a spreadsheet cannot supply.</param>
    Task<DirectorySyncResult> ImportAsync(
        IReadOnlyList<DirectoryPerson> people,
        string actorName,
        string sourceLabel,
        Func<DirectoryPerson, ApplicationUser, CancellationToken, Task<bool>>? afterAccount = null,
        CancellationToken ct = default);
}

/// <summary>
/// The half of the directory import that does not care where the people came from.
///
/// Entra ID is one source; a spreadsheet uploaded on User Management is the other, for deployments
/// with no tenant connection. Both produce a list of <see cref="DirectoryPerson"/> and hand it here,
/// so there is one definition of what importing a person means rather than two that drift.
///
/// It is one-way. Identity fields are refreshed on every run; everything the governance process owns
/// -- declaration access flags, RP transaction role, impersonation approvals -- is left exactly as an
/// administrator set it. Writes go through the stored-procedure writers like every other write in
/// this application; Identity's own tables are the documented exception and use UserManager.
/// </summary>
public class DirectoryImporter(
    ApplicationDbContext db,
    ICompanyWriter companyWriter,
    IDepartmentWriter departmentWriter,
    IMemberWriter memberWriter,
    UserManager<ApplicationUser> userManager,
    RoleManager<IdentityRole> roleManager,
    IAuditLogger auditLog,
    ILogger<DirectoryImporter> logger) : IDirectoryImporter
{
    public async Task<DirectorySyncResult> ImportAsync(
        IReadOnlyList<DirectoryPerson> people,
        string actorName,
        string sourceLabel,
        Func<DirectoryPerson, ApplicationUser, CancellationToken, Task<bool>>? afterAccount = null,
        CancellationToken ct = default)
    {
        int created = 0, updated = 0, skipped = 0, photos = 0;
        var problems = new List<string>();

        // Loaded once and kept in step as we go, so a run that introduces a new company or
        // department does not re-create it for every later user that shares it.
        var companies = await db.Companies.ToListAsync(ct);
        var departments = await db.Departments.ToListAsync(ct);
        var members = await db.Members.ToListAsync(ct);

        foreach (var user in people)
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

                // The object id is the better key when there is one, because it survives a person
                // changing their address -- but a spreadsheet has none, and matching on a null one
                // would pair every row with the first member that also had none. Email is the only
                // handle a spreadsheet carries, which is why it must be unique in the file.
                var member =
                    (user.ObjectId is { Length: > 0 }
                        ? members.FirstOrDefault(m => m.AzureAdObjectId == user.ObjectId)
                        : null)
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
                    // A spreadsheet import must not erase the link an Entra sync established.
                    if (user.ObjectId is { Length: > 0 }) member.AzureAdObjectId = user.ObjectId;
                    member.IsManualEntry = false;
                    member.Active = user.AccountEnabled;
                    await memberWriter.UpdateAsync(member);
                    updated++;
                }

                var appUser = await EnsureLoginAccountAsync(user, member, ct);
                if (appUser is null) continue;

                // A role named in the source is applied; Entra never names one, so its users
                // keep the Normal User grant EnsureLoginAccountAsync gives a new account.
                if (!string.IsNullOrWhiteSpace(user.RoleName))
                {
                    var roleProblem = await ApplyRoleAsync(appUser, user.RoleName!);
                    if (roleProblem is not null) problems.Add($"{user.DisplayName}: {roleProblem}");
                }

                if (afterAccount is not null && await afterAccount(user, appUser, ct)) photos++;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Importing {DisplayName} from Entra ID failed.", user.DisplayName);
                problems.Add($"{user.DisplayName}: {ex.Message}");
                skipped++;
            }
        }

        // A reporting manager can only be linked once everyone exists, since in a list like this
        // the manager is often further down than the person reporting to them.
        var managerLinks = await LinkReportingManagersAsync(people, members, problems);

        await auditLog.LogAsync(
            actorName,
            AuditAction.Update,
            nameof(Member),
            "DirectoryImport",
            $"{sourceLabel}: {created} created, {updated} updated, {skipped} skipped, {photos} photo(s) stored, {managerLinks} reporting manager(s) linked.");

        return new DirectorySyncResult(created, updated, skipped, photos, problems);
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
    private async Task<ApplicationUser?> EnsureLoginAccountAsync(DirectoryPerson user, Member member, CancellationToken ct)
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

    /// <summary>
    /// Grants the role the source named, and takes away the other built-in one so a change of role
    /// in the file is a change here. A name that is not a role is reported rather than guessed at --
    /// being silently left as Normal User is the kind of thing nobody notices until it matters.
    /// </summary>
    private async Task<string?> ApplyRoleAsync(ApplicationUser appUser, string roleName)
    {
        if (!await roleManager.RoleExistsAsync(roleName))
        {
            return $"'{roleName}' is not a role in this system, so their role was left unchanged.";
        }

        var held = await userManager.GetRolesAsync(appUser);
        if (held.Contains(roleName, StringComparer.OrdinalIgnoreCase)) return null;

        // Only the two built-in roles are exchanged; a custom role granted by hand is left alone.
        var toRemove = held.Where(r =>
            GovernanceRoles.All.Contains(r, StringComparer.OrdinalIgnoreCase)
            && !r.Equals(roleName, StringComparison.OrdinalIgnoreCase)).ToList();

        if (toRemove.Count > 0) await userManager.RemoveFromRolesAsync(appUser, toRemove);
        await userManager.AddToRoleAsync(appUser, roleName);
        return null;
    }

    /// <summary>
    /// Second pass over the same list, matching each person's manager by email. Deliberately after
    /// the main loop: the manager is often a row further down, and may have been created by this
    /// very run. A manager in neither the file nor the directory is reported and the person is left
    /// without one, rather than the row failing.
    /// </summary>
    private async Task<int> LinkReportingManagersAsync(
        IReadOnlyList<DirectoryPerson> people, List<Member> members, List<string> problems)
    {
        var withManagers = people.Where(p => !string.IsNullOrWhiteSpace(p.ReportingManagerEmail)).ToList();
        if (withManagers.Count == 0) return 0;

        var byEmail = members
            .Where(m => !string.IsNullOrWhiteSpace(m.Email))
            .GroupBy(m => m.Email!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var linked = 0;

        foreach (var person in withManagers)
        {
            if (person.Address is null || !byEmail.TryGetValue(person.Address, out var member)) continue;

            if (!byEmail.TryGetValue(person.ReportingManagerEmail!, out var manager))
            {
                problems.Add($"{person.DisplayName}: reporting manager {person.ReportingManagerEmail} is not in this file or the directory, so none was set.");
                continue;
            }

            if (manager.Id == member.Id)
            {
                problems.Add($"{person.DisplayName}: is listed as their own reporting manager, which was ignored.");
                continue;
            }

            if (member.ReportingManagerId == manager.Id) continue;

            var previous = member.ReportingManagerId;
            member.ReportingManagerId = manager.Id;
            try
            {
                await memberWriter.UpdateAsync(member);
                linked++;
            }
            catch (Exception ex)
            {
                // usp_Member_Update refuses a reporting cycle (error 50004). Report it and move on
                // rather than failing the whole import over one bad line.
                member.ReportingManagerId = previous;
                logger.LogWarning(ex, "Could not link {Person} to {Manager}.", person.DisplayName, manager.FullName);
                problems.Add($"{person.DisplayName}: could not be linked to {manager.FullName} -- {ex.Message}");
            }
        }

        return linked;
    }
}
