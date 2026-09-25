using Microsoft.JSInterop;
using Microsoft.EntityFrameworkCore;
using CGTOOL.Web.Data;
using CGTOOL.Web.Data.Governance;

namespace CGTOOL.Web.Components.Pages.Admin;

/// <summary>
/// The detailed audit log: what an SCA / ITGC reviewer is shown when they ask to see who did what,
/// to which record, when, from where, why, and who signed it off.
///
/// Risk and area are derived here rather than stored, so a change of policy is a change of code and
/// not a migration plus a backfill of history. The same is true of the control exceptions: they are
/// rules read off what was recorded, not flags written at the time.
/// </summary>
public partial class AuditLogDetailed
{
    /// <summary>The areas the mockup's pills filter by. Derived from action and entity.</summary>
    public enum AuditArea { Access, Change, Data, Config, Security }

    /// <summary>Ordered: comparisons below rely on Critical being the highest.</summary>
    public enum AuditRisk { Low, Medium, High, Critical }

    private List<AuditLogEntry>? _entries;
    private Dictionary<int, AuditLogReview> _reviews = [];
    private AuditChainStatus? _chain;
    private AuditLogEntry? _selected;

    private bool _verifying;
    private bool _signingOff;
    private string _reviewComment = string.Empty;

    // Filters
    private DateOnly _from = DateOnly.FromDateTime(DateTime.Today.AddDays(-30));
    private DateOnly _to = DateOnly.FromDateTime(DateTime.Today);
    private string _search = string.Empty;
    private string _riskFilter = "all";
    private AuditArea? _areaFilter;
    private bool _privilegedOnly;
    private bool _awaitingReviewOnly;
    private int _page = 1;
    private int _pageSize = 25;

    protected override async Task OnInitializedAsync()
    {
        await LoadAsync();
        _chain = await Integrity.VerifyAsync();
    }

    private async Task LoadAsync()
    {
        // Its own short-lived context rather than the circuit-scoped one, as elsewhere.
        await using var db = await DbFactory.CreateDbContextAsync();

        // The range is inclusive of both days; To is taken to mean the end of that day.
        var fromUtc = _from.ToDateTime(TimeOnly.MinValue);
        var toUtc = _to.ToDateTime(TimeOnly.MaxValue);

        _entries = await db.AuditLogEntries
            .Where(a => a.OccurredAtUtc >= fromUtc && a.OccurredAtUtc <= toUtc)
            .OrderByDescending(a => a.OccurredAtUtc)
            .ThenByDescending(a => a.Id)
            .ToListAsync();

        var ids = _entries.Select(e => e.Id).ToList();
        _reviews = await db.AuditLogReviews
            .Where(r => ids.Contains(r.AuditLogEntryId))
            .ToDictionaryAsync(r => r.AuditLogEntryId);

        if (_selected is not null) _selected = _entries.FirstOrDefault(e => e.Id == _selected.Id);
        _selected ??= _entries.FirstOrDefault();
    }

    private async Task ApplyRangeAsync()
    {
        _page = 1;
        await LoadAsync();
    }

    // ---------------------------------------------------------------- derivation

    /// <summary>Which control family an event belongs to. Access is who can do what, Config is how
    /// the application is set up, Data is bulk movement in or out, Security is authentication;
    /// everything else is an ordinary change to a record.</summary>
    public static AuditArea AreaOf(AuditLogEntry e) => e.Action switch
    {
        AuditAction.RoleChange => AuditArea.Access,
        AuditAction.ImpersonationStart or AuditAction.ImpersonationEnd => AuditArea.Access,
        _ when e.EntityType.StartsWith("NavMenuItem", StringComparison.Ordinal) => AuditArea.Config,
        _ when e.EntityType is "PolicyDocumentVersion" or "NavMenuItem" => AuditArea.Config,
        _ when e.EntityType is "ShareholderRecord" or "ShareTradingRecord" => AuditArea.Data,
        _ when e.EntityId is "EntraSync" or "DirectoryImport" => AuditArea.Data,
        _ => AuditArea.Change,
    };

    /// <summary>
    /// How much attention an event deserves. Impersonation and role changes are the two that let
    /// somebody act as, or grant themselves, more than they had -- they rank highest regardless of
    /// what was touched. Removing things outranks adding them, since a deletion is what an auditor
    /// cannot reconstruct from the record.
    /// </summary>
    public static AuditRisk RiskOf(AuditLogEntry e) => e.Action switch
    {
        AuditAction.ImpersonationStart => AuditRisk.Critical,
        AuditAction.RoleChange when (e.Details ?? string.Empty).Contains("Administrator", StringComparison.OrdinalIgnoreCase) => AuditRisk.Critical,
        AuditAction.RoleChange => AuditRisk.High,
        AuditAction.Delete => AuditRisk.High,
        AuditAction.Deactivate => AuditRisk.High,
        AuditAction.ImpersonationEnd => AuditRisk.Medium,
        AuditAction.Create or AuditAction.Update or AuditAction.Reactivate => AuditRisk.Medium,
        _ => AuditRisk.Low,
    };

