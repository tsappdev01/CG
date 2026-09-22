using Microsoft.EntityFrameworkCore;
using Microsoft.JSInterop;
using CGTOOL.Web.Data;
using CGTOOL.Web.Data.Governance;

namespace CGTOOL.Web.Components.Pages.Admin;

public partial class InsiderDeclarationReport
{
    /// <summary>One row per member who was actually sent an Insider Trading notification for a
    /// given cycle -- Declaration is non-null only once they've submitted, so the report can show
    /// everyone who was required to declare, not just those who already have.</summary>
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
        public InsiderDeclaration? Declaration { get; init; }
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
    private bool _showColumnPicker;
    private SubmissionRow? _viewDocumentsRecord;

    private static bool HasAnyDocument(SubmissionRow r) => r.Declaration is not null && (
        !string.IsNullOrEmpty(r.Declaration.EmiratesIdPath) ||
        !string.IsNullOrEmpty(r.Declaration.PassportPath) ||
        !string.IsNullOrEmpty(r.Declaration.TradeLicencePath) ||
        !string.IsNullOrEmpty(r.Declaration.OtherDocumentPath));

    private static readonly (string Key, string Label)[] PrintColumns =
    [
        ("Member", "Member"),
        ("Entity", "Entity"),
        ("Department", "Department"),
        ("Quarter", "Quarter"),
        ("Submission", "Submission"),
        ("HasNin", "Has NIN"),
        ("HoldsShares", "Holds shares"),
        ("Relatives", "Relatives"),
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

    protected override async Task OnInitializedAsync()
    {
        var recipients = await Db.DeclarationCycleRunRecipients
            .AsNoTracking()
            .Include(r => r.DeclarationCycleRun)
            .Where(r => r.DeclarationCycleRun!.Type == DeclarationCycleType.InsiderTrading)
            .ToListAsync();

        var members = await Db.Members
            .AsNoTracking()
            .Include(m => m.Company)
            .Include(m => m.Department)
            .ToDictionaryAsync(m => m.Id);

        var declarations = await Db.InsiderDeclarations
            .AsNoTracking()
            .Include(d => d.Relatives)
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
                // The period an Insider Trading notification covers is chosen explicitly by the
                // admin when sending/scheduling it (Declarations Setup), not derived from when it
                // happened to be sent.
                Year = run.PeriodYear,
                Quarter = run.PeriodQuarter,
                // A saved-but-not-submitted draft is not a completed declaration for compliance
                // reporting purposes -- it's still pending until the member actually submits it.
                Submitted = declaration is { IsDraft: false },
                Declaration = declaration,
            };
        }).ToList();

        _companies = await Db.Companies.OrderBy(c => c.Name).ToListAsync();
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
}
