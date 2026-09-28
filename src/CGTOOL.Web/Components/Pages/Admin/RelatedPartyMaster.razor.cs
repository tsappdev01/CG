using Microsoft.EntityFrameworkCore;
using Microsoft.JSInterop;
using CGTOOL.Web.Data;
using CGTOOL.Web.Data.Governance;

namespace CGTOOL.Web.Components.Pages.Admin;

public partial class RelatedPartyMaster
{
    /// <summary>Where a row came from. The master is a union of four standing lists, and which one a
    /// row came from decides what its Type column means and which of the other columns it can fill --
    /// so it is carried on the row rather than worked out again at render time.</summary>
    public enum MasterSource
    {
        Entity,
        User,
        RelativeCompany,
        MemberCompany,
    }

    public static string SourceLabel(MasterSource source) => source switch
    {
        MasterSource.Entity => "Entity",
        MasterSource.User => "User",
        MasterSource.RelativeCompany => "Relative's company",
        _ => "Member's company",
    };

    /// <summary>One related party. Deliberately one flat row type for all four sources rather than a
    /// table each: the point of a master is that it reads as a single list, and a column a given
    /// source has no answer for is simply blank.</summary>
    private class MasterRow
    {
        public required MasterSource Source { get; init; }
        public required string Name { get; init; }

        /// <summary>The entity type for an entity, the declaration type for a user. One column,
        /// because it answers the same question of both -- what kind of related party this is --
        /// and the master would be unreadable with one column per source that only ever has one
        /// value filled.</summary>
        public string? Type { get; init; }

        public string? EntityName { get; init; }
        public int? EntityCompanyId { get; init; }
        public string? Department { get; init; }

        /// <summary>The member whose register this row came from. Empty for entities and users,
        /// which are the register.</summary>
        public string? MemberName { get; init; }

        /// <summary>Who the interest belongs to: the relative for a relative's company, the member
        /// for their own.</summary>
        public string? RelatedTo { get; init; }

        /// <summary>How RelatedTo is related to the member -- the relationship for a relative,
        /// "Self" for the member's own company.</summary>
        public string? Relationship { get; init; }

        public RelatedPartyHoldingNature? NatureOfHolding { get; init; }
        public string? NatureOfInterest { get; init; }
        public decimal? OwnershipPercentage { get; init; }
        public string? PrincipalBusinessActivity { get; init; }
        public string? TradeLicenceNumber { get; init; }
        public DateTime? TradeLicenceExpiryDate { get; init; }

        /// <summary>Whether the underlying entity or user is active. Null where the row is a company
        /// on someone's register, which carries no active flag of its own.</summary>
        public bool? Active { get; init; }
    }

    private List<MasterRow>? _rows;
    private List<Company>? _companies;
    private string _search = string.Empty;
    private string _sourceFilter = string.Empty;
    private int _companyFilter;
    private string _typeFilter = string.Empty;
    private string _holdingFilter = string.Empty;
    private string _statusFilter = string.Empty;
    private string _sortColumn = "Name";
    private bool _sortAscending = true;
    private bool _showPrintPreview;

    private async Task PrintAsync() => await JS.InvokeVoidAsync("print");

