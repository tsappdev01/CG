using System.Net;
using System.Reflection;
using System.Text;
using CGTOOL.Web.Data.Governance;

namespace CGTOOL.Checks;

/// <summary>
/// "Fill from directory" on a member's record read the entity's whole employee list and used the
/// first row of it. On a tenant that answered at all, that wrote the first employee's name, job
/// title, address and Entra object id over whichever person was being edited. On this tenant it
/// answered nothing at all, because the lookup asked Graph to `$filter=companyName eq '...'` and
/// companyName is not an ordinary filterable property on /users -- so every click reported "No
/// Azure AD records were returned for this entity" and the real defect stayed hidden behind it.
///
/// These check the reading that replaced it, and the defaults a newly created member carries.
/// </summary>
public static class DirectoryFillChecks
{
    public static void Run()
    {
        Section_NoListingToPickTheWrongPersonFrom();
        Section_ReadingGraph();
        Section_NewMemberDefaults();
        Section_WhyThereIsNoPicture();
        Section_DepartmentsFromTheDirectory();
        Section_EnrichingAnUploadedRow();
    }

    private static void Section_NoListingToPickTheWrongPersonFrom()
    {
        Check.Section("Directory lookup is by identity");

        var methods = typeof(IDirectoryEmployeeProvider)
            .GetMethods()
            .Select(m => m.Name)
            .ToHashSet(StringComparer.Ordinal);

        // The guard against the original bug: there is no "hand me the entity's people" call to
        // take the first row of. Anything wanting one person has to say which person.
        Check.That("the provider offers no bulk employee listing to guess from",
                   !methods.Contains("GetEmployeesAsync"));

        Check.That("it offers a lookup by identity", methods.Contains("FindEmployeeAsync"));
        Check.That("it offers a photo fetch", methods.Contains("GetPhotoAsync"));

        var find = typeof(IDirectoryEmployeeProvider).GetMethod("FindEmployeeAsync");
        var parameters = find?.GetParameters().Select(p => p.Name).ToArray() ?? [];

        Check.That("the lookup takes the object id, the address and the name of the person wanted",
                   parameters.Contains("objectId") && parameters.Contains("email") && parameters.Contains("fullName"));
    }

