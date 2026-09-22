using Microsoft.EntityFrameworkCore;
using Microsoft.JSInterop;
using CGTOOL.Web.Data;
using CGTOOL.Web.Data.Governance;

namespace CGTOOL.Web.Components.Pages.Admin;

public partial class RelatedPartyRegister
{
    /// <summary>One row per CoiCompanyEntry (I.B/I.C/I.D), consolidated across every declarant --
    /// Functional Spec §6 "Related Party Register &amp; Reporting."</summary>
    private class RegisterRow
    {
        public required string MemberName { get; init; }
        public string? EntityShortCode { get; init; }
        public int? EntityCompanyId { get; init; }
        public string? Designation { get; init; }
        public required int Year { get; init; }
        public required int Quarter { get; init; }
        public required CoiCompanyOwnerType OwnerType { get; init; }
        public required string LegalCompanyName { get; init; }
        public string? PrincipalBusinessActivity { get; init; }
        public string? TradeLicenseNumber { get; init; }
        public DateTime? TradeLicenseExpiryDate { get; init; }
        public string? LicenseActivities { get; init; }
        public required IReadOnlyList<CoiTradeLicenseDocument> Documents { get; init; }
    }

    /// <summary>One row of the "Related Party Register" view -- one row per declared relative (I.A),
    /// with MemberName identifying the declarant it traces back to; a declarant with no relatives
    /// declared gets a single placeholder row instead ("Self" for Normal Users, whose declarations
    /// are reviewed against their own outside interests, or "Nothing Declared" for Board/Executive
    /// Management). Relatives don't carry their own uploaded documents -- ViewDocuments links back to
    /// the whole declaration (every I.B/I.C/I.D document from that same submission), same as the
    /// RP&amp;COI Submission report.</summary>
    private class RelativeRow
    {
        public required string MemberName { get; init; }
        public string? EntityShortCode { get; init; }
        public int? EntityCompanyId { get; init; }
        public string? CompanyName { get; init; }
        public string? Designation { get; init; }
        public required int Year { get; init; }
        public required int Quarter { get; init; }
        public required string RelatedParty { get; init; }
        public required string Category { get; init; }
        public string? EmiratesIdNameOnCard { get; init; }
        public string? EmiratesIdNumber { get; init; }
        public DateTime? EmiratesIdExpiryDate { get; init; }
        public required RelatedPartyCoiDeclaration Declaration { get; init; }
    }

    private List<RegisterRow>? _rows;
    private List<RelativeRow>? _relativeRows;
    private List<Company>? _companies;
    private string _activeTab = "relatives";
    private string _search = string.Empty;
    private int _yearFilter;
    private int _quarterFilter;
    private int _companyFilter;
    private string _designationFilter = string.Empty;
    private string _ownerTypeFilter = string.Empty;
    private string _expiryFilter = string.Empty;
    private string _sortColumn = "RelatedParty";
    private bool _sortAscending = true;
    private bool _showPrintPreview;
    private RelativeRow? _viewDocumentsRecord;

    private async Task PrintAsync() => await JS.InvokeVoidAsync("print");

    protected override async Task OnInitializedAsync()
    {
        // Own DbContext instance (not the shared circuit-scoped one) -- this page's several
        // sequential queries would otherwise race against whatever else on the circuit happens to
        // touch the shared context, throwing "A second operation was started on this context
        // instance before a previous operation completed." Same pattern NavMenu already uses.
        await using var db = await DbFactory.CreateDbContextAsync();

        var declarations = await db.RelatedPartyCoiDeclarations
            .AsNoTracking()
            .Include(d => d.Member).ThenInclude(m => m!.Company)
            .Include(d => d.DeclarationCycleRun)
            .Include(d => d.Relatives)
            .Include(d => d.Companies).ThenInclude(c => c.Documents)
            .Where(d => !d.IsDraft)
            .ToListAsync();

        // Emirates ID (name on card / number / expiry) has no field of its own on the RP&COI
        // declaration -- it's only ever captured when a member submits an Insider Trading
        // declaration. Pick up the most recent submission per member that actually provided it.
        var latestEmiratesIdByMember = (await db.InsiderDeclarations
                .AsNoTracking()
                .Where(x => !x.IsDraft && x.EmiratesIdNumber != null)
                .ToListAsync())
            .GroupBy(x => x.MemberId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.ModifiedAtUtc ?? x.SubmittedAtUtc).First());

