using Microsoft.AspNetCore.Components;
using Microsoft.EntityFrameworkCore;
using Microsoft.JSInterop;
using CGTOOL.Web.Data;
using CGTOOL.Web.Data.Governance;

namespace CGTOOL.Web.Components.Pages.Admin;

public partial class DeclarationsSetupPage
{
    [Parameter] public string TypeSlug { get; set; } = string.Empty;

    private string _activeTab = "template";

    private DeclarationCycleSetup? _setup;
    private DeclarationCycleSetup? _original;
    private List<Company>? _companies;
    private List<DeclarationCycleRun>? _runs;
    private Dictionary<int, int> _submittedCounts = [];
    private string _sendCompanyId = string.Empty;
    private int _periodYear = DateTime.Today.Year;
    private int _periodQuarter = QuarterOf(DateTime.Today);
    private DateTime _dueDate = DateTime.Today.AddDays(15);
    private DateTime _scheduleSendDate = DateTime.Today.AddDays(1);
    private bool _sending;
    private bool _scheduling;
    private bool _showPrintPreview;
    private DeclarationCycleRun? _printSingleRecord;
    private string _sortColumn = "Sent";
    private bool _sortAscending = false;

    // History tab filters
    private int _historyYearFilter;
    private int _historyQuarterFilter;
    private int _historyCompanyFilter;
    private string _historyStatusFilter = string.Empty;

    private List<int> AvailableHistoryYears() => (_runs ?? [])
        .Select(r => r.PeriodYear)
        .Distinct()
        .OrderByDescending(y => y)
        .ToList();

    // A handful of years around today, regardless of what's in the history yet (unlike
    // AvailableHistoryYears, which only reflects runs that already exist).
    private static List<int> PeriodYearOptions()
    {
        var thisYear = DateTime.Today.Year;
        return Enumerable.Range(thisYear - 1, 4).Reverse().ToList();
    }

    // Send Now / Schedule Send confirmation
    private bool _pendingSendConfirm;
    private bool _pendingScheduleConfirm;
    private string _confirmMessage = string.Empty;
    private int? _pendingCompanyId;

    private static int QuarterOf(DateTime date) => ((date.Month - 1) / 3) + 1;

    private List<DeclarationCycleRunRecipient> PrintRecipients() => (_printSingleRecord is not null
        ? _printSingleRecord.Recipients.AsEnumerable()
        : FilteredRuns().Where(r => r.Sent).SelectMany(r => r.Recipients))
        .OrderByDescending(r => r.SentAtUtc)
        .ToList();

    private DeclarationCycleType Type => TypeSlug switch
    {
        "insider-trading" => DeclarationCycleType.InsiderTrading,
        "conflict-of-interest" => DeclarationCycleType.ConflictOfInterest,
        "related-party-register" => DeclarationCycleType.RelatedPartyRegister,
        "blackout-periods" => DeclarationCycleType.BlackoutPeriod,
        "scheduled-maintenance" => DeclarationCycleType.ScheduledMaintenance,
        _ => DeclarationCycleType.InsiderTrading,
    };

    private string PageHeading => Type switch
    {
        DeclarationCycleType.InsiderTrading => "Insider Trading — declaration setup",
        DeclarationCycleType.ConflictOfInterest => "Conflict of Interest — declaration setup",
        DeclarationCycleType.RelatedPartyRegister => "Related Party Register — declaration setup",
        DeclarationCycleType.BlackoutPeriod => "Blackout Periods — notification setup",
        DeclarationCycleType.ScheduledMaintenance => "Scheduled Maintenance — notification setup",
        _ => "Declarations setup",
    };

    protected override async Task OnParametersSetAsync()
    {
        var id = await SetupWriter.EnsureExistsAsync(Type);
        _setup = await Db.DeclarationCycleSetups.AsNoTracking().FirstAsync(s => s.Id == id);
        _original = Clone(_setup);

        _companies = await Db.Companies.Where(c => c.Active).OrderBy(c => c.Name).ToListAsync();

        await LoadRunsAsync();
        EnsureValidSelectedPeriod();
    }

