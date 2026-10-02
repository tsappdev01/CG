using CGTOOL.Web.Data;
using CGTOOL.Web.Data.Governance;
using CGTOOL.Web.Data.Reports;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.EntityFrameworkCore;

namespace CGTOOL.Web.Components.Pages.Admin;

/// <summary>The legacy RP and COI Detailed Submission Report: one sheet per declarant, carrying
/// the two grids the form produces -- the related parties declared, and the conflicts of interest.
///
/// The legacy report printed both grids' headers even when there was nothing under them, which
/// read as missing data. Here an empty grid says which answer produced it: "nothing to declare"
/// is a different finding from a section nobody filled in.</summary>
public partial class RpCoiDetailReport
{
    [Inject] private IDbContextFactory<ApplicationDbContext> DbFactory { get; set; } = default!;
    [Inject] private AuthenticationStateProvider AuthState { get; set; } = default!;

    private List<DeclarationSubmissionRow<RelatedPartyCoiDeclaration>>? _rows;
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
        _rows = await DeclarationSubmissionSource.RelatedPartyCoiAsync(db);
        _companies = await db.Companies.AsNoTracking().OrderBy(c => c.Name).ToListAsync();

        var newest = _rows.OrderByDescending(r => r.Year).ThenByDescending(r => r.Quarter).FirstOrDefault();
        if (newest is not null) { _yearFilter = newest.Year; _quarterFilter = newest.Quarter; }
    }

    private List<int> Years() => [.. (_rows ?? []).Select(r => r.Year).Distinct().OrderByDescending(y => y)];

    private List<DeclarationSubmissionRow<RelatedPartyCoiDeclaration>> Filtered()
    {
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
            Title = "RP and COI Detailed Submission Report",
            Subtitle = "One sheet per declarant.",
            PreparedBy = _preparedBy,
            Stats =
            [
                new("Declarants", rows.Count.ToString(), "submitted"),
                new("Related parties", rows.Sum(r => r.Declaration!.Companies.Count).ToString(), "declared"),
                new("Conflicts", rows.Sum(r => r.Declaration!.Conflicts.Count).ToString(), "declared"),
            ],
            FilterStatement = FilterStatement(),
            Records = [.. rows.Select(Record)],
        };
    }

    private static ReportRecord Record(DeclarationSubmissionRow<RelatedPartyCoiDeclaration> row)
    {
        var d = row.Declaration!;

        return new ReportRecord(
            row.MemberName,
            $"{row.CompanyName ?? "—"} · Q{row.Quarter} {row.Year} · submitted {d.SubmittedAtUtc.ToLocalDisplay("dd/MM/yyyy HH:mm")}",
            [
                new("Employee Name", row.MemberName),
                new("Company Name", row.CompanyName ?? "—"),
                new("Department", row.DepartmentName ?? "—"),
                new("Relatives declared", d.NothingToDeclareRelatives ? "Nothing to declare" : d.Relatives.Count.ToString()),
                new("Attested by", d.AttestationName is { Length: > 0 } ? d.AttestationName : "—"),
                new("Signed", d.SignedAtUtc is { } signed ? signed.ToLocalDisplay("dd/MM/yyyy HH:mm") : "Not signed"),
                new("Submitted by", d.SubmittedOnBehalfOf is { Length: > 0 }
                    ? $"{d.SubmittedByName} on behalf of {d.SubmittedOnBehalfOf}"
                    : d.SubmittedByName),
                new("Last modified", (d.ModifiedAtUtc ?? d.SubmittedAtUtc).ToLocalDisplay("dd/MM/yyyy HH:mm")),
            ],
            [
                new ReportSubTable(
                    "Relatives",
                    [new("Name", 60), new("Relationship", 40)],
                    [.. d.Relatives.Select(r => new[] { r.Name, RelationshipLabel(r.Relationship) })],
                    d.NothingToDeclareRelatives ? "Declared nothing to declare." : "No relatives entered."),

                new ReportSubTable(
                    "Related Parties Declaration Form",
                    [new("Legal Name", 34), new("Nature of Business", 28), new("Nature of Holding", 20), new("Declared as", 18)],
                    [.. d.Companies.Select(c => new[]
                    {
                        c.LegalCompanyName,
                        c.PrincipalBusinessActivity ?? "—",
                        c.NatureOfHolding ?? "—",
                        OwnerLabel(c.OwnerType),
                    })],
                    d.NothingToDeclareSelfOwned && d.NothingToDeclareRelativeOwned && d.NothingToDeclareBoardRoles
                        ? "Declared nothing to declare in all three company sections."
                        : "No companies entered."),

                Documents(d),

                new ReportSubTable(
                    "Conflict of Interest Declaration Form",
                    [new("Legal Name", 34), new("Nature of Business", 28), new("Nature of My Interest", 38)],
                    [.. d.Conflicts.Select(c => new[]
                    {
                        c.CompanyOrCounterpartyName,
                        c.PrincipalBusinessActivity ?? "—",
                        c.NatureOfInterest ?? c.NatureOfHolding ?? "—",
                    })],
                    d.NothingToDeclareConflicts ? "Declared no conflicts of interest." : "No conflicts entered."),
            ]);
    }

    /// <summary>The trade licences uploaded against this declarant's companies. Ported from the
    /// old report, where it sat behind a per-row button and a modal; here it is a section of the
    /// person's own sheet, which is where a reviewer already is.
    ///
    /// The file name rather than the word "View" is the link text, because this report prints and
    /// exports and a relative path resolves from neither -- the cell has to say something true
    /// without the link.</summary>
    private static ReportSubTable Documents(RelatedPartyCoiDeclaration d)
    {
        var files = d.Companies
            .SelectMany(c => c.Documents.Select(doc => (Company: c.LegalCompanyName, Doc: doc)))
            .OrderBy(x => x.Company).ThenBy(x => x.Doc.FileName)
            .ToList();

        return new ReportSubTable(
            "Documents",
            [new("Company", 38), new("File", 40), new("Uploaded", 22)],
            [.. files.Select(f => new[]
            {
                f.Company,
                f.Doc.FileName,
                f.Doc.UploadedAtUtc.ToLocalDisplay("dd/MM/yyyy"),
            })],
            d.Companies.Count == 0
                ? "No companies were declared, so there are no licences to file."
                : "No trade licences were uploaded against the declared companies.",
            LinkColumn: 1,
            RowLinks: [.. files.Select(f => (string?)f.Doc.FilePath)]);
    }

    /// <summary>Which section of the form the company came from — the declarant's own, a
    /// relative's, or a board or executive role. Without it the three sections collapse into one
    /// list and a reviewer cannot tell whose interest is whose.</summary>
    private static string OwnerLabel(CoiCompanyOwnerType type) => type switch
    {
        CoiCompanyOwnerType.Self => "Own interest",
        CoiCompanyOwnerType.Relative => "Relative's interest",
        CoiCompanyOwnerType.BoardOrExecutiveRole => "Board / executive role",
        _ => type.ToString(),
    };

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
