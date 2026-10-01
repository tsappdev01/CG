using CGTOOL.Web.Data;
using CGTOOL.Web.Data.Governance;
using CGTOOL.Web.Data.Reports;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.EntityFrameworkCore;

namespace CGTOOL.Web.Components.Pages.Admin;

/// <summary>The legacy NIN Report of Submissions. One row per declarant, carrying the National
/// Investor Number they declared and the shares held against it -- the list a dealing check is run
/// against, which is why it shows only people who actually submitted something to check.</summary>
public partial class InsiderNinReport
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
    private string _ninFilter = string.Empty;

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
        var rows = (_rows ?? []).AsEnumerable();

        if (_yearFilter > 0) rows = rows.Where(r => r.Year == _yearFilter);
        if (_quarterFilter > 0) rows = rows.Where(r => r.Quarter == _quarterFilter);
        if (_companyFilter > 0) rows = rows.Where(r => r.CompanyId == _companyFilter);

        rows = _ninFilter switch
        {
            "yes" => rows.Where(r => r.Declaration is { HasNin: true }),
            "no" => rows.Where(r => r.Declaration is null or { HasNin: false }),
            _ => rows,
        };

        if (_search.Trim() is { Length: > 0 } term)
        {
            rows = rows.Where(r =>
                r.MemberName.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                (r.Declaration?.NinNumber ?? string.Empty).Contains(term, StringComparison.OrdinalIgnoreCase));
        }

        return [.. rows];
    }

    private ReportDocument Build()
    {
        var rows = Filtered();

        return new ReportDocument
        {
            Title = "NIN Report of Submissions",
            Subtitle = "Each declarant's National Investor Number and declared DI shareholding.",
            PreparedBy = _preparedBy,
            Stats =
            [
                new("Declarants", rows.Count.ToString(), "notified"),
                new("Holding a NIN", rows.Count(r => r.Declaration is { HasNin: true }).ToString(), "declared"),
                new("Holding DI shares", rows.Count(r => r.Declaration is { HoldsShares: true }).ToString(), "declared"),
                new("Shares declared", rows.Sum(r => r.Declaration?.NumberOfSharesHeld ?? 0).ToString("N0"), "in total"),
            ],
            FilterStatement = FilterStatement(),
            Columns =
            [
                new("S. No", 5),
                new("User", 22),
                new("Department", 14),
                new("Company Name", 20),
                new("Submitted", 9),
                new("Has NIN", 8),
                new("NIN", 13),
                new("No of Shares", 9, Numeric: true),
            ],
            Rows =
            [
                .. rows.Select((r, i) => new[]
                {
                    (i + 1).ToString(),
                    r.MemberName,
                    r.DepartmentName ?? "—",
                    r.CompanyName ?? "—",
                    r.Submitted ? "True" : "False",
                    r.Declaration is null ? "—" : r.Declaration.HasNin ? "Yes" : "No",
                    // A declarant who answered "no NIN" has nothing to show here, and a dash is
                    // the honest rendering of that -- not an empty cell, which reads as missing data.
                    r.Declaration is { HasNin: true, NinNumber: { Length: > 0 } nin } ? nin : "—",
                    (r.Declaration?.NumberOfSharesHeld ?? 0).ToString(),
                })
            ],
        };
    }

    private string FilterStatement()
    {
        var parts = new List<string>
        {
            _yearFilter > 0 ? _yearFilter.ToString() : "All years",
            _quarterFilter > 0 ? $"Q{_quarterFilter}" : "All quarters",
            _companyFilter > 0 ? _companies?.FirstOrDefault(c => c.Id == _companyFilter)?.Name ?? "Unknown entity" : "All entities",
            _ninFilter switch { "yes" => "Holds a NIN", "no" => "No NIN", _ => "With and without a NIN" },
        };
        if (_search.Trim() is { Length: > 0 } term) parts.Add($"Search “{term}”");
        return string.Join(" · ", parts);
    }
}
