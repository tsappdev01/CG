using Microsoft.EntityFrameworkCore;
using Microsoft.JSInterop;
using CGTOOL.Web.Data;
using CGTOOL.Web.Data.Governance;

namespace CGTOOL.Web.Components.Pages.Admin;

public partial class RelatedPartyCoiDeclarationReport
{
    /// <summary>One row per member who was actually sent a Related Party &amp; COI notification for a
    /// given cycle -- Declaration is non-null only once they've submitted, so the report can show
    /// everyone who was required to declare, not just those who already have. Same shape as
    /// InsiderDeclarationReport's SubmissionRow.</summary>
    private class SubmissionRow
    {
        public required int MemberId { get; init; }
        public required string MemberName { get; init; }
        public int? EntityCompanyId { get; init; }
        public string? EntityShortCode { get; init; }
        public string? DepartmentName { get; init; }
        public required int Year { get; init; }
        public required int Quarter { get; init; }
        public required bool Submitted { get; init; }
        public RelatedPartyCoiDeclaration? Declaration { get; init; }
    }

    private List<SubmissionRow>? _rows;
    private List<Company>? _companies;
    private string _search = string.Empty;
    private int _yearFilter;
    private int _quarterFilter;
    private int _companyFilter;
    private string _statusFilter = string.Empty;
    private string _sortColumn = "Submitted";
    private bool _sortAscending = false;
    private bool _showPrintPreview;
    private SubmissionRow? _printSingleRecord;
    private SubmissionRow? _viewDocumentsRecord;
    private bool _showColumnPicker;

    private static readonly (string Key, string Label)[] PrintColumns =
    [
        ("Member", "Member"),
        ("Entity", "Entity"),
        ("Department", "Department"),
        ("Quarter", "Quarter"),
        ("Submission", "Submission"),
        ("Relatives", "Relatives"),
        ("Companies", "Companies"),
        ("Conflicts", "Conflicts of Interest"),
        ("Submitted", "Submitted"),
        ("OnBehalfOf", "On behalf of"),
    ];

    private readonly HashSet<string> _selectedColumns = PrintColumns.Select(c => c.Key).ToHashSet();

    private bool IsColumnSelected(string key) => _selectedColumns.Contains(key);

    private void ToggleColumn(string key, bool selected)
    {
        if (selected) _selectedColumns.Add(key);
        else _selectedColumns.Remove(key);
    }

    private async Task PrintAsync() => await JS.InvokeVoidAsync("print");

    private List<SubmissionRow> PrintRows() => _printSingleRecord is not null ? [_printSingleRecord] : FilteredRows();

    private static bool HasAnyDocument(SubmissionRow r) => r.Declaration is not null && r.Declaration.Companies.Any(c => c.Documents.Count > 0);

    private static string OwnerTypeLabel(CoiCompanyOwnerType type) => type switch
    {
        CoiCompanyOwnerType.Self => "I.B — Self-owned",
        CoiCompanyOwnerType.Relative => "I.C — Relative-owned",
        _ => "I.D — Board/executive role",
    };

    private static string RelationshipLabel(RelativeRelationship relationship) => relationship switch
    {
        RelativeRelationship.InLaws => "In-Laws",
        RelativeRelationship.FatherInLaw => "Father-in-Law",
        RelativeRelationship.MotherInLaw => "Mother-in-Law",
        RelativeRelationship.Stepchildren => "Children of spouse",
        _ => relationship.ToString(),
    };

    protected override async Task OnInitializedAsync()
    {
        // Its own short-lived context rather than the circuit-scoped ApplicationDbContext:
        // sharing that one lets this race, or outlive, whatever else in the circuit is using
        // it -- which kills the circuit and takes the page with it.
        await using var db = await DbFactory.CreateDbContextAsync();

        var recipients = await db.DeclarationCycleRunRecipients
            .AsNoTracking()
            .Include(r => r.DeclarationCycleRun)
            .Where(r => r.DeclarationCycleRun!.Type == DeclarationCycleType.ConflictOfInterest)
            .ToListAsync();

        var members = await db.Members
            .AsNoTracking()
            .Include(m => m.Company)
            .Include(m => m.Department)
            .ToDictionaryAsync(m => m.Id);

        var declarations = await db.RelatedPartyCoiDeclarations
            .AsNoTracking()
            .Include(d => d.Relatives)
            .Include(d => d.Companies).ThenInclude(c => c.Documents)
            .Include(d => d.Conflicts)
            .ToListAsync();
        var declarationLookup = declarations.ToDictionary(d => (d.MemberId, d.DeclarationCycleRunId));

        _rows = recipients.Select(r =>
        {
            var run = r.DeclarationCycleRun!;
            members.TryGetValue(r.MemberId, out var member);
            declarationLookup.TryGetValue((r.MemberId, run.Id), out var declaration);

            return new SubmissionRow
            {
                MemberId = r.MemberId,
                MemberName = r.MemberName,
                EntityCompanyId = member?.CompanyId,
                EntityShortCode = member?.Company?.ShortCode ?? r.CompanyName,
                DepartmentName = member?.Department?.Name,
                Year = run.PeriodYear,
                Quarter = run.PeriodQuarter,
                // A saved-but-not-submitted draft is not a completed declaration for compliance
                // reporting purposes -- it's still pending until the member actually submits it.
                Submitted = declaration is { IsDraft: false },
                Declaration = declaration,
            };
        }).ToList();

        _companies = await db.Companies.OrderBy(c => c.Name).ToListAsync();
    }

    private List<int> AvailableYears() => (_rows ?? [])
        .Select(r => r.Year)
        .Distinct()
        .OrderByDescending(y => y)
        .ToList();

    private List<SubmissionRow> FilteredRows()
    {
        if (_rows is null) return [];

        IEnumerable<SubmissionRow> query = _rows;

        if (_yearFilter != 0) query = query.Where(r => r.Year == _yearFilter);
        if (_quarterFilter != 0) query = query.Where(r => r.Quarter == _quarterFilter);
        if (_companyFilter != 0) query = query.Where(r => r.EntityCompanyId == _companyFilter);
        if (_statusFilter == "complete") query = query.Where(r => r.Submitted);
        else if (_statusFilter == "pending") query = query.Where(r => !r.Submitted);

        if (!string.IsNullOrWhiteSpace(_search))
        {
            var q = _search.Trim();
            query = query.Where(r => r.MemberName.Contains(q, StringComparison.OrdinalIgnoreCase));
        }

        IOrderedEnumerable<SubmissionRow> sorted = _sortColumn switch
        {
            "Member" => query.OrderBy(r => r.MemberName),
            "Entity" => query.OrderBy(r => r.EntityShortCode),
            "Quarter" => query.OrderBy(r => r.Year).ThenBy(r => r.Quarter),
            _ => query.OrderBy(r => r.Declaration != null ? r.Declaration.SubmittedAtUtc : DateTime.MaxValue),
        };
        return (_sortAscending ? sorted : sorted.Reverse()).ToList();
    }

    private void Sort(string column)
    {
        if (_sortColumn == column) _sortAscending = !_sortAscending;
        else { _sortColumn = column; _sortAscending = true; }
    }

    private static int CompanyCount(RelatedPartyCoiDeclaration d) => d.Companies.Count;
}