    /// <summary>An action that changes who can do what, or removes something. These are the ones a
    /// reviewer is required to look at, and the ones that must carry a reason.</summary>
    public static bool IsPrivileged(AuditLogEntry e) => e.Action is
        AuditAction.RoleChange or AuditAction.Delete or AuditAction.Deactivate
        or AuditAction.ImpersonationStart or AuditAction.ImpersonationEnd;

    /// <summary>
    /// Rules read off what was recorded. Deliberately not stored at the time: a control that was
    /// not being checked when an event happened still ought to surface against it now.
    /// </summary>
    public static List<string> ExceptionsOf(AuditLogEntry e)
    {
        var exceptions = new List<string>();

        if (IsPrivileged(e) && string.IsNullOrWhiteSpace(e.Justification))
            exceptions.Add("Privileged change recorded without justification");

        if (e.ActingOnBehalfOf is not null)
            exceptions.Add($"Action taken while impersonating {e.ActingOnBehalfOf}");

        if (string.IsNullOrWhiteSpace(e.IpAddress))
            exceptions.Add("No source address recorded for this event");

        if (e.Sequence is null)
            exceptions.Add("Record predates the tamper-evident chain and was sealed retrospectively");

        return exceptions;
    }

    private AuditLogReview? ReviewOf(AuditLogEntry e) => _reviews.GetValueOrDefault(e.Id);

    private bool AwaitingReview(AuditLogEntry e) => IsPrivileged(e) && ReviewOf(e) is null;

    // ---------------------------------------------------------------- filtering

    private List<AuditLogEntry> Visible()
    {
        IEnumerable<AuditLogEntry> rows = _entries ?? [];

        if (_areaFilter is { } area) rows = rows.Where(e => AreaOf(e) == area);
        if (_privilegedOnly) rows = rows.Where(IsPrivileged);
        if (_awaitingReviewOnly) rows = rows.Where(AwaitingReview);

        if (_riskFilter != "all" && Enum.TryParse<AuditRisk>(_riskFilter, out var risk))
            rows = rows.Where(e => RiskOf(e) == risk);

        if (!string.IsNullOrWhiteSpace(_search))
        {
            var term = _search.Trim();
            rows = rows.Where(e =>
                e.ActorDisplayName.Contains(term, StringComparison.OrdinalIgnoreCase)
                || e.EntityType.Contains(term, StringComparison.OrdinalIgnoreCase)
                || (e.EntityId ?? string.Empty).Contains(term, StringComparison.OrdinalIgnoreCase)
                || (e.Details ?? string.Empty).Contains(term, StringComparison.OrdinalIgnoreCase)
                || (e.Justification ?? string.Empty).Contains(term, StringComparison.OrdinalIgnoreCase)
                || EventRef(e).Contains(term, StringComparison.OrdinalIgnoreCase));
        }

        return rows.ToList();
    }

    private List<AuditLogEntry> Paged() => Visible().Skip((_page - 1) * _pageSize).Take(_pageSize).ToList();

    private bool HasActiveFilters =>
        !string.IsNullOrWhiteSpace(_search) || _riskFilter != "all" || _areaFilter is not null
        || _privilegedOnly || _awaitingReviewOnly;

    private void ClearFilters()
    {
        _search = string.Empty;
        _riskFilter = "all";
        _areaFilter = null;
        _privilegedOnly = false;
        _awaitingReviewOnly = false;
        _page = 1;
    }

    private void SetArea(AuditArea? area)
    {
        _areaFilter = area;
        _page = 1;
    }

    /// <summary>The reference an auditor quotes. Built from the chain position, so it is stable and
    /// means something -- a row with no sequence has not been sealed and says so.</summary>
    public static string EventRef(AuditLogEntry e) =>
        e.Sequence is { } seq ? $"AUD-{seq:000000}" : $"AUD-unsealed-{e.Id}";

    // ---------------------------------------------------------------- actions

