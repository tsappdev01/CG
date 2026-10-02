using CGTOOL.Web.Data;
using CGTOOL.Web.Data.Governance;
using CGTOOL.Web.Data.Reports;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.EntityFrameworkCore;

namespace CGTOOL.Web.Components.Pages.Admin;

/// <summary>The legacy Insider Submission Detail Report: one sheet per declarant rather than one
/// row, because the answers do not fit a row -- a person can hold a NIN, hold shares, and have any
/// number of relatives who hold either.
///
/// Only submitted declarations appear. The legacy report ran to 304 sheets and had a raw "%" box
/// for a name filter; this one filters by name, period and entity, and says on the sheet what it
/// was filtered to.</summary>
public partial class InsiderDetailReport
{
    [Inject] private IDbContextFactory<ApplicationDbContext> DbFactory { get; set; } = default!;
    [Inject] private AuthenticationStateProvider AuthState { get; set; } = default!;

    private List<DeclarationSubmissionRow<InsiderDeclaration>>? _rows;
    private List<Company>? _companies;
    private string _preparedBy = "unknown";

    private string _search = string.Empty;
    private int _yearFilter;
    private int _quarterFilter;
    private int _companyFilter;

    protected override async Task OnInitializedAsync()
    {
        var state = await AuthState.GetAuthenticationStateAsync();
        _preparedBy = state.User.Identity?.Name ?? "unknown";

        await using var db = await DbFactory.CreateDbContextAsync();
        _rows = await DeclarationSubmissionSource.InsiderAsync(db);
        _companies = await db.Companies.AsNoTracking().OrderBy(c => c.Name).ToListAsync();

        var newest = _rows.OrderByDescending(r => r.Year).ThenByDescending(r => r.Quarter).FirstOrDefault();
        if (newest is not null) { _yearFilter = newest.Year; _quarterFilter = newest.Quarter; }
    }

    private List<int> Years() => [.. (_rows ?? []).Select(r => r.Year).Distinct().OrderByDescending(y => y)];

    private List<DeclarationSubmissionRow<InsiderDeclaration>> Filtered()
    {
        // A detail sheet of someone who did not declare would be a page of dashes. Who has not
        // declared is the submission report's question, not this one's.
        var rows = (_rows ?? []).Where(r => r.Submitted);

        if (_yearFilter > 0) rows = rows.Where(r => r.Year == _yearFilter);
        if (_quarterFilter > 0) rows = rows.Where(r => r.Quarter == _quarterFilter);
        if (_companyFilter > 0) rows = rows.Where(r => r.CompanyId == _companyFilter);
        if (_search.Trim() is { Length: > 0 } term)
            rows = rows.Where(r => r.MemberName.Contains(term, StringComparison.OrdinalIgnoreCase));

        return [.. rows];
    }

    private ReportDocument Build()
    {
        var rows = Filtered();

        return new ReportDocument
        {
            Title = "Insider Declaration Detailed Submission Report",
            Subtitle = "One sheet per declarant.",
            PreparedBy = _preparedBy,
            Stats =
            [
                new("Declarants", rows.Count.ToString(), "submitted"),
                new("Holding a NIN", rows.Count(r => r.Declaration is { HasNin: true }).ToString(), ""),
                new("Relatives with a NIN", rows.Count(r => r.Declaration is { RelativesHaveNin: true }).ToString(), "declarants"),
            ],
            FilterStatement = FilterStatement(),
            Records = [.. rows.Select(Record)],
        };
    }

    private static ReportRecord Record(DeclarationSubmissionRow<InsiderDeclaration> row)
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
                new("I / my relatives hold shares in DI PJSC?", d.HoldsShares || d.RelativesHoldShares ? "Yes" : "No"),
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
                    "Relatives holding DI shares",
                    [new("Name", 36), new("Relationship", 22), new("NIN", 24), new("Shares", 18, Numeric: true)],
                    [.. d.Relatives.Select(r => new[]
                    {
                        r.RelativeName,
                        RelationshipLabel(r.Relationship),
                        r.NinNumber ?? "—",
                        r.NumberOfShares.ToString(),
                    })],
                    d.RelativesHoldShares ? "Declared that relatives hold shares, but none were listed."
                                          : "Declared that no relatives hold shares in DI PJSC."),
            ]);
    }

    /// <summary>What the declarant uploaded as evidence, with the number and expiry they go with.
    /// Ported from the old report, where it lived behind a per-row button and a modal; here it is a
    /// section of the person's own sheet, because that is where a reviewer is already looking.
    ///
    /// The file name rather than the word "View" is the link text. This report prints and exports,
    /// and a relative path does not resolve from a PDF or a spreadsheet -- so the cell has to say
    /// something true without the link, and the file's name does.</summary>
    private static ReportSubTable Documents(InsiderDeclaration d)
    {
        var files = new List<(string Kind, string? Number, DateTime? Expiry, string? Path)>
        {
            ("Emirates ID", d.EmiratesIdNumber, d.EmiratesIdExpiryDate, d.EmiratesIdPath),
            ("Passport", d.PassportNumber, d.PassportExpiryDate, d.PassportPath),
            ("Trade Licence", d.TradeLicenceNumber, d.TradeLicenceExpiryDate, d.TradeLicencePath),
            ("Other document", null, null, d.OtherDocumentPath),
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
    private static string FileNameOf(string path)
    {
        var name = path.Replace('\\', '/').Split('/').LastOrDefault();
        return string.IsNullOrWhiteSpace(name) ? "Open" : name;
    }

    /// <summary>"FatherInLaw" is how the enum spells it; "Father in law" is how a reader does.</summary>
    private static string RelationshipLabel(RelativeRelationship relationship) => relationship switch
    {
        RelativeRelationship.InLaws => "In-laws",
        RelativeRelationship.FatherInLaw => "Father-in-law",
        RelativeRelationship.MotherInLaw => "Mother-in-law",
        _ => relationship.ToString(),
    };

    private string FilterStatement()
    {
        var parts = new List<string>
        {
            _yearFilter > 0 ? _yearFilter.ToString() : "All years",
            _quarterFilter > 0 ? $"Q{_quarterFilter}" : "All quarters",
            _companyFilter > 0 ? _companies?.FirstOrDefault(c => c.Id == _companyFilter)?.Name ?? "Unknown entity" : "All entities",
            "Submitted only",
        };
        if (_search.Trim() is { Length: > 0 } term) parts.Add($"Name contains “{term}”");
        return string.Join(" · ", parts);
    }
}
