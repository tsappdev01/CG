using Microsoft.EntityFrameworkCore;
using CGTOOL.Web.Data;
using CGTOOL.Web.Data.Governance;

namespace CGTOOL.Web.Components.Pages;

public partial class Dashboard
{
    private enum DeclarationCategory { InsiderTrading, ConflictOfInterest }

    /// <summary>One member's completion status for one declaration period/category. Both categories
    /// come from the same "who was actually notified" source as their respective Submission reports --
    /// DeclarationCycleRunRecipients for the run, cross-referenced against InsiderDeclarations /
    /// RelatedPartyCoiDeclarations for completion.</summary>
    private class ComplianceRow
    {
        public required int MemberId { get; init; }
        public required string MemberName { get; init; }
        public required string Email { get; init; }
        public string? EntityShortCode { get; init; }
        public string? CompanyName { get; init; }
        public required DeclarationCategory Category { get; init; }
        public required int Year { get; init; }
        public required int Quarter { get; init; }
        public required bool Complete { get; init; }
        public DateTime? SubmittedAtUtc { get; init; }
        public DateTime DueDateUtc { get; init; }
    }

    private List<ComplianceRow>? _rows;
    private List<DeclarationReminderLog>? _reminderLogs;
    private readonly HashSet<(int MemberId, DeclarationCategory Category, int Year, int Quarter)> _selected = [];
    private int _yearFilter;
    private int _quarterFilter;
    private string _categoryFilter = string.Empty;
    private string _statusFilter = string.Empty;
    private int _page = 1;
    private bool _sendingReminders;
    private const int PageSize = 25;

    protected override async Task OnInitializedAsync()
    {
        var state = await AuthState.GetAuthenticationStateAsync();
        if (GovernanceRoles.IsNormalStaffOnly(state.User))
        {
            Nav.NavigateTo("/my-declarations");
            return;
        }

        await LoadAsync();
    }

    private async Task LoadAsync()
    {
        await using var db = await DbFactory.CreateDbContextAsync();
        var rows = new List<ComplianceRow>();

        var insiderRecipients = await db.DeclarationCycleRunRecipients
            .AsNoTracking()
            .Include(r => r.DeclarationCycleRun)
            .Where(r => r.DeclarationCycleRun!.Type == DeclarationCycleType.InsiderTrading)
            .ToListAsync();
        var insiderDeclarations = await db.InsiderDeclarations
            .AsNoTracking()
            .ToDictionaryAsync(d => (d.MemberId, d.DeclarationCycleRunId));

        foreach (var r in insiderRecipients)
        {
            var run = r.DeclarationCycleRun!;
            insiderDeclarations.TryGetValue((r.MemberId, run.Id), out var declaration);
            rows.Add(new ComplianceRow
            {
                MemberId = r.MemberId,
                MemberName = r.MemberName,
                Email = r.Email,
                EntityShortCode = r.CompanyName,
                CompanyName = r.CompanyName,
                Category = DeclarationCategory.InsiderTrading,
                Year = run.PeriodYear,
                Quarter = run.PeriodQuarter,
                // A saved-but-not-submitted draft doesn't count as complete for compliance purposes.
                Complete = declaration is { IsDraft: false },
                SubmittedAtUtc = declaration is { IsDraft: false } ? declaration.SubmittedAtUtc : null,
                DueDateUtc = run.DueDateUtc,
            });
        }

        // Same source as the RP&COI Submission report: "who was actually notified" for a
        // ConflictOfInterest DeclarationCycleRun, cross-referenced against RelatedPartyCoiDeclarations
        // (the modern table that superseded DeclarationSetup/DeclarationSubmission).
        var coiRecipients = await db.DeclarationCycleRunRecipients
            .AsNoTracking()
            .Include(r => r.DeclarationCycleRun)
            .Where(r => r.DeclarationCycleRun!.Type == DeclarationCycleType.ConflictOfInterest)
            .ToListAsync();
        var coiDeclarations = await db.RelatedPartyCoiDeclarations
            .AsNoTracking()
            .ToDictionaryAsync(d => (d.MemberId, d.DeclarationCycleRunId));

        foreach (var r in coiRecipients)
        {
            var run = r.DeclarationCycleRun!;
            coiDeclarations.TryGetValue((r.MemberId, run.Id), out var declaration);
            rows.Add(new ComplianceRow
            {
                MemberId = r.MemberId,
                MemberName = r.MemberName,
                Email = r.Email,
                EntityShortCode = r.CompanyName,
                CompanyName = r.CompanyName,
                Category = DeclarationCategory.ConflictOfInterest,
                Year = run.PeriodYear,
                Quarter = run.PeriodQuarter,
                Complete = declaration is { IsDraft: false },
                SubmittedAtUtc = declaration is { IsDraft: false } ? declaration.SubmittedAtUtc : null,
                DueDateUtc = run.DueDateUtc,
            });
        }

        _rows = rows;

        _reminderLogs = await db.DeclarationReminderLogs
            .AsNoTracking()
            .OrderByDescending(l => l.SentAtUtc)
            .ToListAsync();

        _selected.Clear();
    }

    private List<int> AvailableYears() => (_rows ?? [])
        .Select(r => r.Year)
        .Distinct()
        .OrderByDescending(y => y)
        .ToList();

    // Respects the Year/Quarter filter only -- these totals ARE the category/status breakdown, so
    // filtering them by Category or Status too would make some tiles trivially show zero.
    private IEnumerable<ComplianceRow> RowsForPeriod() => (_rows ?? [])
        .Where(r => (_yearFilter == 0 || r.Year == _yearFilter) && (_quarterFilter == 0 || r.Quarter == _quarterFilter));