    private static void Section_ReadingGraph()
    {
        Check.Section("Reading Microsoft Graph");

        var type = typeof(GraphDirectoryEmployeeProvider);

        var matches = type.GetMethod("Matches", BindingFlags.NonPublic | BindingFlags.Static);
        Check.That("the entity match is made here, not by Graph", matches is not null);

        if (matches is not null)
        {
            bool Match(string? a, string? b) => (bool)matches.Invoke(null, [a, b])!;

            // The reason the match moved out of $filter in the first place: Graph compares
            // companyName case-sensitively, and the entity names here are not typed consistently.
            Check.That("entity names match regardless of case", Match("DIP HOLDING PLC", "DIP Holding PLC"));
            Check.That("entity names match regardless of surrounding spaces", Match(" DIP Holding PLC ", "DIP Holding PLC"));
            Check.That("different entities do not match", !Match("DIP Holding PLC", "DIP Investments"));
            Check.That("a blank entity name matches nothing", !Match(null, "DIP Holding PLC") && !Match("  ", "DIP Holding PLC"));
        }

        var read = type.GetMethod("Read", BindingFlags.NonPublic | BindingFlags.Static);
        Check.That("a Graph user can be read into a record", read is not null);

        if (read is not null)
        {
            object? Row(string json)
            {
                using var doc = System.Text.Json.JsonDocument.Parse(json);
                return read.Invoke(null, [doc.RootElement.Clone()]);
            }

            static DirectoryEmployeeRecord? RecordOf(object? row) =>
                row is null ? null : (DirectoryEmployeeRecord?)row.GetType()
                    .GetProperty("Record")!.GetValue(row);

            var full = RecordOf(Row("""
                {"id":"abc","displayName":"A Person","mail":"a.person@di.ae",
                 "userPrincipalName":"a.person@di.ae","jobTitle":"Head of Treasury",
                 "department":"Treasury","companyName":"DIP Holding PLC"}
                """));

            Check.That("the name, title and department all come across",
                       full is { FullName: "A Person", JobTitle: "Head of Treasury", DepartmentName: "Treasury" });
            Check.That("the object id comes across, so the match can be made exactly next time",
                       full?.SystemId == "abc");

            // Entra leaves mail null on plenty of accounts; the sign-in name is the address then.
            var noMail = RecordOf(Row("""
                {"id":"b","displayName":"B Person","mail":null,"userPrincipalName":"b.person@di.ae"}
                """));
            Check.That("a user with no mail falls back to the sign-in name", noMail?.Email == "b.person@di.ae");

            Check.That("a row with no display name is not turned into a nameless record",
                       Row("""{"id":"c"}""") is null);
        }

        var readAll = type.GetMethod("ReadAllAsync", BindingFlags.NonPublic | BindingFlags.Static);
        Check.That("the reader exists and can be exercised", readAll is not null);

        if (readAll is not null)
        {
            // /users answers 100 rows at a time and hands back an @odata.nextLink for the rest.
            // A reader that stops at the first page reports a directory that ends partway
            // through, which on screen is indistinguishable from the person not being in it.
            var pages = new Queue<string>(
            [
                """
                {"value":[{"id":"1","displayName":"One","companyName":"DIP Holding PLC"}],
                 "@odata.nextLink":"https://graph.microsoft.com/v1.0/users?$skiptoken=x"}
                """,
                """
                {"value":[{"id":"2","displayName":"Two","companyName":"DIP Holding PLC"}]}
                """,
            ]);

            using var client = new HttpClient(new QueueHandler(pages))
            {
                BaseAddress = new Uri("https://graph.microsoft.com/v1.0/"),
            };

            var task = (Task)readAll.Invoke(null, [client, "users?$top=999", CancellationToken.None])!;
            task.GetAwaiter().GetResult();
            var rows = (System.Collections.ICollection)task.GetType().GetProperty("Result")!.GetValue(task)!;

            Check.That("every page of the directory is read, not just the first", rows.Count == 2);
        }
    }

    private static void Section_NewMemberDefaults()
    {
        Check.Section("What a new member declares");

        var member = MemberDefaults.ApplyToNew(new Member { FullName = "A Person" });

        // Imported people arrived with every flag false, so onboarding had nothing to ask them
        // for and My Workspace stayed empty until an administrator ticked the boxes by hand.
        Check.That("Insider Trading is ticked", member.InsiderTradingAccess);
        Check.That("Conflict of Interest is ticked", member.ConflictOfInterestAccess);
        Check.That("Related Party Register is ticked", member.RelatedPartyRegisterAccess);

        // Not a declaration everyone files: it is a place in the approval workflow, and ticking
        // it for everybody would put every member into that chain.
        Check.That("Related Party Transactions is left alone", !member.RelatedPartyTransactionAccess);
    }

    private static void Section_WhyThereIsNoPicture()
    {
        Check.Section("A missing picture says why");

        // Every way the fetch can come back empty used to return the same null, so "the directory
        // has no photograph for them" and "the tenant will not hand photos to this app
        // registration" were the same blank avatar and the same silence.
        foreach (var outcome in Enum.GetValues<DirectoryPhotoOutcome>())
        {
            var explained = DirectoryPhoto.Unavailable(outcome).Explain("A Person");

            if (outcome == DirectoryPhotoOutcome.Fetched)
            {
                Check.That("a photo that arrived needs no explanation", explained is null);
                continue;
            }

            Check.That($"{outcome} is explained to the administrator", explained is { Length: > 10 });
        }

        Check.That("a refusal names the permission to grant",
                   DirectoryPhoto.Unavailable(DirectoryPhotoOutcome.Refused)
                       .Explain("A Person")?.Contains("User.Read.All", StringComparison.Ordinal) == true);

        Check.That("an empty body is not mistaken for a picture",
                   !new DirectoryPhoto(DirectoryPhotoOutcome.Fetched, [], "image/jpeg").HasContent);

        Check.That("bytes that did arrive are usable",
                   new DirectoryPhoto(DirectoryPhotoOutcome.Fetched, [1, 2, 3], "image/jpeg").HasContent);
    }

