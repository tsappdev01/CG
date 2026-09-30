using CGTOOL.Web.Data;
using CGTOOL.Web.Data.Governance;
using CGTOOL.Web.Data.Reports;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.EntityFrameworkCore;

namespace CGTOOL.Web.Components.Pages.Admin;

/// <summary>Who can reach what. The report the old CG app had no equivalent of in CGTOOL, and the
/// one an access review is actually run from -- so it answers "who has this" as a figure at the
/// top, not only as a column the reader has to count down.</summary>
public partial class UsersReport
{
    [Inject] private IDbContextFactory<ApplicationDbContext> DbFactory { get; set; } = default!;
    [Inject] private AuthenticationStateProvider AuthState { get; set; } = default!;

    private List<Member>? _members;
    private List<Company>? _companies;
    private string _preparedBy = "unknown";

    private string _search = string.Empty;
    private int _companyFilter;
    private string _accessFilter = string.Empty;

    /// <summary>Active only by default. An access review asks who can reach the system today; a
    /// list padded with leavers who cannot sign in reads as a worse finding than it is.</summary>
    private string _statusFilter = "active";

    protected override async Task OnInitializedAsync()
    {
        var state = await AuthState.GetAuthenticationStateAsync();
        _preparedBy = state.User.Identity?.Name ?? "unknown";

        await using var db = await DbFactory.CreateDbContextAsync();
        _members = await db.Members
            .AsNoTracking()
            .Include(m => m.Company)
            .Include(m => m.Department)
            .OrderBy(m => m.Company!.Name).ThenBy(m => m.FullName)
            .ToListAsync();

        _companies = await db.Companies.AsNoTracking().OrderBy(c => c.Name).ToListAsync();
    }

    private IEnumerable<Member> Filtered()
    {
        var members = (_members ?? []).AsEnumerable();

        if (_statusFilter == "active") members = members.Where(m => m.Active);
        else if (_statusFilter == "inactive") members = members.Where(m => !m.Active);

        if (_companyFilter > 0) members = members.Where(m => m.CompanyId == _companyFilter);

        members = _accessFilter switch
        {
            "coi" => members.Where(m => m.ConflictOfInterestAccess),
            "insider" => members.Where(m => m.InsiderTradingAccess),
            "register" => members.Where(m => m.RelatedPartyRegisterAccess),
            "rptm" => members.Where(m => m.RelatedPartyTransactionAccess),
            "none" => members.Where(m => !m.ConflictOfInterestAccess && !m.InsiderTradingAccess
                                      && !m.RelatedPartyRegisterAccess && !m.RelatedPartyTransactionAccess),
            _ => members,
        };

        if (_search.Trim() is { Length: > 0 } term)
        {
            members = members.Where(m =>
                m.FullName.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                (m.Email ?? string.Empty).Contains(term, StringComparison.OrdinalIgnoreCase));
        }

        return members;
    }

    private ReportDocument Build()
    {
        var rows = Filtered().ToList();

        return new ReportDocument
        {
            Title = "Users Report",
            Subtitle = "Everyone with a CGTOOL account, and which parts of the system each of them can reach.",
            PreparedBy = _preparedBy,
            Stats =
            [
                new("Users", rows.Count.ToString(), $"of {_members?.Count ?? 0}"),
                new("RP & COI Declaration", rows.Count(m => m.ConflictOfInterestAccess).ToString(), "with access"),
                new("Insider Declaration", rows.Count(m => m.InsiderTradingAccess).ToString(), "with access"),
                new("Related Party Register", rows.Count(m => m.RelatedPartyRegisterAccess).ToString(), "with access"),
                new("RP Transaction Monitoring", rows.Count(m => m.RelatedPartyTransactionAccess).ToString(), "with access"),
            ],
            FilterStatement = FilterStatement(),
            Columns =
            [
                new("S. No", 4),
                new("Name", 15),
                new("Email", 19),
                new("Company", 14),
                new("Department", 11),
                new("RP & COI Declaration Access", 9),
                new("Insider Declaration Access", 9),
                new("Related Party Register Access", 9),
                new("RP Transaction Monitoring Access", 10),
            ],
            Rows =
            [
                .. rows.Select((m, i) => new[]
                {
                    (i + 1).ToString(),
                    m.FullName,
                    m.Email ?? "—",
                    m.Company?.Name ?? "—",
                    m.Department?.Name ?? "—",
                    Yes(m.ConflictOfInterestAccess),
                    Yes(m.InsiderTradingAccess),
                    Yes(m.RelatedPartyRegisterAccess),
                    Yes(m.RelatedPartyTransactionAccess),
                })
            ],
        };
    }

    /// <summary>"True"/"False" as the legacy report spelled them, rather than a tick and a cross.
    /// This one is read in Excel as often as on paper, where a glyph does not sort or filter.</summary>
    private static string Yes(bool value) => value ? "True" : "False";

    private string FilterStatement()
    {
        var parts = new List<string>
        {
            _companyFilter > 0
                ? _companies?.FirstOrDefault(c => c.Id == _companyFilter)?.Name ?? "Unknown entity"
                : "All entities",
            _accessFilter switch
            {
                "coi" => "RP & COI Declaration access",
                "insider" => "Insider Declaration access",
                "register" => "Related Party Register access",
                "rptm" => "RP Transaction Monitoring access",
                "none" => "No access at all",
                _ => "Any access",
            },
            _statusFilter switch
            {
                "active" => "Active only",
                "inactive" => "Inactive only",
                _ => "Active and inactive",
            },
        };

        if (_search.Trim() is { Length: > 0 } term) parts.Add($"Search “{term}”");
        return string.Join(" · ", parts);
    }
}