    // AsNoTracking: this DbContext is scoped to the whole circuit, and both settings and run rows
    // are written via raw stored procedures, not EF's tracker -- without it, a later re-read here
    // (e.g. after Send Now updates a run through usp_DeclarationCycleRun_MarkSent) could return the
    // stale pre-update entity from EF's identity map instead of the fresh row.
    private async Task LoadRunsAsync()
    {
        _runs = await Db.DeclarationCycleRuns
            .AsNoTracking()
            .Include(r => r.Company)
            .Include(r => r.Recipients)
            .Where(r => r.Type == Type)
            .OrderByDescending(r => r.SentAtUtc)
            .ToListAsync();

        // Recall is only offered for the two declaration types that actually have a submission
        // concept (Related Party Register/Blackout/Scheduled Maintenance are notifications only, with
        // nothing to check "no submissions yet" against).
        _submittedCounts = Type switch
        {
            DeclarationCycleType.InsiderTrading => await Db.InsiderDeclarations
                .AsNoTracking()
                .Where(d => !d.IsDraft)
                .GroupBy(d => d.DeclarationCycleRunId)
                .Select(g => new { RunId = g.Key, Count = g.Count() })
                .ToDictionaryAsync(g => g.RunId, g => g.Count),
            DeclarationCycleType.ConflictOfInterest => await Db.RelatedPartyCoiDeclarations
                .AsNoTracking()
                .Where(d => !d.IsDraft)
                .GroupBy(d => d.DeclarationCycleRunId)
                .Select(g => new { RunId = g.Key, Count = g.Count() })
                .ToDictionaryAsync(g => g.RunId, g => g.Count),
            _ => [],
        };
    }

    private bool CanRecall(DeclarationCycleRun run)
    {
        if (Type != DeclarationCycleType.InsiderTrading && Type != DeclarationCycleType.ConflictOfInterest) return false;
        if (!run.Sent || run.Recalled) return false;
        return !_submittedCounts.TryGetValue(run.Id, out var count) || count == 0;
    }

    private List<DeclarationCycleRun> FilteredRuns()
    {
        if (_runs is null) return [];

        IEnumerable<DeclarationCycleRun> query = _runs;

        if (_historyYearFilter != 0) query = query.Where(r => r.PeriodYear == _historyYearFilter);
        if (_historyQuarterFilter != 0) query = query.Where(r => r.PeriodQuarter == _historyQuarterFilter);
        if (_historyCompanyFilter != 0) query = query.Where(r => r.CompanyId == _historyCompanyFilter);
        if (_historyStatusFilter == "sent") query = query.Where(r => r.Sent);
        else if (_historyStatusFilter == "scheduled") query = query.Where(r => !r.Sent);

        return query.ToList();
    }

    private List<DeclarationCycleRun> SortedRuns()
    {
        var runs = FilteredRuns();
        IOrderedEnumerable<DeclarationCycleRun> sorted = _sortColumn switch
        {
            "Entity" => runs.OrderBy(r => r.Company?.Name),
            "Due" => runs.OrderBy(r => r.DueDateUtc),
            "Recipients" => runs.OrderBy(r => r.RecipientCount),
            _ => runs.OrderBy(r => r.SentAtUtc),
        };
        return (_sortAscending ? sorted : sorted.Reverse()).ToList();
    }

    private void Sort(string column)
    {
        if (_sortColumn == column) _sortAscending = !_sortAscending;
        else { _sortColumn = column; _sortAscending = true; }
    }

    private async Task PrintAsync() => await JS.InvokeVoidAsync("print");

    private async Task SaveAsync()
    {
        if (_setup is null || _original is null) return;

        var changes = DiffChanges(_original, _setup);

        await SetupWriter.UpdateAsync(_setup);

        await AuditLog.LogAsync(await CurrentActorAsync(), AuditAction.Update, nameof(DeclarationCycleSetup), Type.ToString(),
            changes.Count > 0 ? $"{PageHeading} settings changed: {string.Join("; ", changes)}" : $"{PageHeading} settings saved (no field changes)");

        Toasts.ShowSuccess("Settings saved.");
        _original = Clone(_setup);
    }

