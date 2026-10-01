using CGTOOL.Web.Data;
using CGTOOL.Web.Data.Governance;
using CGTOOL.Web.Data.Reports;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.EntityFrameworkCore;

namespace CGTOOL.Web.Components.Pages.Admin;

/// <summary>The legacy Insider Submission Report, rebuilt on CGTOOL's own report chrome.
///
/// It is deliberately the same shape as the one it replaces -- the same three figures at the top,
/// the same seven columns -- because Compliance reads it against prior quarters and a report that
/// reorganises itself cannot be compared with the ones already filed. The richer screen at
/// /reports/insider-declarations stays as it is: this one is the printable submission return.</summary>
public partial class InsiderSubmissionReport
{
    [Inject] private IDbContextFactory<ApplicationDbContext> DbFactory { get; set; } = default!;
    [Inject] private AuthenticationStateProvider AuthState { get; set; } = default!;

    /// <summary>One row per member who was actually sent a notification for a cycle. Declaration is
    /// null until they submit, which is how the report shows everyone who was required to declare
    /// rather than only those who have.</summary>
    private sealed class Row
    {
        public required string MemberName { get; init; }
        public int? CompanyId { get; init; }
        public string? CompanyName { get; init; }
        public string? DepartmentName { get; init; }
        public required int Year { get; init; }
        public required int Quarter { get; init; }
        public required bool Submitted { get; init; }
        public InsiderDeclaration? Declaration { get; init; }
    }

    private List<Row>? _rows;
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

        var recipients = await db.DeclarationCycleRunRecipients
            .AsNoTracking()
            .Include(r => r.DeclarationCycleRun)
            .Where(r => r.DeclarationCycleRun!.Type == DeclarationCycleType.InsiderTrading)
            .ToListAsync();

        var members = await db.Members
            .AsNoTracking()
            .Include(m => m.Company)
            .Include(m => m.Department)
            .ToDictionaryAsync(m => m.Id);

        var declarations = await db.InsiderDeclarations.AsNoTracking().ToListAsync();
        var byMemberAndRun = declarations.ToDictionary(d => (d.MemberId, d.DeclarationCycleRunId));

        _rows = recipients.Select(r =>
        {
            var run = r.DeclarationCycleRun!;
            members.TryGetValue(r.MemberId, out var member);
            byMemberAndRun.TryGetValue((r.MemberId, run.Id), out var declaration);

            return new Row
            {
                MemberName = r.MemberName,
                CompanyId = member?.CompanyId,
                // The name as the recipient list recorded it is the fallback: a member deleted
                // since the notification went out still belongs on the return for that period.
                CompanyName = member?.Company?.Name ?? r.CompanyName,
                DepartmentName = member?.Department?.Name,
                // The period is chosen by the administrator when the cycle is set up, not derived
                // from when the notification happened to be sent.
                Year = run.PeriodYear,
                Quarter = run.PeriodQuarter,
                // A saved draft is not a completed declaration: it is still pending until the
                // member submits it.
                Submitted = declaration is { IsDraft: false },
                Declaration = declaration,
            };
        })
        .OrderBy(r => r.CompanyName).ThenBy(r => r.MemberName)
        .ToList();

        _companies = await db.Companies.AsNoTracking().OrderBy(c => c.Name).ToListAsync();

        // Open on the newest period rather than on everything at once, which is what the report is
        // almost always wanted for and what the legacy one did.
        var newest = _rows.OrderByDescending(r => r.Year).ThenByDescending(r => r.Quarter).FirstOrDefault();
        if (newest is not null)
        {
            _yearFilter = newest.Year;
            _quarterFilter = newest.Quarter;
        }
    }

    private List<int> AvailableYears() =>
        [.. (_rows ?? []).Select(r => r.Year).Distinct().OrderByDescending(y => y)];

    private List<Row> Filtered()
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
            Title = "Insider Declaration Submission Report",
            Subtitle = "Everyone notified of an Insider Trading declaration for the period, and whether they have completed it.",
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
                new("User", 22),
                new("Department", 14),
                new("Company Name", 20),
                new("Submitted", 9),
                new("Modified On", 15),
                new("Modified By", 15),
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
                    ModifiedOn(r),
                    ModifiedBy(r),
                })
            ],
        };
    }

    /// <summary>When the declaration was last touched: the edit if there has been one, otherwise
    /// the submission. The legacy report had a ModifiedOn column that printed an expression error
    /// on every row because its dataset no longer returned the field; this is the value it was
    /// meant to show.</summary>
    private static string ModifiedOn(Row r) =>
        r.Declaration is { } d
            ? (d.ModifiedAtUtc ?? d.SubmittedAtUtc).ToLocalDisplay("dd/MM/yyyy HH:mm")
            : "—";

    /// <summary>Who last touched it. CGTOOL records the person who actually pressed submit --
    /// which is the impersonator where one filed on the member's behalf -- and does not keep a
    /// separate "last modified by", so that is what this shows. Where it differs from the member
    /// the row is about, it is shown as filed on their behalf, because a reviewer needs to see
    /// that someone else answered for them.</summary>
    private static string ModifiedBy(Row r)
    {
        if (r.Declaration is not { } d) return "—";
        if (string.IsNullOrWhiteSpace(d.SubmittedByName)) return "—";

        return d.SubmittedOnBehalfOf is { Length: > 0 }
            ? $"{d.SubmittedByName} (on behalf)"
            : d.SubmittedByName;
    }

    private string FilterStatement()
    {
        var parts = new List<string>
        {
            _yearFilter > 0 ? _yearFilter.ToString() : "All years",
            _quarterFilter > 0 ? $"Q{_quarterFilter}" : "All quarters",
            _companyFilter > 0
                ? _companies?.FirstOrDefault(c => c.Id == _companyFilter)?.Name ?? "Unknown entity"
                : "All entities",
            _statusFilter switch
            {
                "submitted" => "Submitted only",
                "pending" => "Not submitted only",
                _ => "Submitted and not",
            },
        };

        if (_search.Trim() is { Length: > 0 } term) parts.Add($"Search “{term}”");
        return string.Join(" · ", parts);
    }
}
