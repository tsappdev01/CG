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

    [Inject] private Branding Branding { get; set; } = default!;

    private List<DeclarationSubmissionRow<InsiderDeclaration>>? _rows;
    private List<Company>? _companies;
    private string _preparedBy = "unknown";

    private string _search = string.Empty;
    private int _yearFilter;
    private int _quarterFilter;
    private int _companyFilter;

    /// <summary>Set by the "View details" link on the submission report, so arriving here lands on
    /// the person and the period that was being looked at rather than on everybody and the newest
    /// quarter. They are ordinary filters once set: the boxes show them and they can be changed or
    /// cleared, because a page that cannot be steered after it opens is a dead end.</summary>
    [SupplyParameterFromQuery(Name = "name")] private string? NameQuery { get; set; }

    [SupplyParameterFromQuery(Name = "year")] private int? YearQuery { get; set; }

    [SupplyParameterFromQuery(Name = "quarter")] private int? QuarterQuery { get; set; }

    protected override async Task OnInitializedAsync()
    {
        var state = await AuthState.GetAuthenticationStateAsync();
        _preparedBy = state.User.Identity?.Name ?? "unknown";

        await using var db = await DbFactory.CreateDbContextAsync();
        _rows = await DeclarationSubmissionSource.InsiderAsync(db);
        _companies = await db.Companies.AsNoTracking().OrderBy(c => c.Name).ToListAsync();

        var newest = _rows.OrderByDescending(r => r.Year).ThenByDescending(r => r.Quarter).FirstOrDefault();
        if (newest is not null) { _yearFilter = newest.Year; _quarterFilter = newest.Quarter; }

        // After the default, so a link that names a period wins over the newest one.
        if (!string.IsNullOrWhiteSpace(NameQuery)) _search = NameQuery.Trim();
        if (YearQuery is > 0) _yearFilter = YearQuery.Value;
        if (QuarterQuery is > 0 and <= 4) _quarterFilter = QuarterQuery.Value;
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
            Records = [.. rows.Select(r => InsiderDetailSheet.Record(r, Branding.ParentHoldingCompany))],
        };
    }

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
