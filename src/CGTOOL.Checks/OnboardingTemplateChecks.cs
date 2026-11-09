using System.Data;
using System.Text;
using CGTOOL.Web.Data.Governance;
using ClosedXML.Excel;

namespace CGTOOL.Checks;

/// <summary>
/// The onboarding template, read the way the upload screen reads it.
///
/// Built here rather than loaded from a file so the check travels with the code: the columns are
/// the template's, including the two that disagree on purpose and the ones whose headers carry a
/// parenthetical the header matcher does not strip.
/// </summary>
public static class OnboardingTemplateChecks
{
    public static void Run()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

        using var book = new XLWorkbook();
        var sheet = book.AddWorksheet("Sheet3 (2)");

        string[] headers =
        [
            "Emirates ID", "EmailAddress", "Name", "Company", "NIN",
            "NameOnEmiratesID (User Data)", "NameOnEmiratesID_Arabic",
            "NameOnEmiratesID (ID Data)", "Nationality", "Expiry Date",
        ];
        for (var c = 0; c < headers.Length; c++) sheet.Cell(1, c + 1).Value = headers[c];

        // A row of the shape the real file has: a short NIN, the two card names disagreeing, the
        // expiry as the day number Excel stores when the column was never formatted as a date,
        // and a nationality in the long form the card export writes.
        sheet.Cell(2, 1).Value = "784197831806167";
        sheet.Cell(2, 2).Value = "someone@example.com";
        sheet.Cell(2, 3).Value = "Short Name";
        sheet.Cell(2, 4).Value = "An Entity";
        sheet.Cell(2, 5).Value = "98656533";
        sheet.Cell(2, 6).Value = "Typed By The Team";
        sheet.Cell(2, 7).Value = "الاسم بالعربية";
        sheet.Cell(2, 8).Value = "Read Off The Card";
        sheet.Cell(2, 9).Value = "State Of Palestine";
        sheet.Cell(2, 10).Value = 47154;

        // A row carrying Excel's broken-reference marker and no card data at all.
        sheet.Cell(3, 1).Value = "NULL";
        sheet.Cell(3, 2).Value = "other@example.com";
        sheet.Cell(3, 3).Value = "No Card Person";
        sheet.Cell(3, 4).Value = "An Entity";
        sheet.Cell(3, 5).Value = "NULL";
        sheet.Cell(3, 9).Value = "#REF!";

        using var stream = new MemoryStream();
        book.SaveAs(stream);
        stream.Position = 0;

        var parsed = UserListWorkbookParser.Parse(stream, "onboarding_Template.xlsx", []);

        Check.Section("Onboarding template");
        Check.That("both people are read, card or no card", parsed.People.Count == 2);
        Check.That("only the one with card details becomes an identity row", parsed.Identities.Count == 1);

        var row = parsed.Identities.FirstOrDefault();
        Check.That("that row is the right person", row?.Email == "someone@example.com");
        if (row is null) return;

        Check.That("the card's own name wins over the one typed by the team",
                   row.NameOnEmiratesId == "Read Off The Card");
        Check.That("the Arabic name is kept", row.NameOnEmiratesIdArabic == "الاسم بالعربية");
        Check.That("the Emirates ID number is kept", row.EmiratesIdNumber == "784197831806167");

        Check.That($"a short NIN is padded to ten ({row.Nin})", row.Nin == "0098656533");
        Check.That("and the padded one passes the rule the screens apply", Nin.IsValid(row.Nin));

        Check.That($"the expiry is read from Excel's day number ({row.EmiratesIdExpiryDate:dd/MM/yyyy})",
                   row.EmiratesIdExpiryDate == new DateTime(2029, 2, 5));

        Check.That($"the long-form nationality resolves ({row.Nationality} -> {Countries.Resolve(row.Nationality)})",
                   Countries.Resolve(row.Nationality) == "Palestine");

        var noCard = parsed.People.FirstOrDefault(p => p.Mail == "other@example.com");
        Check.That("a person with no card details is still imported", noCard is not null);
        Check.That("and leaves no identity row behind",
                   parsed.Identities.All(i => i.Email != "other@example.com"));
    }
}
