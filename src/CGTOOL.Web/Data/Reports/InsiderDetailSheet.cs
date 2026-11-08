using CGTOOL.Web.Data.Governance;

namespace CGTOOL.Web.Data.Reports;

/// <summary>One declarant's Insider Trading sheet: their answers, the NINs their relatives hold,
/// what they uploaded, and the shares bought, sold and held over the period.
///
/// Its own class because two places show it and they must not drift: the detailed submission
/// report, where a reviewer reads everyone's, and a declarant's own print view in My Declarations,
/// which used to be a hand-written table that had quietly fallen a release behind -- no share
/// transactions on it at all, and a document list that stopped at the three built-in kinds.
///
/// Static and given the group's name rather than reading configuration itself, so the caller that
/// already has it does not acquire a dependency to pass one string.</summary>
public static class InsiderDetailSheet
{
    public static ReportRecord Record(DeclarationSubmissionRow<InsiderDeclaration> row, string parentHoldingCompany)
    {
        var d = row.Declaration!;

        return new ReportRecord(
            row.MemberName,
            $"{row.CompanyName ?? "—"} · Q{row.Quarter} {row.Year} · submitted {d.SubmittedAtUtc.ToLocalDisplay("dd/MM/yyyy HH:mm")}",
            [
                new("Employee Name", row.MemberName),
                new("Company Name", row.CompanyName ?? "—"),
                new("Department", row.DepartmentName ?? "—"),
                new("User Has NIN?", d.HasNin ? "Yes" : "No"),
                new("Relatives Have NIN?", d.RelativesHaveNin ? "Yes" : "No"),
                new($"I / my relatives hold shares in {parentHoldingCompany}?", d.HoldsShares || d.RelativesHoldShares ? "Yes" : "No"),
                new("Own NIN", d.HasNin && d.NinNumber is { Length: > 0 } ? d.NinNumber : "—"),
                new("Shares held", d.HoldsShares ? (d.NumberOfSharesHeld ?? 0).ToString("N0") : "—"),
                new("Submitted by", d.SubmittedOnBehalfOf is { Length: > 0 }
                    ? $"{d.SubmittedByName} on behalf of {d.SubmittedOnBehalfOf}"
                    : d.SubmittedByName),
                new("Last modified", (d.ModifiedAtUtc ?? d.SubmittedAtUtc).ToLocalDisplay("dd/MM/yyyy HH:mm")),
            ],
            [
                new ReportSubTable(
                    "Individual NIN Numbers",
                    [new("Shares Held By", 28), new("Name Of Share Holder", 42), new("NIN", 30)],
                    [.. d.NinHolders.Select(h => new[]
                    {
                        RelationshipLabel(h.Relationship),
                        h.NameOfShareHolder,
                        h.NinNumber,
                    })],
                    d.RelativesHaveNin ? "Declared that relatives hold a NIN, but no holders were listed."
                                       : "Declared that no relatives hold a National Investor Number."),

                Documents(d),

                new ReportSubTable(
                    $"Relatives holding {parentHoldingCompany} shares",
                    [new("Name", 36), new("Relationship", 22), new("NIN", 24), new("Shares", 18, Numeric: true)],
                    [.. d.Relatives.Select(r => new[]
                    {
                        r.RelativeName,
                        RelationshipLabel(r.Relationship),
                        r.NinNumber ?? "—",
                        r.NumberOfShares.ToString(),
                    })],
                    d.RelativesHoldShares ? "Declared that relatives hold shares, but none were listed."
                                          : $"Declared that no relatives hold shares in {parentHoldingCompany}."),

                ShareTransactions(d, row.MemberName),
            ]);
    }