    private static List<string> DiffChanges(DeclarationCycleSetup before, DeclarationCycleSetup after)
    {
        var changes = new List<string>();
        if (before.ReminderCount != after.ReminderCount) changes.Add($"Reminder count {before.ReminderCount} → {after.ReminderCount}");
        if (before.ReminderFrequency != after.ReminderFrequency) changes.Add($"Reminder frequency {before.ReminderFrequency} → {after.ReminderFrequency}");
        if (before.EmailSubject != after.EmailSubject) changes.Add($"Email subject '{before.EmailSubject}' → '{after.EmailSubject}'");
        if (before.EmailBody != after.EmailBody) changes.Add("Email template body changed");
        return changes;
    }

    private static DeclarationCycleSetup Clone(DeclarationCycleSetup s) => new()
    {
        Id = s.Id,
        Type = s.Type,
        ReminderCount = s.ReminderCount,
        ReminderFrequency = s.ReminderFrequency,
        EmailSubject = s.EmailSubject,
        EmailBody = s.EmailBody,
    };

    private string EntityLabel(int? companyId) =>
        companyId is { } cid ? _companies?.FirstOrDefault(c => c.Id == cid)?.Name ?? "selected entity" : "all entities";

    private DeclarationCycleRun? MostRecentSentRun(int? companyId) =>
        _runs?.Where(r => r.Sent && r.CompanyId == companyId).OrderByDescending(r => r.SentAtUtc).FirstOrDefault();

    // Quarters sort/compare as a single ascending index: Q1 2026 = 2026*4+0, Q2 2026 = 2026*4+1, etc.
    private static int PeriodIndex(int year, int quarter) => (year * 4) + (quarter - 1);

    // Notifications for this declaration Type must go out in period order -- once a later period has
    // been sent (to any entity), an earlier one can no longer be sent, so this is scoped across all
    // entities/companies rather than per-selected-entity. Only Sent runs count; a merely Scheduled
    // (not yet sent) later-period run doesn't block an earlier one until it actually goes out.
    private (int Year, int Quarter)? LatestSentPeriod()
    {
        var latest = (_runs ?? []).Where(r => r.Sent).OrderByDescending(r => PeriodIndex(r.PeriodYear, r.PeriodQuarter)).FirstOrDefault();
        return latest is null ? null : (latest.PeriodYear, latest.PeriodQuarter);
    }

    private bool IsQuarterDisabled(int quarter) =>
        LatestSentPeriod() is { } latest && PeriodIndex(_periodYear, quarter) < PeriodIndex(latest.Year, latest.Quarter);

    // Called after the Year select changes (and once on load) so the selected Quarter never stays
    // parked on an option that just became disabled -- advances to the period right after the most
    // recently sent one instead.
    private void EnsureValidSelectedPeriod()
    {
        if (LatestSentPeriod() is not { } latest) return;
        if (PeriodIndex(_periodYear, _periodQuarter) >= PeriodIndex(latest.Year, latest.Quarter)) return;

        var nextIndex = PeriodIndex(latest.Year, latest.Quarter) + 1;
        _periodYear = nextIndex / 4;
        _periodQuarter = (nextIndex % 4) + 1;
    }

    /// <summary>Converts a locally-picked calendar date (from an &lt;input type="date"&gt;) into the
    /// correct UTC instant for storage, at the given local hour -- treating the naive value as UTC
    /// directly (as the rest of the app previously did) would shift the stored moment by the server's
    /// UTC offset, firing "Schedule Send" at the wrong local time and, for negative offsets, even
    /// displaying the wrong calendar date for a plain due date.</summary>
    private static DateTime LocalDateToUtc(DateTime localDate, int hour = 0) =>
        DateTime.SpecifyKind(localDate.Date.AddHours(hour), DateTimeKind.Local).ToUniversalTime();

