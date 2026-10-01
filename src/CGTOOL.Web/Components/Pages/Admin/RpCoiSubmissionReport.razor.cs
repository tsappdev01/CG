using CGTOOL.Web.Data;
using CGTOOL.Web.Data.Governance;
using CGTOOL.Web.Data.Reports;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.EntityFrameworkCore;

namespace CGTOOL.Web.Components.Pages.Admin;

/// <summary>The legacy RP and COI Submission Report, rebuilt on CGTOOL's report chrome. The
/// Insider return's twin, and failing the same way in the legacy application: both printed an
/// expression error in place of every ModifiedOn and ModifiedBy value.
///
/// Deliberately the same shape as the Insider one, because Compliance reads the two side by side
/// for the same quarter.</summary>
public partial class RpCoiSubmissionReport
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
    private string _statusFilter = string.Empty;

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
        var rows = (_rows ?? []).AsEnumerable();

        if (_yearFilter > 0) rows = rows.Where(r => r.Year == _yearFilter);
        if (_quarterFilter > 0) rows = rows.Where(r => r.Quarter == _quarterFilter);
        if (_companyFilter > 0) rows = rows.Where(r => r.CompanyId == _companyFilter);

        rows = _statusFilter switch
        {
            "submitted" => rows.Where(r => r.Submitted),
            "pending" => rows.Where(r => !r.Submitted),
            _ => rows,
        };

        if (_search.Trim() is { Length: > 0 } term)
            rows = rows.Where(r => r.MemberName.Contains(term, StringComparison.OrdinalIgnoreCase));

        return [.. rows];
    }

    private ReportDocument Build()
    {
        var rows = Filtered();
        var submitted = rows.Count(r => r.Submitted);

        return new ReportDocument
        {
            Title = "RP and COI Submission Report",
            Subtitle = "Everyone notified of a Related Party & Conflict of Interest declaration for the period, and whether they have completed it.",
            PreparedBy = _preparedBy,
            Stats =
            [
                new("Total Submissions", submitted.ToString(), "completed"),
                new("Non Submissions", (rows.Count - submitted).ToString(), "outstanding"),
                new("Total Users", rows.Count.ToString(), "notified"),
            ],
            FilterStatement = FilterStatement(),
            Columns =
            [
                new("S. No", 5),
                new("User", 20),
                new("Department", 13),
                new("Company Name", 18),
                new("Submitted", 8),
                new("Declared", 14),
                new("Modified On", 12),
                new("Modified By", 10),
            ],
            Rows =
            [
                .. rows.Select((r, i) => new[]
                {
                    (i + 1).ToString(),
                    r.MemberName,
                    r.DepartmentName ?? "—",
                    r.CompanyName ?? "—",
                    r.Submitted ? "Yes" : "No",
                    Declared(r.Declaration),
                    ModifiedOn(r.Declaration),
                    ModifiedBy(r.Declaration),
                })
            ],
        };
    }

    /// <summary>What they actually declared, in one cell. Counting is what a reviewer does with
    /// this column, and "none" is a different answer from a dash: one person declared nothing,
    /// the other has not declared at all.</summary>
    private static string Declared(RelatedPartyCoiDeclaration? d)
    {
        if (d is null) return "—";

        var parts = new List<string>();
        if (d.Relatives.Count > 0) parts.Add($"{d.Relatives.Count} relative{(d.Relatives.Count == 1 ? "" : "s")}");
        if (d.Companies.Count > 0) parts.Add($"{d.Companies.Count} compan{(d.Companies.Count == 1 ? "y" : "ies")}");
        if (d.Conflicts.Count > 0) parts.Add($"{d.Conflicts.Count} conflict{(d.Conflicts.Count == 1 ? "" : "s")}");

        return parts.Count == 0 ? "Nothing to declare" : string.Join(", ", parts);
    }

    private static string ModifiedOn(RelatedPartyCoiDeclaration? d) =>
        d is null ? "—" : (d.ModifiedAtUtc ?? d.SubmittedAtUtc).ToLocalDisplay("dd/MM/yyyy HH:mm");

    /// <summary>CGTOOL records who pressed submit rather than a separate "last modified by", and
    /// where someone filed for another person that is the person who filed. A reviewer needs to
    /// see that, so it is marked rather than hidden.</summary>
    private static string ModifiedBy(RelatedPartyCoiDeclaration? d)
    {
        if (d is null || string.IsNullOrWhiteSpace(d.SubmittedByName)) return "—";
        return d.SubmittedOnBehalfOf is { Length: > 0 } ? $"{d.SubmittedByName} (on behalf)" : d.SubmittedByName;
    }

    private string FilterStatement()
    {
        var parts = new List<string>
        {
            _yearFilter > 0 ? _yearFilter.ToString() : "All years",
            _quarterFilter > 0 ? $"Q{_quarterFilter}" : "All quarters",
            _companyFilter > 0 ? _companies?.FirstOrDefault(c => c.Id == _companyFilter)?.Name ?? "Unknown entity" : "All entities",
            _statusFilter switch { "submitted" => "Submitted only", "pending" => "Not submitted only", _ => "Submitted and not" },
        };
        if (_search.Trim() is { Length: > 0 } term) parts.Add($"Search “{term}”");
        return string.Join(" · ", parts);
    }
}
