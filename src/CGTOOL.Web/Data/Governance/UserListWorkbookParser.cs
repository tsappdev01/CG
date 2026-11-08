using System.Data;

namespace CGTOOL.Web.Data.Governance;

/// <summary>What a spreadsheet upload produced: the people it could read, and a line per row it
/// could not, naming the row number so the file can be corrected.</summary>
public record UserListParseResult(
    List<DirectoryPerson> People,
    List<string> Problems,
    int BlankRowsSkipped,
    /// <summary>The Emirates ID and DFM details, for the rows that carry them. Optional: a plain
    /// user list has none of these columns and imports exactly as it always did.</summary>
    List<IdentityImportRow> Identities);

/// <summary>
/// Reads a spreadsheet of people into the same shape the Entra ID sync produces, so a deployment
/// with no tenant connection can still load its directory.
///
/// Columns are matched by name, not position, and each has a couple of accepted spellings, because
/// the file is exported by hand from AD and the headings vary. Display Name, Email Address and
/// Company Name are the three a row cannot do without: the first two are what a person is, and
/// without an entity there is nowhere to file them -- the same rule the Entra import applies.
/// </summary>
public static class UserListWorkbookParser
{
    // Normalized (lowercased, spaces/hyphens/dots removed) header spellings, in preference order.
    private static readonly string[] DisplayName  = ["displayname", "fullname", "name"];
    private static readonly string[] Email        = ["emailaddress", "email", "mail", "userprincipalname", "upn"];
    private static readonly string[] Company      = ["companyname", "company", "entity"];
    private static readonly string[] Department   = ["department", "dept"];
    private static readonly string[] Title        = ["title", "jobtitle", "designation"];
    private static readonly string[] Role         = ["role", "systemrole", "accessrole"];
    private static readonly string[] Manager      = ["reportingmanager", "manager", "reportsto", "reportingto"];
    private static readonly string[] WindowsId    = ["windowsuserid", "windowsid", "samaccountname", "username"];
    private static readonly string[] Enabled      = ["accountenabled", "enabled", "active", "status"];

    // Onboarding reference data. Optional -- a file without these columns still imports people.
    private static readonly string[] Nin          = ["nin", "ninnumber", "nationalinvestornumber"];
    private static readonly string[] NameOnId     = ["nameonemiratesid", "nameonid", "nameoneid"];
    private static readonly string[] EmiratesId   = ["emiratesid", "emiratesidnumber", "eid"];
    private static readonly string[] Nationality  = ["nationality"];

    /// <summary>The headings written into the downloadable template, in this order.</summary>
    public static readonly string[] TemplateHeaders =
        ["Display Name", "Windows User ID", "Department", "Title", "Email Address", "Company Name", "Role", "Reporting Manager"];

    /// <param name="excludedDomains">Email domains to leave out of the file entirely -- people
    /// and onboarding details alike. Companies in the group whose staff do not declare: creating
    /// members for them puts them on the declaration mailing, because that goes to every active
    /// member with an address.</param>
    public static UserListParseResult Parse(
        Stream fileStream, string? fileName = null, IReadOnlyList<string>? excludedDomains = null)
    {
        var table = WorkbookTable.ReadFirstSheet(fileStream, fileName);

        // The header is located by the two columns every version of this file has. Saying so here
        // means a file missing them fails with that sentence rather than importing nothing quietly.
        var headerRowIndex = FindHeader(table);
        var columns = WorkbookTable.MapColumns(table, headerRowIndex);

        var missing = new List<string>();
        if (!WorkbookTable.HasAny(columns, DisplayName)) missing.Add("Display Name");
        if (!WorkbookTable.HasAny(columns, Email)) missing.Add("Email Address");
        if (!WorkbookTable.HasAny(columns, Company)) missing.Add("Company Name");
        if (missing.Count > 0)
        {
            throw new InvalidDataException(
                $"The file is missing required column(s): {string.Join(", ", missing)}. " +
                "Download the template to see the headings this expects.");
        }

        var people = new List<DirectoryPerson>();
        var identities = new List<IdentityImportRow>();
        var problems = new List<string>();
        var blanks = 0;
        var excluded = 0;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (var i = headerRowIndex + 1; i < table.Rows.Count; i++)
        {
            var row = table.Rows[i];
            if (WorkbookTable.IsBlankRow(row)) { blanks++; continue; }

            // Spreadsheet row numbers, 1-based, so a problem points at what the person sees in Excel.
            var rowNumber = i + 1;

            var name = WorkbookTable.TextAny(row, columns, DisplayName);
            var email = WorkbookTable.TextAny(row, columns, Email);
            var company = WorkbookTable.TextAny(row, columns, Company);

            if (IsExcluded(email, excludedDomains)) { excluded++; continue; }

            if (string.IsNullOrWhiteSpace(name)) { problems.Add($"Row {rowNumber}: no Display Name."); continue; }
            if (string.IsNullOrWhiteSpace(email)) { problems.Add($"Row {rowNumber}: {name} has no Email Address."); continue; }
            if (!email.Contains('@')) { problems.Add($"Row {rowNumber}: '{email}' is not an email address."); continue; }
            if (string.IsNullOrWhiteSpace(company)) { problems.Add($"Row {rowNumber}: {name} has no Company Name, so there is no entity to file them under."); continue; }

            // A duplicate address would otherwise be imported twice and quietly win with whichever
            // row came last.
            if (!seen.Add(email))
            {
                problems.Add($"Row {rowNumber}: {email} appears more than once; only the first row was used.");
                continue;
            }

            people.Add(new DirectoryPerson(
                ObjectId: null,                    // a spreadsheet has no Entra object id; matching is on email
                DisplayName: name,
                Mail: email,
                UserPrincipalName: null,
                JobTitle: WorkbookTable.TextAny(row, columns, Title),
                Department: WorkbookTable.TextAny(row, columns, Department),
                CompanyName: company,
                AccountEnabled: ReadEnabled(row, columns),
                RoleName: NormalizeRole(WorkbookTable.TextAny(row, columns, Role)),
                ReportingManagerEmail: NullIfNone(WorkbookTable.TextAny(row, columns, Manager)),
                WindowsUserId: WorkbookTable.TextAny(row, columns, WindowsId)));

            // The same row's Emirates ID and DFM details, when the file carries them. Kept only
            // where there is something to keep: a row with neither is nothing for onboarding to
            // show, and a blank reference row would make "we hold nothing for this person"
            // indistinguishable from "nobody has looked yet".
            var nin = Clean(WorkbookTable.TextAny(row, columns, Nin));
            var eid = Clean(WorkbookTable.TextAny(row, columns, EmiratesId));
            if (nin is not null || eid is not null)
            {
                identities.Add(new IdentityImportRow(
                    Email: email,
                    FullName: name,
                    CompanyName: company,
                    Nin: nin,
                    NameOnEmiratesId: Clean(WorkbookTable.TextAny(row, columns, NameOnId)),
                    EmiratesIdNumber: eid,
                    Nationality: Clean(WorkbookTable.TextAny(row, columns, Nationality))));
            }
        }

        if (excluded > 0)
        {
            problems.Insert(0,
                $"{excluded} row(s) left out: {string.Join(", ", excludedDomains!)}. " +
                "They are neither imported as people nor given onboarding details.");
        }

        return new UserListParseResult(people, problems, blanks, identities);
    }