        _rows = declarations
            .SelectMany(d => d.Companies.Select(c => new RegisterRow
            {
                MemberName = d.Member?.FullName ?? "—",
                EntityShortCode = d.Member?.Company?.ShortCode,
                EntityCompanyId = d.Member?.CompanyId,
                Designation = d.Member?.JobTitle,
                Year = d.DeclarationCycleRun!.PeriodYear,
                Quarter = d.DeclarationCycleRun!.PeriodQuarter,
                OwnerType = c.OwnerType,
                LegalCompanyName = c.LegalCompanyName,
                PrincipalBusinessActivity = c.PrincipalBusinessActivity,
                TradeLicenseNumber = c.TradeLicenseNumber,
                TradeLicenseExpiryDate = c.TradeLicenseExpiryDate,
                LicenseActivities = c.LicenseActivities,
                Documents = c.Documents,
            }))
            .ToList();

        _relativeRows = declarations
            .SelectMany(d =>
            {
                latestEmiratesIdByMember.TryGetValue(d.MemberId, out var eid);
                var isBoardOrExecutive = d.Member?.IsBoardMember == true || d.Member?.IsExecutiveManagement == true;

                RelativeRow Build(string relatedParty, string category) => new()
                {
                    MemberName = d.Member?.FullName ?? "—",
                    EntityShortCode = d.Member?.Company?.ShortCode,
                    EntityCompanyId = d.Member?.CompanyId,
                    CompanyName = d.Member?.Company?.Name,
                    Designation = d.Member?.JobTitle,
                    Year = d.DeclarationCycleRun!.PeriodYear,
                    Quarter = d.DeclarationCycleRun!.PeriodQuarter,
                    RelatedParty = relatedParty,
                    Category = category,
                    EmiratesIdNameOnCard = eid?.EmiratesIdNameOnCard,
                    EmiratesIdNumber = eid?.EmiratesIdNumber,
                    EmiratesIdExpiryDate = eid?.EmiratesIdExpiryDate,
                    Declaration = d,
                };

                // Any declarant's declared relatives (I.A) are the related parties -- shown by the
                // relative's own name and relationship (father, brother, etc.), alongside the Member
                // column identifying which declarant they trace back to. This applies regardless of
                // Board/Executive vs Normal User status: a Normal User's declared relatives are just
                // as much related parties as a Board/Executive's. Only when nothing was declared do
                // we fall back to a placeholder row so the submission doesn't silently disappear from
                // the register -- "Self" for Normal Users (whose own outside interests are what's
                // tracked when there's no relative to report), "Nothing Declared" for Board/Executive.
                return d.Relatives.Count > 0
                    ? d.Relatives.Select(r => Build(r.Name, RelationshipLabel(r.Relationship)))
                    : isBoardOrExecutive
                        ? [Build(d.Member?.FullName ?? "—", "Nothing Declared")]
                        : [Build(d.Member?.FullName ?? "—", "Self")];
            })
            .ToList();