    private void PrepareSendNow()
    {
        if (_setup is null) return;

        if (IsQuarterDisabled(_periodQuarter))
        {
            Toasts.ShowError($"Q{_periodQuarter} {_periodYear} can't be sent: a later period has already been sent.");
            return;
        }

        var companyId = int.TryParse(_sendCompanyId, out var id) ? id : (int?)null;
        _pendingCompanyId = companyId;

        var entityLabel = EntityLabel(companyId);
        var priorRun = MostRecentSentRun(companyId);
        var periodLabel = $"Q{_periodQuarter} {_periodYear}";
        _confirmMessage = priorRun is not null
            ? $"A notification was already sent to {entityLabel} on {priorRun.SentAtUtc.ToLocalDisplay():dd MMM yyyy}. Send again now for {periodLabel}?"
            : $"Send this notification now to all active users in {entityLabel} for {periodLabel}?";

        _pendingSendConfirm = true;
    }

    private void CancelSendConfirm() => _pendingSendConfirm = false;

    private async Task ConfirmSendNowAsync()
    {
        _pendingSendConfirm = false;
        if (_setup is null) return;

        _sending = true;
        var recipients = await DeclarationCycleReminderHostedService.ResolveRecipientsAsync(Db, _pendingCompanyId, default);
        if (recipients.Count == 0)
        {
            Toasts.ShowError("No active users with an email address were found for that entity.");
            _sending = false;
            return;
        }

        var actor = await CurrentActorAsync();

        var run = new DeclarationCycleRun
        {
            DeclarationCycleSetupId = _setup.Id,
            Type = Type,
            CompanyId = _pendingCompanyId,
            SentAtUtc = DateTime.UtcNow,
            PeriodYear = _periodYear,
            PeriodQuarter = _periodQuarter,
            DueDateUtc = LocalDateToUtc(_dueDate),
            Sent = false,
        };
        run.Id = await RunWriter.InsertAsync(run);

        var (sentCount, failedCount) = await DeclarationCycleReminderHostedService.SendNotificationAsync(Db, EmailSender, RunWriter, AuditLog, run, _setup, actor);
        await RunWriter.MarkSentAsync(run.Id, DateTime.UtcNow, sentCount + failedCount);

        Toasts.ShowSuccess(failedCount == 0
            ? $"Sent to {sentCount} active user(s)."
            : $"Sent to {sentCount} active user(s); {failedCount} failed (see audit log).");
        _sending = false;
        await LoadRunsAsync();
    }

    private void PrepareScheduleSend()
    {
        if (_setup is null) return;

        if (IsQuarterDisabled(_periodQuarter))
        {
            Toasts.ShowError($"Q{_periodQuarter} {_periodYear} can't be sent: a later period has already been sent.");
            return;
        }

        if (_scheduleSendDate.Date <= DateTime.Today)
        {
            Toasts.ShowError("Pick a future date to schedule.");
            return;
        }

        if (_dueDate.Date < _scheduleSendDate.Date)
        {
            Toasts.ShowError("Due date must be on or after the scheduled send date.");
            return;
        }

        var companyId = int.TryParse(_sendCompanyId, out var id) ? id : (int?)null;
        _pendingCompanyId = companyId;

        var entityLabel = EntityLabel(companyId);
        var priorRun = MostRecentSentRun(companyId);
        var periodLabel = $"Q{_periodQuarter} {_periodYear}";
        _confirmMessage = priorRun is not null
            ? $"A notification was already sent to {entityLabel} on {priorRun.SentAtUtc.ToLocalDisplay():dd MMM yyyy}. Schedule another for {_scheduleSendDate:dd MMM yyyy} at 08:00 ({periodLabel})?"
            : $"Schedule this notification to send to {entityLabel} on {_scheduleSendDate:dd MMM yyyy} at 08:00 ({periodLabel})?";

        _pendingScheduleConfirm = true;
    }

    private void CancelScheduleConfirm() => _pendingScheduleConfirm = false;

    private async Task ConfirmScheduleSendAsync()
    {
        _pendingScheduleConfirm = false;
        if (_setup is null) return;

        _scheduling = true;

        var run = new DeclarationCycleRun
        {
            DeclarationCycleSetupId = _setup.Id,
            Type = Type,
            CompanyId = _pendingCompanyId,
            SentAtUtc = LocalDateToUtc(_scheduleSendDate, hour: 8),
            PeriodYear = _periodYear,
            PeriodQuarter = _periodQuarter,
            DueDateUtc = LocalDateToUtc(_dueDate),
            Sent = false,
        };
        await RunWriter.InsertAsync(run);

        var entityLabel = EntityLabel(_pendingCompanyId);
        await AuditLog.LogAsync(await CurrentActorAsync(), AuditAction.Create, nameof(DeclarationCycleRun), Type.ToString(),
            $"{PageHeading} scheduled to send {run.SentAtUtc.ToLocalDisplay():yyyy-MM-dd} 08:00 to {entityLabel}, due {_dueDate:yyyy-MM-dd}");

        Toasts.ShowSuccess($"Scheduled to send on {run.SentAtUtc.ToLocalDisplay():yyyy-MM-dd} at 08:00.");
        _scheduling = false;
        await LoadRunsAsync();
    }

