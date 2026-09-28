using System.Globalization;
using Microsoft.EntityFrameworkCore;
using CGTOOL.Web.Data;
using CGTOOL.Web.Data.Governance;

namespace CGTOOL.Web.Components.Pages.Admin;

/// <summary>Where "route by authority — per DoA matrix" stops being a line on a diagram. One matrix
/// per entity: the value bands that say which body must ultimately approve, and the approver's own
/// limit, above which a transaction never reaches them.</summary>
public partial class DelegationOfAuthorityPage
{
    private List<Company>? _companies;
    private readonly HashSet<int> _configuredCompanyIds = [];
    private List<string> _unconfigured = [];
    private int _companyId;
    private DelegationOfAuthority _matrix = new();
    private bool _saving;

    private string CurrentCompanyName => _companies?.FirstOrDefault(c => c.Id == _companyId)?.Name ?? "—";

    protected override async Task OnInitializedAsync()
    {
        await using var db = await DbFactory.CreateDbContextAsync();
        _companies = await db.Companies.AsNoTracking().OrderBy(c => c.Name).ToListAsync();

        _companyId = _companies.FirstOrDefault()?.Id ?? 0;
        await RefreshConfiguredAsync(db);
        await LoadForCompanyAsync(_companyId);
    }

    private async Task RefreshConfiguredAsync(ApplicationDbContext db)
    {
        var ids = await db.DelegationsOfAuthority.AsNoTracking().Select(d => d.CompanyId).ToListAsync();
        _configuredCompanyIds.Clear();
        foreach (var id in ids) _configuredCompanyIds.Add(id);

        _unconfigured = (_companies ?? [])
            .Where(c => !_configuredCompanyIds.Contains(c.Id))
            .Select(c => c.Name)
            .ToList();
    }

    private async Task LoadForCompanyAsync(int companyId)
    {
        _companyId = companyId;

        await using var db = await DbFactory.CreateDbContextAsync();
        var existing = await db.DelegationsOfAuthority
            .AsNoTracking()
            .Include(d => d.Bands)
            .FirstOrDefaultAsync(d => d.CompanyId == companyId);

        // A brand new matrix opens on one band covering everything, so the first save is a decision
        // about where the boundaries go rather than a blank form to work out from scratch.
        _matrix = existing ?? new DelegationOfAuthority
        {
            CompanyId = companyId,
            EffectiveFrom = DateTime.Today,
            Bands = [new DelegationOfAuthorityBand { FromValue = 0, ToValue = null, Authority = RpApprovalAuthority.ApproverThenCcao }],
        };
        _matrix.Bands = [.. _matrix.Bands.OrderBy(b => b.FromValue)];
    }

    private void AddBand()
    {
        // A new band starts where the last one ends, which is the only place it can go without
        // immediately failing the gap and overlap checks.
        var last = _matrix.Bands.OrderBy(b => b.FromValue).LastOrDefault();
        var from = last?.ToValue is { } end ? end + 1 : (last?.FromValue ?? 0) + 1;

        if (last is { ToValue: null }) last.ToValue = last.FromValue + 999_999;

        _matrix.Bands.Add(new DelegationOfAuthorityBand { FromValue = from, ToValue = null, Authority = RpApprovalAuthority.ApproverCcaoThenBoard });
        _matrix.Bands = [.. _matrix.Bands.OrderBy(b => b.FromValue)];
    }

    private static decimal? ReadMoney(string? raw) =>
        decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out var value) ? value : null;

    private void SetFrom(DelegationOfAuthorityBand band, string? raw) => band.FromValue = ReadMoney(raw) ?? 0;

    private void SetTo(DelegationOfAuthorityBand band, string? raw) => band.ToValue = ReadMoney(raw);

    private void SetApproverLimit(string? raw) => _matrix.ApproverLimit = ReadMoney(raw);

    private void SetEscalateAfterDays(string? raw) =>
        _matrix.EscalateAfterDays = int.TryParse(raw, out var days) && days > 0 ? days : null;

    /// <summary>What has to be true before a matrix can route anything. Shown as it stands rather
    /// than only on save: a matrix with a gap in it silently sends whatever falls in the gap
    /// nowhere, which is the one failure nobody would notice.</summary>
    private List<(string Label, bool Ok)> Coverage
    {
        get
        {
            var bands = _matrix.Bands.OrderBy(b => b.FromValue).ToList();
            var startsAtZero = bands.Count > 0 && bands[0].FromValue == 0;
            var topIsOpen = bands.Count > 0 && bands[^1].ToValue is null;

            var continuous = true;
            var overlapping = false;
            for (var i = 0; i < bands.Count - 1; i++)
            {
                if (bands[i].ToValue is not { } end) { overlapping = true; continue; }
                if (bands[i + 1].FromValue <= end) overlapping = true;
                if (bands[i + 1].FromValue > end + 1) continuous = false;
            }

            var eachOrdered = bands.All(b => b.ToValue is null || b.ToValue >= b.FromValue);

            return
            [
                ("Bands start at zero", startsAtZero),
                ("Bands are continuous — no gaps", continuous),
                ("No overlapping bands", !overlapping),
                ("Top band has no upper limit", topIsOpen),
                ("Every band ends after it starts", eachOrdered),
            ];
        }
    }

    private List<string> Problems => Coverage.Where(c => !c.Ok).Select(c => c.Label).ToList();

    private async Task SaveAsync()
    {
        if (_saving || _companyId == 0) return;
        if (Problems.Count > 0)
        {
            Toasts.ShowError($"Fix the bands first: {string.Join("; ", Problems)}.");
            return;
        }

        _saving = true;
        try
        {
            _matrix.CompanyId = _companyId;
            await Writer.SaveAsync(_matrix);

            var state = await AuthState.GetAuthenticationStateAsync();
            await AuditLog.LogAsync(state.User.Identity?.Name ?? "unknown", AuditAction.Update, nameof(DelegationOfAuthority), _companyId.ToString(),
                $"Delegation of Authority saved for {CurrentCompanyName}: " +
                $"{string.Join("; ", _matrix.Bands.OrderBy(b => b.FromValue).Select(b => $"{b.Range} → {RpApprovalAuthorities.Label(b.Authority)}"))}. " +
                $"Approver limit: {(_matrix.ApproverLimit is { } limit ? limit.ToString("N2") : "none")}.");

            await using var db = await DbFactory.CreateDbContextAsync();
            await RefreshConfiguredAsync(db);
            await LoadForCompanyAsync(_companyId);

            Toasts.ShowSuccess($"Delegation of Authority saved for {CurrentCompanyName}.");
        }
        finally
        {
            _saving = false;
        }
    }

    private async Task DeleteAsync()
    {
        if (_saving || _companyId == 0) return;

        _saving = true;
        try
        {
            var name = CurrentCompanyName;
            await Writer.DeleteAsync(_companyId);

            var state = await AuthState.GetAuthenticationStateAsync();
            await AuditLog.LogAsync(state.User.Identity?.Name ?? "unknown", AuditAction.Delete, nameof(DelegationOfAuthority), _companyId.ToString(),
                $"Delegation of Authority removed for {name}; transactions revert to the single approving authority whatever their value.");

            await using var db = await DbFactory.CreateDbContextAsync();
            await RefreshConfiguredAsync(db);
            await LoadForCompanyAsync(_companyId);

            Toasts.ShowSuccess($"Matrix removed for {name}.");
        }
        finally
        {
            _saving = false;
        }
    }
}