    /// <summary>What was declared bought and sold during the period, and what was held at each end
    /// of it -- the share movement table from the printed form.
    ///
    /// Sorted the way the form reads rather than by id: the declarant's lines, then the spouse's,
    /// then the relatives', and within each the opening holding, the transactions in date order,
    /// then the closing holding. A reviewer is checking a sequence, not a list of rows.</summary>
    internal static ReportSubTable ShareTransactions(InsiderDeclaration d, string declarantName) =>
        new("Share transactions and holdings",
            [new("Held by", 12), new("Name", 24), new("NIN", 16), new("Entry", 18),
             new("Date", 12), new("Shares", 9, Numeric: true), new("Rate", 9, Numeric: true)],
            [.. d.ShareEntries
                .OrderByDescending(e => e.IsSelf).ThenBy(e => e.Relationship).ThenBy(e => e.EntryKind).ThenBy(e => e.TransactionDate)
                .Select(e => new[]
                {
                    e.IsSelf ? "Self" : RelationshipLabel(e.Relationship),
                    e.IsSelf ? declarantName : e.HolderName ?? "—",
                    e.HolderNin ?? "—",
                    e.EntryKind switch
                    {
                        ShareEntryKind.OpeningBalance => "Opening holding",
                        ShareEntryKind.Purchase => "Purchase",
                        ShareEntryKind.Sale => "Sale",
                        _ => "Closing holding",
                    },
                    e.TransactionDate is { } on ? on.ToString("dd/MM/yyyy") : "—",
                    e.NumberOfShares.ToString("N0"),
                    e.Rate is { } rate ? rate.ToString("N4") : "—",
                })],
            "No share transactions or holdings were declared for this period.");

    /// <summary>What the declarant uploaded as evidence, with the number and expiry they go with.
    /// Ported from the old report, where it lived behind a per-row button and a modal; here it is a
    /// section of the person's own sheet, because that is where a reviewer is already looking.
    ///
    /// The file name rather than the word "View" is the link text. This report prints and exports,
    /// and a relative path does not resolve from a PDF or a spreadsheet -- so the cell has to say
    /// something true without the link, and the file's name does.</summary>
    internal static ReportSubTable Documents(InsiderDeclaration d)
    {
        var files = new List<(string Kind, string? Number, DateTime? Expiry, string? Path)>
        {
            ("Emirates ID", d.EmiratesIdNumber, d.EmiratesIdExpiryDate, DocumentUrl.For(d.EmiratesIdBlobId, d.EmiratesIdPath)),
            ("Passport", d.PassportNumber, d.PassportExpiryDate, DocumentUrl.For(d.PassportBlobId, d.PassportPath)),
            ("Trade Licence", d.TradeLicenceNumber, d.TradeLicenceExpiryDate, DocumentUrl.For(d.TradeLicenceBlobId, d.TradeLicencePath)),
            ("Other document", null, null, DocumentUrl.For(d.OtherDocumentBlobId, d.OtherDocumentPath)),
        };

        var filed = files.Where(f => f.Path is { Length: > 0 }).ToList();

        return new ReportSubTable(
            "Documents",
            [new("Document", 26), new("Number", 26), new("Expiry", 18), new("File", 30)],
            [.. filed.Select(f => new[]
            {
                f.Kind,
                f.Number ?? "—",
                f.Expiry?.ToString("dd/MM/yyyy") ?? "—",
                FileNameOf(f.Path!),
            })],
            "No documents were uploaded with this declaration.",
            LinkColumn: 3,
            RowLinks: [.. filed.Select(f => (string?)f.Path)]);
    }

    /// <summary>The name a person would recognise, out of a stored path like
    /// "uploads/declarations/9f2c…-emirates-id.jpg".</summary>
    internal static string FileNameOf(string path)
    {
        var name = path.Replace('\\', '/').Split('/').LastOrDefault();
        return string.IsNullOrWhiteSpace(name) ? "Open" : name;
    }

    /// <summary>"FatherInLaw" is how the enum spells it; "Father in law" is how a reader does.</summary>
    internal static string RelationshipLabel(RelativeRelationship relationship) => relationship switch
    {
        RelativeRelationship.InLaws => "In-laws",
        RelativeRelationship.FatherInLaw => "Father-in-law",
        RelativeRelationship.MotherInLaw => "Mother-in-law",
        _ => relationship.ToString(),
    };
}