    protected override async Task OnInitializedAsync()
    {
        // Own DbContext instance rather than the circuit-scoped one, like the other reports: these
        // sequential queries would otherwise race whatever else on the circuit touches the shared
        // context ("a second operation was started on this context instance").
        await using var db = await DbFactory.CreateDbContextAsync();

        var companies = await db.Companies
            .AsNoTracking()
            .OrderBy(c => c.Name)
            .ToListAsync();

        var members = await db.Members
            .AsNoTracking()
            .Include(m => m.Company)
            .Include(m => m.Department)
            .OrderBy(m => m.FullName)
            .ToListAsync();

        var ownedCompanies = await db.OwnedCompanies
            .AsNoTracking()
            .Include(o => o.Member).ThenInclude(m => m!.Company)
            .ToListAsync();

        var holdings = await db.FamilyMemberHoldings
            .AsNoTracking()
            .Include(h => h.FamilyMember).ThenInclude(f => f!.Member).ThenInclude(m => m!.Company)
            .ToListAsync();

        _companies = companies;

        _rows =
        [
            .. companies.Select(c => new MasterRow
            {
                Source = MasterSource.Entity,
                Name = c.Name,
                Type = CompanyEntityTypes.Label(c.EntityType),
                EntityName = c.Name,
                EntityCompanyId = c.Id,
                Active = c.Active,
            }),

            .. members.Select(m => new MasterRow
            {
                Source = MasterSource.User,
                Name = m.FullName,
                Type = MemberDeclarationTypes.Label(m.DeclarationType),
                EntityName = m.Company?.Name,
                EntityCompanyId = m.CompanyId,
                Department = m.Department?.Name,
                Active = m.Active,
            }),

            .. holdings.Select(h => new MasterRow
            {
                Source = MasterSource.RelativeCompany,
                Name = h.CompanyName,
                EntityName = h.FamilyMember?.Member?.Company?.Name,
                EntityCompanyId = h.FamilyMember?.Member?.CompanyId,
                MemberName = h.FamilyMember?.Member?.FullName,
                RelatedTo = h.FamilyMember?.Name,
                Relationship = h.FamilyMember is null ? null : RelationshipLabel(h.FamilyMember.Relationship),
                NatureOfHolding = h.NatureOfHolding,
                NatureOfInterest = h.NatureOfInterest,
                OwnershipPercentage = h.OwnershipPercentage,
                PrincipalBusinessActivity = h.PrincipalBusinessActivity,
                TradeLicenceNumber = h.TradeLicenceNumber,
                TradeLicenceExpiryDate = h.TradeLicenceExpiryDate,
            }),

            .. ownedCompanies.Select(o => new MasterRow
            {
                Source = MasterSource.MemberCompany,
                Name = o.CompanyName,
                EntityName = o.Member?.Company?.Name,
                EntityCompanyId = o.Member?.CompanyId,
                MemberName = o.Member?.FullName,
                RelatedTo = o.Member?.FullName,
                Relationship = "Self",
                NatureOfHolding = o.NatureOfHolding,
                NatureOfInterest = o.NatureOfInterest,
                OwnershipPercentage = o.OwnershipPercentage,
                PrincipalBusinessActivity = o.PrincipalBusinessActivity,
                TradeLicenceNumber = o.TradeLicenceNumber,
                TradeLicenceExpiryDate = o.TradeLicenceExpiryDate,
            }),
        ];
    }

    private static string RelationshipLabel(RelativeRelationship relationship) => relationship switch
    {
        RelativeRelationship.InLaws => "In-Laws",
        RelativeRelationship.FatherInLaw => "Father-in-Law",
        RelativeRelationship.MotherInLaw => "Mother-in-Law",
        RelativeRelationship.Stepchildren => "Children of spouse",
        _ => relationship.ToString(),
    };

    public static string HoldingLabel(RelatedPartyHoldingNature? nature) =>
        nature is null or RelatedPartyHoldingNature.None ? "—" : nature.ToString()!;

    /// <summary>Every Type value in the data, across both meanings of the column, so the filter
    /// offers what is actually there rather than the union of two enums that can never both match.</summary>
    private List<string> AvailableTypes() => (_rows ?? [])
        .Select(r => r.Type)
        .Where(t => !string.IsNullOrWhiteSpace(t))
        .Select(t => t!)
        .Distinct()
        .OrderBy(t => t, StringComparer.OrdinalIgnoreCase)
        .ToList();

    private int CountOf(MasterSource source) => (_rows ?? []).Count(r => r.Source == source);

    /// <summary>The four lists the master is made of, in the order they are counted across the top.</summary>
    public static readonly (MasterSource Source, string Label)[] Tiles =
    [
        (MasterSource.Entity, "Entities"),
        (MasterSource.User, "Users"),
        (MasterSource.MemberCompany, "Members' companies"),
        (MasterSource.RelativeCompany, "Relatives' companies"),
    ];