    /// <summary>A cell's value, or null when there is nothing in it.
    ///
    /// "Nothing in it" includes the literal text NULL. These files are SQL exports and write NULL
    /// where a value is missing -- in 3,724 of the 3,957 rows of the one this was built against --
    /// so a reader that only tests for an empty string stores the word itself, and onboarding then
    /// asks somebody to confirm that their National Investor Number is "NULL".</summary>
    private static string? Clean(string? value)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrEmpty(trimmed)) return null;
        return trimmed.Equals("NULL", StringComparison.OrdinalIgnoreCase) ? null : trimmed;
    }

    private static int FindHeader(DataTable table)
    {
        // Try the likely spellings of the two mandatory columns rather than one fixed pair.
        foreach (var name in DisplayName)
        {
            foreach (var mail in Email)
            {
                try { return WorkbookTable.FindHeaderRow(table, name, mail); }
                catch (InvalidDataException) { /* try the next spelling */ }
            }
        }

        throw new InvalidDataException(
            "Could not find a header row. The first 30 rows contain no row with both a name column " +
            "(Display Name / Full Name / Name) and an email column (Email Address / Email / User Principal Name).");
    }

    /// <summary>"NormalUser", "Normal User", "normal", blank -- all mean the default role. Anything
    /// beginning "admin" means Administrator. Anything else is passed through and checked against
    /// the roles that exist at import time, so a typo is reported rather than silently downgraded.</summary>
    private static bool IsExcluded(string? email, IReadOnlyList<string>? excludedDomains)
    {
        if (excludedDomains is null or { Count: 0 } || string.IsNullOrWhiteSpace(email)) return false;

        var at = email.LastIndexOf('@');
        if (at < 0) return false;

        var domain = email[(at + 1)..].Trim();
        return excludedDomains.Any(d => domain.Equals(d, StringComparison.OrdinalIgnoreCase));
    }

    private static string? NormalizeRole(string? role)
    {
        if (string.IsNullOrWhiteSpace(role)) return null;

        var compact = WorkbookTable.Normalize(role);
        if (compact.StartsWith("admin")) return GovernanceRoles.Administrator;
        if (compact is "normaluser" or "normal" or "user" or "normalstaff" or "staff") return GovernanceRoles.NormalUser;
        return role.Trim();
    }

    private static string? NullIfNone(string? value) =>
        string.IsNullOrWhiteSpace(value) || WorkbookTable.Normalize(value) is "none" or "na" or "n/a" or "-"
            ? null
            : value.Trim();

    /// <summary>No column at all means everyone is active, which is what a hand-made list implies.</summary>
    private static bool ReadEnabled(DataRow row, Dictionary<string, int> columns)
    {
        var text = WorkbookTable.TextAny(row, columns, Enabled);
        if (string.IsNullOrWhiteSpace(text)) return true;

        return WorkbookTable.Normalize(text) switch
        {
            "false" or "no" or "n" or "0" or "disabled" or "inactive" => false,
            _ => true,
        };
    }
}
