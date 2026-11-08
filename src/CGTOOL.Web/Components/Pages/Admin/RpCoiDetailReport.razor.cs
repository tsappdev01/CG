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
    [Inject] private IReportAudience Audience { get; set; } = default!;
    [Inject] private IDocumentStore DocumentStore { get; set; } = default!;

    private List<DeclarationSubmissionRow<RelatedPartyCoiDeclaration>>? _rows;

    /// <summary>Signature images by declaration, read once when the rows load.
    ///
    /// They are fetched here rather than where the report is assembled because Build() runs inside
    /// the render and cannot await, and because the alternative -- reading them during assembly --
    /// would re-read every signature on every re-render, including the ones a filter has just
    /// excluded. A signature is a few kilobytes and there is one per declarant.</summary>
    private readonly Dictionary<int, byte[]> _signatureImages = [];
    private List<Company>? _companies;
    private string _preparedBy = "unknown";

    private string _search = string.Empty;
    private int _yearFilter;
    private int _quarterFilter;
    private int _companyFilter;

    /// <summary>Set by the "View details" link on the RP &amp; COI submission report, the same way
    /// InsiderDetailReport takes them: arriving here lands on the person and the period that was
    /// being looked at. They are ordinary filters once set -- shown in the boxes, free to be
    /// changed or cleared.</summary>
    [SupplyParameterFromQuery(Name = "name")] private string? NameQuery { get; set; }

    /// <summary>Which declarant the link was for. Selected on in preference to the name, because a
    /// name is not an identity -- the search matches on "contains", so a link for "Nayyar Ali Khan"
    /// also opened "Nayyar Ali Khan BOD", and showed that one first. Cleared the moment the search
    /// box is edited: from then on the person is looking for something, not at someone.</summary>
    [SupplyParameterFromQuery(Name = "member")] private int? MemberQuery { get; set; }

    private int? _memberFilter;

    private void OnSearchChanged() => _memberFilter = null;

    [SupplyParameterFromQuery(Name = "year")] private int? YearQuery { get; set; }

    [SupplyParameterFromQuery(Name = "quarter")] private int? QuarterQuery { get; set; }

    protected override async Task OnInitializedAsync()
    {
        var state = await AuthState.GetAuthenticationStateAsync();
        _preparedBy = state.User.Identity?.Name ?? "unknown";

        await using var db = await DbFactory.CreateDbContextAsync();
        _rows = (await DeclarationSubmissionSource.RelatedPartyCoiAsync(db))
            .LimitTo(await Audience.VisibleMemberIdsAsync(state.User), r => r.MemberId);
        await LoadSignatureImagesAsync();
        _companies = await db.Companies.AsNoTracking().OrderBy(c => c.Name).ToListAsync();

        var newest = _rows.OrderByDescending(r => r.Year).ThenByDescending(r => r.Quarter).FirstOrDefault();
        if (newest is not null) { _yearFilter = newest.Year; _quarterFilter = newest.Quarter; }

        // After the default, so a link that names a period wins over the newest one.
        if (!string.IsNullOrWhiteSpace(NameQuery)) _search = NameQuery.Trim();
        if (MemberQuery is > 0) _memberFilter = MemberQuery;
        if (YearQuery is > 0) _yearFilter = YearQuery.Value;
        if (QuarterQuery is > 0 and <= 4) _quarterFilter = QuarterQuery.Value;
    }

    private List<int> Years() => [.. (_rows ?? []).Select(r => r.Year).Distinct().OrderByDescending(y => y)];

    private List<DeclarationSubmissionRow<RelatedPartyCoiDeclaration>> Filtered()
    {
        var rows = (_rows ?? []).Where(r => r.Submitted);

        if (_yearFilter > 0) rows = rows.Where(r => r.Year == _yearFilter);
        if (_quarterFilter > 0) rows = rows.Where(r => r.Quarter == _quarterFilter);
        if (_companyFilter > 0) rows = rows.Where(r => r.CompanyId == _companyFilter);
        if (_memberFilter is { } memberId)
        {
            rows = rows.Where(r => r.MemberId == memberId);
        }
        else if (_search.Trim() is { Length: > 0 } term)
        {
            rows = rows.Where(r => r.MemberName.Contains(term, StringComparison.OrdinalIgnoreCase));
        }

        return [.. rows];
    }

    /// <summary>Reads the signature image for each declaration that has one.</summary>
    private async Task LoadSignatureImagesAsync()
    {
        _signatureImages.Clear();

        foreach (var row in _rows ?? [])
        {
            if (row.Declaration?.SignatureBlobId is not int blobId) continue;

            await using var document = await DocumentStore.OpenAsync(blobId);
            if (document is null) continue;

            using var buffer = new MemoryStream();
            await document.Stream.CopyToAsync(buffer);
            _signatureImages[row.Declaration.Id] = buffer.ToArray();
        }
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

    private ReportRecord Record(DeclarationSubmissionRow<RelatedPartyCoiDeclaration> row)
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
            ],
            // The mark itself, under the sheet it signs. "Signed 07/11/2026 23:30" in the facts
            // says a signature exists; a reviewer checking a declaration wants to see it.
            DocumentUrl.Exists(d.SignatureBlobId, d.SignaturePath)
                ? new ReportSignature(
                    _signatureImages.GetValueOrDefault(d.Id),
                    d.SignaturePath,
                    d.AttestationName,
                    d.SignedAtUtc)
                : null);
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

    private static string RelationshipLabel(RelativeRelationship relationship) =>
        RelativeRelationships.Label(relationship);

    private string FilterStatement()
    {
        var parts = new List<string>
        {
            _yearFilter > 0 ? _yearFilter.ToString() : "All years",
            _quarterFilter > 0 ? $"Q{_quarterFilter}" : "All quarters",
            _companyFilter > 0 ? _companies?.FirstOrDefault(c => c.Id == _companyFilter)?.Name ?? "Unknown entity" : "All entities",
            "Submitted only",
        };
        // Says which it is. "Name contains" against a sheet selected by member would be a filter
        // statement describing a filter that is not the one applied.
        if (_memberFilter is not null && _search.Trim() is { Length: > 0 } who) parts.Add($"Declarant “{who}”");
        else if (_search.Trim() is { Length: > 0 } term) parts.Add($"Name contains “{term}”");
        return string.Join(" · ", parts);
    }
}