    /// <summary>Clicking the tile already filtered on clears it, so the tiles work as a toggle rather
    /// than a one-way trip into a filter you then have to find in the dropdown to get out of.</summary>
    private void ToggleSource(MasterSource source) =>
        _sourceFilter = _sourceFilter == source.ToString() ? string.Empty : source.ToString();

    private List<MasterRow> FilteredRows()
    {
        IEnumerable<MasterRow> query = _rows ?? [];

        if (!string.IsNullOrWhiteSpace(_search))
        {
            var term = _search.Trim();
            query = query.Where(r =>
                Contains(r.Name, term) || Contains(r.MemberName, term) || Contains(r.RelatedTo, term)
                || Contains(r.EntityName, term) || Contains(r.Type, term) || Contains(r.Department, term)
                || Contains(r.PrincipalBusinessActivity, term) || Contains(r.NatureOfInterest, term)
                || Contains(r.TradeLicenceNumber, term));
        }

        if (!string.IsNullOrWhiteSpace(_sourceFilter) && Enum.TryParse<MasterSource>(_sourceFilter, out var source))
        {
            query = query.Where(r => r.Source == source);
        }

        if (_companyFilter != 0)
        {
            query = query.Where(r => r.EntityCompanyId == _companyFilter);
        }

        if (!string.IsNullOrWhiteSpace(_typeFilter))
        {
            query = query.Where(r => r.Type == _typeFilter);
        }

        if (!string.IsNullOrWhiteSpace(_holdingFilter) && Enum.TryParse<RelatedPartyHoldingNature>(_holdingFilter, out var nature))
        {
            query = query.Where(r => r.NatureOfHolding == nature);
        }

        // "Active"/"Inactive" are about entities and users; a company on someone's register has no
        // such flag, so filtering on one drops those rows rather than guessing a state for them.
        if (_statusFilter == "active") query = query.Where(r => r.Active == true);
        if (_statusFilter == "inactive") query = query.Where(r => r.Active == false);

        query = (_sortColumn, _sortAscending) switch
        {
            ("Source", true) => query.OrderBy(r => r.Source).ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase),
            ("Source", false) => query.OrderByDescending(r => r.Source).ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase),
            ("Type", true) => query.OrderBy(r => r.Type, StringComparer.OrdinalIgnoreCase).ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase),
            ("Type", false) => query.OrderByDescending(r => r.Type, StringComparer.OrdinalIgnoreCase).ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase),
            ("Entity", true) => query.OrderBy(r => r.EntityName, StringComparer.OrdinalIgnoreCase).ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase),
            ("Entity", false) => query.OrderByDescending(r => r.EntityName, StringComparer.OrdinalIgnoreCase).ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase),
            ("RelatedTo", true) => query.OrderBy(r => r.RelatedTo, StringComparer.OrdinalIgnoreCase).ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase),
            ("RelatedTo", false) => query.OrderByDescending(r => r.RelatedTo, StringComparer.OrdinalIgnoreCase).ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase),
            ("Expiry", true) => query.OrderBy(r => r.TradeLicenceExpiryDate ?? DateTime.MaxValue),
            ("Expiry", false) => query.OrderByDescending(r => r.TradeLicenceExpiryDate ?? DateTime.MinValue),
            (_, false) => query.OrderByDescending(r => r.Name, StringComparer.OrdinalIgnoreCase),
            _ => query.OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase),
        };

        return query.ToList();
    }

    private static bool Contains(string? value, string term) =>
        value is not null && value.Contains(term, StringComparison.OrdinalIgnoreCase);

    private void Sort(string column)
    {
        if (_sortColumn == column)
        {
            _sortAscending = !_sortAscending;
        }
        else
        {
            _sortColumn = column;
            _sortAscending = true;
        }
    }

    private void ClearFilters()
    {
        _search = string.Empty;
        _sourceFilter = string.Empty;
        _companyFilter = 0;
        _typeFilter = string.Empty;
        _holdingFilter = string.Empty;
        _statusFilter = string.Empty;
    }
}
