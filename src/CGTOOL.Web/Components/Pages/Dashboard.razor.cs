using Microsoft.EntityFrameworkCore;
using Microsoft.JSInterop;
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

    /// <summary>What one of the two category cards says. Pending and Overdue are disjoint -- a row
    /// past its due date is counted as overdue and not also as pending, so the three numbers add up
    /// to the total and the bar can be read straight across.</summary>
    private record CategoryCard(string Title, int Total, int Complete, int Pending, int Overdue)
    {
        public int Percent => Total == 0 ? 0 : (int)Math.Round(Complete * 100.0 / Total);
        public double CompleteWidth => Total == 0 ? 0 : Complete * 100.0 / Total;
        public double OverdueWidth => Total == 0 ? 0 : Overdue * 100.0 / Total;
    }

    private List<ComplianceRow>? _rows;
    private List<DeclarationReminderLog>? _reminderLogs;
    /// <summary>How many reminders each category is configured to send, for the "n of N" on each row.</summary>
    private Dictionary<DeclarationCycleType, int> _reminderTargets = [];
    private string _search = string.Empty;
    private bool _logOpen;
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

        _reminderTargets = await db.DeclarationCycleSetups
            .AsNoTracking()
            .ToDictionaryAsync(s => s.Type, s => s.ReminderCount);

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

    /// <summary>Pending and past its due date. Read against UAE time, the same clock the due dates
    /// and the notification schedule are set in.</summary>
    private static bool IsOverdue(ComplianceRow r) => !r.Complete && r.DueDateUtc < DateTime.UtcNow;

    private static string StatusOf(ComplianceRow r) => r.Complete ? "Complete" : IsOverdue(r) ? "Overdue" : "Pending";

    private List<CategoryCard> Cards()
    {
        var rows = RowsForPeriod().ToList();
        return
        [
            CardFor("Insider Trading", rows.Where(r => r.Category == DeclarationCategory.InsiderTrading)),
            CardFor("Related Party & COI", rows.Where(r => r.Category == DeclarationCategory.ConflictOfInterest)),
        ];
    }

    private static CategoryCard CardFor(string title, IEnumerable<ComplianceRow> rows)
    {
        var list = rows.ToList();
        var overdue = list.Count(IsOverdue);
        return new CategoryCard(title, list.Count, list.Count(r => r.Complete), list.Count(r => !r.Complete) - overdue, overdue);
    }

    /// <summary>The soonest due date still ahead of us in the selected period -- what the header
    /// counts down to. Null once everything in scope is past due or there is nothing in scope.</summary>
    private DateTime? NextDueDateUtc => RowsForPeriod()
        .Where(r => !r.Complete && r.DueDateUtc >= DateTime.UtcNow)
        .Select(r => (DateTime?)r.DueDateUtc)
        .OrderBy(d => d)
        .FirstOrDefault();

    private int? DaysUntilDue => NextDueDateUtc is { } due ? (int)Math.Ceiling((UaeTime.FromUtc(due).Date - UaeTime.Now.Date).TotalDays) : null;

    /// <summary>Reminders sent for the period on screen, however they were sent -- from this table or
    /// by the scheduler.</summary>
    private int RemindersForPeriod => (_reminderLogs ?? [])
        .Count(l => (_yearFilter == 0 || l.Year == _yearFilter) && (_quarterFilter == 0 || l.Quarter == _quarterFilter));

    private int CountByStatus(string status) => RowsForPeriod().Count(r => StatusOf(r).Equals(status, StringComparison.OrdinalIgnoreCase));

    private bool AnyFilterApplied =>
        _search.Length > 0 || _yearFilter != 0 || _quarterFilter != 0 || _categoryFilter.Length > 0 || _statusFilter.Length > 0;

    private void ClearFilters()
    {
        _search = string.Empty;
        _yearFilter = 0;
        _quarterFilter = 0;
        _categoryFilter = string.Empty;
        _statusFilter = string.Empty;
        ResetToFirstPage();
    }

    private void PickStatus(string status)
    {
        _statusFilter = _statusFilter == status ? string.Empty : status;
        ResetToFirstPage();
    }

    /// <summary>Two letters for the row avatar: first and last word of the name, so "Khalid Al
    /// Mansoori" reads KM rather than KA.</summary>
    private static string InitialsOf(string name)
    {
        var parts = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return "?";
        return parts.Length == 1
            ? parts[0][..1].ToUpperInvariant()
            : $"{parts[0][0]}{parts[^1][0]}".ToUpperInvariant();
    }

    private int ReminderTargetFor(ComplianceRow r) =>
        _reminderTargets.TryGetValue(ToLogCategory(r.Category), out var n) ? n : 0;

    private List<ComplianceRow> FilteredRows()
    {
        var query = RowsForPeriod();

        if (_categoryFilter == "insider") query = query.Where(r => r.Category == DeclarationCategory.InsiderTrading);
        else if (_categoryFilter == "coi") query = query.Where(r => r.Category == DeclarationCategory.ConflictOfInterest);

        if (_search.Length > 0)
        {
            var term = _search.Trim();
            query = query.Where(r =>
                r.MemberName.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                r.Email.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                (r.CompanyName ?? string.Empty).Contains(term, StringComparison.OrdinalIgnoreCase));
        }

        if (_statusFilter.Length > 0) query = query.Where(r => StatusOf(r).Equals(_statusFilter, StringComparison.OrdinalIgnoreCase));

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

        var targets = _rows.Where(r => !r.Complete && _selected.Contains(KeyOf(r))).ToList();
        await SendRemindersAsync(targets);
    }

    /// <summary>The per-row "Remind" button: the same send as the bulk action, for one person, so a
    /// single chaser does not mean selecting a checkbox and clearing it again afterwards.</summary>
    private Task RemindOneAsync(ComplianceRow row) => SendRemindersAsync([row]);

    private async Task SendRemindersAsync(List<ComplianceRow> targets)
    {
        if (targets.Count == 0) return;

        _sendingReminders = true;
        try
        {
            var state = await AuthState.GetAuthenticationStateAsync();
            var actorName = state.User.Identity?.Name ?? "unknown";

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

            Toasts.ShowSuccess(targets.Count == 1
                ? $"Reminder sent to {targets[0].MemberName}."
                : $"Reminder sent to {targets.Count} user(s).");
            await LoadAsync();
        }
        finally
        {
            _sendingReminders = false;
        }
    }

    private async Task RefreshAsync()
    {
        await LoadAsync();
        Toasts.ShowSuccess("Dashboard refreshed.");
    }

    /// <summary>Exports what is on screen -- the filtered rows, not the whole table -- so the export
    /// matches the question the filters were set to answer.</summary>
    private async Task ExportCsvAsync()
    {
        var rows = FilteredRows();
        var csv = new System.Text.StringBuilder();
        csv.AppendLine("User,Email,Entity,Category,Period,Status,Submitted,Reminders sent,Last reminder,Due date");

        foreach (var r in rows)
        {
            csv.AppendLine(string.Join(",", new[]
            {
                r.MemberName, r.Email, r.CompanyName ?? string.Empty, CategoryLabel(r.Category),
                $"Q{r.Quarter} {r.Year}", StatusOf(r),
                r.SubmittedAtUtc?.ToLocalDisplay("dd/MM/yyyy HH:mm") ?? string.Empty,
                ReminderCountFor(r).ToString(),
                LastReminderAtFor(r)?.ToLocalDisplay("dd/MM/yyyy HH:mm") ?? string.Empty,
                r.DueDateUtc.ToLocalDisplay("dd/MM/yyyy"),
            }.Select(Csv)));
        }

        // A BOM so Excel opens a file of Arabic names and em dashes as UTF-8 rather than mangling it.
        var name = $"declaration-compliance-{DateTime.UtcNow:yyyyMMdd}.csv";
        var bytes = System.Text.Encoding.UTF8.GetPreamble()
            .Concat(System.Text.Encoding.UTF8.GetBytes(csv.ToString()))
            .ToArray();
        await JS.InvokeVoidAsync("downloadFileFromBase64", name, "text/csv", Convert.ToBase64String(bytes));

        var actor = (await AuthState.GetAuthenticationStateAsync()).User.Identity?.Name ?? "unknown";
        await AuditLog.LogAsync(actor, AuditAction.Update, "Dashboard", "ComplianceExport",
            $"Exported {rows.Count} declaration compliance record(s)");
    }

    private static string Csv(string value) => $"\"{value.Replace("\"", "\"\"")}\"";
}