    private int InsiderPendingCount => RowsForPeriod().Count(r => r.Category == DeclarationCategory.InsiderTrading && !r.Complete);
    private int InsiderCompleteCount => RowsForPeriod().Count(r => r.Category == DeclarationCategory.InsiderTrading && r.Complete);
    private int CoiPendingCount => RowsForPeriod().Count(r => r.Category == DeclarationCategory.ConflictOfInterest && !r.Complete);
    private int CoiCompleteCount => RowsForPeriod().Count(r => r.Category == DeclarationCategory.ConflictOfInterest && r.Complete);

    private List<ComplianceRow> FilteredRows()
    {
        var query = RowsForPeriod();

        if (_categoryFilter == "insider") query = query.Where(r => r.Category == DeclarationCategory.InsiderTrading);
        else if (_categoryFilter == "coi") query = query.Where(r => r.Category == DeclarationCategory.ConflictOfInterest);

        if (_statusFilter == "complete") query = query.Where(r => r.Complete);
        else if (_statusFilter == "pending") query = query.Where(r => !r.Complete);

        return query
            .OrderByDescending(r => r.Year)
            .ThenByDescending(r => r.Quarter)
            .ThenBy(r => r.MemberName)
            .ToList();
    }

    private static string CategoryLabel(DeclarationCategory category) => category switch
    {
        DeclarationCategory.InsiderTrading => "Insider Trading",
        _ => "RP/COI",
    };

    private static DeclarationCycleType ToLogCategory(DeclarationCategory category) => category switch
    {
        DeclarationCategory.InsiderTrading => DeclarationCycleType.InsiderTrading,
        _ => DeclarationCycleType.ConflictOfInterest,
    };

    private int TotalPages => Math.Max(1, (int)Math.Ceiling(FilteredRows().Count / (double)PageSize));

    private List<ComplianceRow> PagedRows() => FilteredRows()
        .Skip((_page - 1) * PageSize)
        .Take(PageSize)
        .ToList();

    private void GoToPage(int page) => _page = Math.Clamp(page, 1, TotalPages);

    private void ResetToFirstPage() => _page = 1;

    // --- Reminders ---

    private static (int MemberId, DeclarationCategory Category, int Year, int Quarter) KeyOf(ComplianceRow r) => (r.MemberId, r.Category, r.Year, r.Quarter);

    private int ReminderCountFor(ComplianceRow r) => (_reminderLogs ?? [])
        .Count(l => l.MemberId == r.MemberId && l.Category == ToLogCategory(r.Category) && l.Year == r.Year && l.Quarter == r.Quarter);

    private DateTime? LastReminderAtFor(ComplianceRow r) => (_reminderLogs ?? [])
        .Where(l => l.MemberId == r.MemberId && l.Category == ToLogCategory(r.Category) && l.Year == r.Year && l.Quarter == r.Quarter)
        .Select(l => (DateTime?)l.SentAtUtc)
        .OrderByDescending(d => d)
        .FirstOrDefault();

    private bool IsSelected(ComplianceRow r) => _selected.Contains(KeyOf(r));

    private void ToggleSelected(ComplianceRow r, bool isChecked)
    {
        if (isChecked) _selected.Add(KeyOf(r));
        else _selected.Remove(KeyOf(r));
    }

    // "Select all" applies to every pending row matching the current filters (across all pages), not
    // just the ones currently visible on this page.
    private bool AllVisiblePendingSelected => FilteredRows().Where(r => !r.Complete).Any() && FilteredRows().Where(r => !r.Complete).All(IsSelected);

    private void ToggleSelectAll(bool isChecked)
    {
        foreach (var r in FilteredRows().Where(r => !r.Complete))
        {
            ToggleSelected(r, isChecked);
        }
    }

    private int SelectedCount => _selected.Count;

    private async Task SendRemindersAsync()
    {
        if (_selected.Count == 0 || _rows is null) return;

        _sendingReminders = true;
        try
        {
            var state = await AuthState.GetAuthenticationStateAsync();
            var actorName = state.User.Identity?.Name ?? "unknown";

            var targets = _rows.Where(r => !r.Complete && _selected.Contains(KeyOf(r))).ToList();

            foreach (var row in targets)
            {
                var label = CategoryLabel(row.Category);
                var subject = $"Reminder: {label} declaration due — Q{row.Quarter} {row.Year}";
                var body = $"Dear {row.MemberName},<br/><br/>" +
                           $"Our records show your {label} declaration for Q{row.Quarter} {row.Year} is still pending, due by {row.DueDateUtc.ToLocalDisplay("dd/MM/yyyy")}. " +
                           "Please log in and submit it at your earliest convenience.<br/><br/>Regards,<br/>Corporate Affairs";

                await EmailSender.SendAsync(row.Email, subject, body);

                await ReminderLogWriter.InsertAsync(new DeclarationReminderLog
                {
                    MemberId = row.MemberId,
                    MemberName = row.MemberName,
                    Email = row.Email,
                    CompanyName = row.CompanyName,
                    Category = ToLogCategory(row.Category),
                    Year = row.Year,
                    Quarter = row.Quarter,
                    SentByName = actorName,
                });

                await AuditLog.LogAsync(actorName, AuditAction.Notify, nameof(DeclarationReminderLog), row.MemberId.ToString(),
                    $"Sent {label} declaration reminder to {row.MemberName} (Q{row.Quarter} {row.Year})");
            }

            Toasts.ShowSuccess($"Reminder sent to {targets.Count} user(s).");
            await LoadAsync();
        }
        finally
        {
            _sendingReminders = false;
        }
    }
}