    private async Task VerifyAsync()
    {
        if (_verifying) return;
        _verifying = true;
        try
        {
            _chain = await Integrity.VerifyAsync();

            if (_chain.Unsealed > 0)
            {
                var sealedNow = await Integrity.BackfillAsync();
                _chain = await Integrity.VerifyAsync();
                Toasts.ShowWarning($"{sealedNow} record(s) predating the chain were sealed now. They are protected from this point onwards, not before.");
            }

            if (_chain.Verified) Toasts.ShowSuccess($"Chain verified — {_chain.Sealed} record(s), no gaps, nothing altered.");
            else if (_chain.Problems.Count > 0) Toasts.ShowError($"{_chain.Problems.Count} problem(s) found. The log has been altered.");

            await LoadAsync();
        }
        finally
        {
            _verifying = false;
        }
    }

    private async Task SignOffAsync(AuditReviewOutcome outcome)
    {
        if (_selected is null || _signingOff) return;

        if (string.IsNullOrWhiteSpace(_reviewComment))
        {
            Toasts.ShowError("A reviewer comment is required.");
            return;
        }

        _signingOff = true;
        try
        {
            var state = await AuthState.GetAuthenticationStateAsync();
            var reviewer = state.User.Identity?.Name ?? "unknown";

            await ReviewWriter.UpsertAsync(_selected.Id, reviewer, outcome, _reviewComment.Trim());

            Toasts.ShowSuccess(outcome == AuditReviewOutcome.Reviewed
                ? $"{EventRef(_selected)} signed off as reviewed."
                : $"{EventRef(_selected)} raised as an exception.");

            _reviewComment = string.Empty;
            await LoadAsync();
        }
        finally
        {
            _signingOff = false;
        }
    }

    /// <summary>
    /// The evidence pack: the events on screen, with their seals, as a file an auditor can keep.
    /// The hashes are the point -- handed over with the rows, they let someone check later that
    /// what they were given is what the system holds.
    /// </summary>
    private async Task ExportAsync()
    {
        var rows = Visible();
        var csv = new System.Text.StringBuilder();

        csv.AppendLine("Reference,Sequence,OccurredUtc,Actor,OnBehalfOf,Action,Area,Risk,EntityType,EntityId,Details,Justification,IpAddress,UserAgent,Review,Reviewer,ReviewedUtc,ReviewComment,PreviousHash,RecordHash");

        foreach (var e in rows)
        {
            var review = ReviewOf(e);
            csv.AppendLine(string.Join(',', new[]
            {
                EventRef(e), e.Sequence?.ToString() ?? string.Empty,
                e.OccurredAtUtc.ToString("O"), e.ActorDisplayName, e.ActingOnBehalfOf ?? string.Empty,
                e.Action.ToString(), AreaOf(e).ToString(), RiskOf(e).ToString(),
                e.EntityType, e.EntityId ?? string.Empty, e.Details ?? string.Empty, e.Justification ?? string.Empty,
                e.IpAddress ?? string.Empty, e.UserAgent ?? string.Empty,
                review?.Outcome.ToString() ?? (AwaitingReview(e) ? "Awaiting" : "Not required"),
                review?.ReviewerName ?? string.Empty, review?.ReviewedAtUtc.ToString("O") ?? string.Empty,
                review?.Comment ?? string.Empty,
                e.PreviousHash ?? string.Empty, e.RecordHash ?? string.Empty,
            }.Select(Csv)));
        }

        // The same helper the RP Transaction Register export uses, and a BOM so Excel opens a file
        // of Arabic names and em dashes as UTF-8 rather than mangling it.
        var name = $"audit-evidence-{_from:yyyyMMdd}-{_to:yyyyMMdd}.csv";
        var bytes = System.Text.Encoding.UTF8.GetPreamble().Concat(System.Text.Encoding.UTF8.GetBytes(csv.ToString())).ToArray();
        await JS.InvokeVoidAsync("downloadFileFromBase64", name, "text/csv", Convert.ToBase64String(bytes));

        var actor = (await AuthState.GetAuthenticationStateAsync()).User.Identity?.Name ?? "unknown";
        await AuditLog.LogAsync(actor, AuditAction.Update, "AuditLog", "EvidencePack",
            $"Exported {rows.Count} audit record(s) for {_from:dd/MM/yyyy}-{_to:dd/MM/yyyy}");

        await LoadAsync();
    }

    /// <summary>The reference for a chain position, for naming a problem the verify reported.</summary>
    private static string EventRefFor(long sequence) => $"AUD-{sequence:000000}";

    /// <summary>Quotes a field, and doubles any quote inside it, so a comma or a newline in a
    /// justification cannot shift every later column.</summary>
    private static string Csv(string value) => $"\"{value.Replace("\"", "\"\"")}\"";
}
