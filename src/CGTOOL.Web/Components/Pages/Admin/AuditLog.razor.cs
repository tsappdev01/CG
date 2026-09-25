using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.EntityFrameworkCore;
using Microsoft.JSInterop;
using CGTOOL.Web.Data;
using CGTOOL.Web.Data.Governance;

namespace CGTOOL.Web.Components.Pages.Admin;

public partial class AuditLog
{
    private const int MaxRows = 500;

    [Parameter] public string? ModeSlug { get; set; }

    private List<AuditLogEntry>? _entries;
    private int _totalCount;
    private DateOnly? _from;
    private DateOnly? _to;
    private string _sortColumn = "When";
    private bool _sortAscending = false;
    private bool _showPrintPreview;

    private async Task PrintAsync() => await JS.InvokeVoidAsync("print");

    private List<AuditLogEntry> SortedEntries()
    {
        if (_entries is null) return [];
        IOrderedEnumerable<AuditLogEntry> sorted = _sortColumn switch
        {
            "Who" => _entries.OrderBy(e => e.ActorDisplayName),
            "ActingOnBehalfOf" => _entries.OrderBy(e => e.ActingOnBehalfOf),
            "Action" => _entries.OrderBy(e => e.Action),
            "Entity" => _entries.OrderBy(e => e.EntityType),
            _ => _entries.OrderBy(e => e.OccurredAtUtc),
        };
        return (_sortAscending ? sorted : sorted.Reverse()).ToList();
    }

    private void Sort(string column)
    {
        if (_sortColumn == column) _sortAscending = !_sortAscending;
        else { _sortColumn = column; _sortAscending = true; }
    }

    /// <summary>"Daily Logs" (default route) always shows today (UTC); "Periodic Review" (/periodic)
    /// exposes an editable From/To range, defaulting to the last 30 days.</summary>
    private bool IsPeriodic => ModeSlug == "periodic";

    private string PageHeading => IsPeriodic ? "Audit log — periodic review" : "Audit log — daily";

    protected override async Task OnParametersSetAsync()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        if (IsPeriodic)
        {
            _from = today.AddDays(-30);
            _to = today;
        }
        else
        {
            _from = today;
            _to = today;
        }

        await LoadAsync();
    }

    private async Task OnFromChangedAsync(ChangeEventArgs e)
    {
        if (DateOnly.TryParse((string?)e.Value, out var parsed)) _from = parsed;
        await LoadAsync();
    }

    private async Task OnToChangedAsync(ChangeEventArgs e)
    {
        if (DateOnly.TryParse((string?)e.Value, out var parsed)) _to = parsed;
        await LoadAsync();
    }

    private async Task LoadAsync()
    {
        // Its own short-lived context rather than the circuit-scoped ApplicationDbContext:
        // sharing that one lets this race, or outlive, whatever else in the circuit is using
        // it -- which kills the circuit and takes the page with it.
        await using var db = await DbFactory.CreateDbContextAsync();

        if (_from is null || _to is null) return;

        var fromUtc = _from.Value.ToDateTime(TimeOnly.MinValue);
        var toUtcExclusive = _to.Value.AddDays(1).ToDateTime(TimeOnly.MinValue);

        var query = db.AuditLogEntries.Where(e => e.OccurredAtUtc >= fromUtc && e.OccurredAtUtc < toUtcExclusive);

        _totalCount = await query.CountAsync();
        _entries = await query.OrderByDescending(e => e.OccurredAtUtc).Take(MaxRows).ToListAsync();
    }

    private static string StatusClass(AuditAction action) => action switch
    {
        AuditAction.Delete or AuditAction.Deactivate => "flagged",
        AuditAction.Create or AuditAction.Reactivate => "cleared",
        _ => "pending",
    };
}