        _companies = await db.Companies.OrderBy(c => c.Name).ToListAsync();
    }

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

    private static bool HasAnyDocument(RelativeRow r) => r.Declaration.Companies.Any(c => c.Documents.Count > 0);

    private List<int> AvailableYears() => (_rows ?? [])
        .Select(r => r.Year)
        .Concat((_relativeRows ?? []).Select(r => r.Year))
        .Distinct()
        .OrderByDescending(y => y)
        .ToList();

    private List<string> AvailableDesignations() => (_rows ?? [])
        .Select(r => r.Designation)
        .Concat((_relativeRows ?? []).Select(r => r.Designation))
        .Where(d => !string.IsNullOrWhiteSpace(d))
        .Select(d => d!)
        .Distinct()
        .OrderBy(d => d)
        .ToList();

    private List<RegisterRow> FilteredRows()
    {
        if (_rows is null) return [];

        IEnumerable<RegisterRow> query = _rows;

        if (_yearFilter != 0) query = query.Where(r => r.Year == _yearFilter);
        if (_quarterFilter != 0) query = query.Where(r => r.Quarter == _quarterFilter);
        if (_companyFilter != 0) query = query.Where(r => r.EntityCompanyId == _companyFilter);
        if (!string.IsNullOrEmpty(_designationFilter)) query = query.Where(r => r.Designation == _designationFilter);
        if (!string.IsNullOrEmpty(_ownerTypeFilter) && Enum.TryParse<CoiCompanyOwnerType>(_ownerTypeFilter, out var ownerType))
        {
            query = query.Where(r => r.OwnerType == ownerType);
        }
        if (!string.IsNullOrEmpty(_expiryFilter))
        {
            query = query.Where(r => SubmitRelatedPartyCoiDeclaration.ExpiryStatus(r.TradeLicenseExpiryDate) == _expiryFilter);
        }

        if (!string.IsNullOrWhiteSpace(_search))
        {
            var q = _search.Trim();
            query = query.Where(r =>
                r.MemberName.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                r.LegalCompanyName.Contains(q, StringComparison.OrdinalIgnoreCase));
        }

        IOrderedEnumerable<RegisterRow> sorted = _sortColumn switch
        {
            "Member" => query.OrderBy(r => r.MemberName),
            "Entity" => query.OrderBy(r => r.EntityShortCode),
            "Company" => query.OrderBy(r => r.LegalCompanyName),
            "Quarter" => query.OrderBy(r => r.Year).ThenBy(r => r.Quarter),
            _ => query.OrderBy(r => r.TradeLicenseExpiryDate ?? DateTime.MaxValue),
        };
        return (_sortAscending ? sorted : sorted.Reverse()).ToList();
    }

    private List<RelativeRow> FilteredRelativeRows()
    {
        if (_relativeRows is null) return [];

        IEnumerable<RelativeRow> query = _relativeRows;

        if (_yearFilter != 0) query = query.Where(r => r.Year == _yearFilter);
        if (_quarterFilter != 0) query = query.Where(r => r.Quarter == _quarterFilter);
        if (_companyFilter != 0) query = query.Where(r => r.EntityCompanyId == _companyFilter);
        if (!string.IsNullOrEmpty(_designationFilter)) query = query.Where(r => r.Designation == _designationFilter);

        if (!string.IsNullOrWhiteSpace(_search))
        {
            var q = _search.Trim();
            query = query.Where(r =>
                r.MemberName.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                r.RelatedParty.Contains(q, StringComparison.OrdinalIgnoreCase));
        }

        IOrderedEnumerable<RelativeRow> sorted = _sortColumn switch
        {
            "Entity" => query.OrderBy(r => r.EntityShortCode),
            "Company" => query.OrderBy(r => r.CompanyName),
            "Quarter" => query.OrderBy(r => r.Year).ThenBy(r => r.Quarter),
            _ => query.OrderBy(r => r.RelatedParty),
        };
        return (_sortAscending ? sorted : sorted.Reverse()).ToList();
    }

    private void Sort(string column)
    {
        if (_sortColumn == column) _sortAscending = !_sortAscending;
        else { _sortColumn = column; _sortAscending = true; }
    }

    private void SwitchTab(string tab)
    {
        _activeTab = tab;
        _sortColumn = tab == "relatives" ? "RelatedParty" : "Expiry";
        _sortAscending = true;
    }
}