    private static void Section_DepartmentsFromTheDirectory()
    {
        Check.Section("Departments named by the directory");

        // The import created a department the directory named; the button on a member's record
        // refused and said so, which on screen read as the directory having no department. Both
        // now go through one code generator, so a department reaches the database the same way
        // whichever path met it first.
        var taken = new List<string?> { "FINANCE", "TREASURY" };

        var fresh = ShortCodes.Unique("TS Digital Transformation", taken);
        Check.That("a new department gets a code of its own", fresh is { Length: > 0 } && !taken.Contains(fresh));

        taken.Add(fresh);
        var again = ShortCodes.Unique("TS Digital Transformation", taken);
        Check.That("a second department of the same name does not collide", again != fresh && !taken.Contains(again));

        Check.That("the comparison ignores case",
                   ShortCodes.Unique("Finance", ["finance"]) is var code && !code.Equals("FINANCE", StringComparison.OrdinalIgnoreCase));

        Check.That("a name with no letters still produces a code",
                   ShortCodes.Unique("---", []) is { Length: > 0 });

        Check.That("the code fits the column, which is 20 characters",
                   ShortCodes.Unique("A Very Long Department Name Indeed", []).Length <= 20);
    }

    private static void Section_EnrichingAnUploadedRow()
    {
        Check.Section("Completing an uploaded row from the directory");

        // What the template actually produces: a first name and an address, nothing else.
        var row = new DirectoryPerson(
            ObjectId: null, DisplayName: "A Person", Mail: "a.person@example.invalid", UserPrincipalName: null,
            JobTitle: null, Department: null, CompanyName: "An Entity", AccountEnabled: true,
            RoleName: "Normal User");

        var record = new DirectoryEmployeeRecord(
            FullName: "A Person Of Consequence", JobTitle: "HCM Consultant",
            DepartmentName: "Digital Transformation", Email: "a.person@example.invalid",
            Signature: null, ReportingManagerName: null, SystemId: "11111111-2222-3333-4444-555555555555");

        var merged = DirectoryEnrichment.Merge(row, record);

        Check.That("the job title the file left blank is filled", merged.JobTitle == "HCM Consultant");
        Check.That("the department the file left blank is filled", merged.Department == "Digital Transformation");
        Check.That("the full name comes from the directory", merged.DisplayName == "A Person Of Consequence");
        Check.That("the object id the file could not supply is attached",
                   merged.ObjectId == "11111111-2222-3333-4444-555555555555");
        Check.That("it counts as a row the directory completed", DirectoryEnrichment.Changed(row, merged));

        // The file still owns what the directory does not carry.
        Check.That("the entity the file filed them under is untouched", merged.CompanyName == "An Entity");
        Check.That("the address is untouched", merged.Mail == "a.person@example.invalid");
        Check.That("the role named in the file is untouched", merged.RoleName == "Normal User");
        Check.That("whether the account is enabled is untouched", merged.AccountEnabled);

        // A directory entry with gaps of its own must not blank out what the file did say.
        var sparse = new DirectoryEmployeeRecord(
            FullName: "", JobTitle: null, DepartmentName: "   ", Email: null,
            Signature: null, ReportingManagerName: null, SystemId: null);

        var typed = row with { DisplayName = "A Person Of Consequence", JobTitle = "Consultant", Department = "IT" };
        var kept = DirectoryEnrichment.Merge(typed, sparse);

        Check.That("a blank directory name does not erase the file's", kept.DisplayName == "A Person Of Consequence");
        Check.That("a missing directory title does not erase the file's", kept.JobTitle == "Consultant");
        Check.That("a whitespace directory department does not erase the file's", kept.Department == "IT");
        Check.That("nothing changed means nothing to report", !DirectoryEnrichment.Changed(typed, kept));
    }

    private sealed class QueueHandler(Queue<string> pages) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    pages.Count > 0 ? pages.Dequeue() : """{"value":[]}""",
                    Encoding.UTF8,
                    "application/json"),
            });
    }
}