    // Extend/adjust due date on an existing run (Send Now or Schedule Send have already fired) --
    // e.g. granting a late declarant more time without cancelling and resending the whole notification.
    private int? _editingDueDateRunId;
    private DateTime _editingDueDate;
    private bool _savingDueDate;

    private void StartEditDueDate(DeclarationCycleRun run)
    {
        _editingDueDateRunId = run.Id;
        _editingDueDate = run.DueDateUtc.ToLocalDisplay().Date;
    }

    private void CancelEditDueDate() => _editingDueDateRunId = null;

    private async Task SaveDueDateAsync(DeclarationCycleRun run)
    {
        if (_savingDueDate) return;
        _savingDueDate = true;

        try
        {
            var oldDueDateLabel = run.DueDateUtc.ToLocalDisplay("dd/MM/yyyy");
            var newDueDateUtc = LocalDateToUtc(_editingDueDate);

            await RunWriter.UpdateDueDateAsync(run.Id, newDueDateUtc);
            await AuditLog.LogAsync(await CurrentActorAsync(), AuditAction.Update, nameof(DeclarationCycleRun), Type.ToString(),
                $"{PageHeading} due date for Q{run.PeriodQuarter} {run.PeriodYear} ({EntityLabel(run.CompanyId)}) changed {oldDueDateLabel} → {newDueDateUtc.ToLocalDisplay("dd/MM/yyyy")}");

            Toasts.ShowSuccess("Due date updated.");
            _editingDueDateRunId = null;
            await LoadRunsAsync();
        }
        finally
        {
            _savingDueDate = false;
        }
    }

    private async Task CancelScheduledSendAsync(DeclarationCycleRun run)
    {
        if (run.Sent) return;

        await RunWriter.DeleteAsync(run.Id);
        await AuditLog.LogAsync(await CurrentActorAsync(), AuditAction.Delete, nameof(DeclarationCycleRun), Type.ToString(),
            $"Cancelled scheduled {PageHeading} send for {run.SentAtUtc.ToLocalDisplay():yyyy-MM-dd} 08:00");

        Toasts.ShowSuccess("Scheduled send cancelled.");
        await LoadRunsAsync();
    }

    // Recall (only offered while CanRecall(run) holds -- Sent, not already Recalled, and zero
    // submissions on record for it): notifies every original recipient by email that the
    // notification is withdrawn, then marks the run recalled so it stops counting as "due" anywhere
    // and stops receiving further reminders. The row itself is kept (not deleted) so History still
    // shows it happened.
    private DeclarationCycleRun? _pendingRecallRun;
    private bool _recalling;

    private void PrepareRecall(DeclarationCycleRun run) => _pendingRecallRun = run;

    private void CancelRecallConfirm() => _pendingRecallRun = null;

    private async Task ConfirmRecallAsync()
    {
        if (_pendingRecallRun is null || _recalling) return;
        var run = _pendingRecallRun;
        _pendingRecallRun = null;
        _recalling = true;

        try
        {
            var actor = await CurrentActorAsync();
            await DeclarationCycleReminderHostedService.RecallAsync(EmailSender, RunWriter, AuditLog, run, Type, actor);

            Toasts.ShowSuccess($"Recalled. {run.Recipients.Count} recipient(s) notified.");
            await LoadRunsAsync();
        }
        finally
        {
            _recalling = false;
        }
    }

    private async Task<string> CurrentActorAsync()
    {
        var state = await AuthState.GetAuthenticationStateAsync();
        return state.User.Identity?.Name ?? "unknown";
    }
}
