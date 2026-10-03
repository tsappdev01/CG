using CGTOOL.Web.Data;
using CGTOOL.Web.Data.Governance;
using CGTOOL.Web.Data.Reports;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.EntityFrameworkCore;

namespace CGTOOL.Web.Components.Pages.Admin;

/// <summary>The audit trail for someone who has to review it rather than investigate with it.
///
/// The Detailed Log already exists and is the right screen for IT: sequence numbers, hashes, user
/// agents, IP addresses, a record at a time. This is the other audience. A compliance reviewer
/// signing off a quarter needs to read what happened, notice anything that looks wrong, and put
/// their name to it -- and none of those are helped by a column of SHA-256.
///
/// So: a day to a page, in sentences, with the technical machinery left out and its *conclusion*
/// stated in words at the top -- whether anything in the period has been altered or removed. The
/// reviewer is being asked to trust the record; they are owed a plain answer about whether they
/// can.</summary>
public partial class AuditTrailReport
{
    [Inject] private IDbContextFactory<ApplicationDbContext> DbFactory { get; set; } = default!;
    [Inject] private AuthenticationStateProvider AuthState { get; set; } = default!;
    [Inject] private IAuditLogIntegrity Integrity { get; set; } = default!;

    private List<AuditLogEntry>? _entries;
    private AuditChainStatus? _chain;
    private string _preparedBy = "unknown";

    private string _search = string.Empty;
    private DateTime? _from;
    private DateTime? _to;
    private string _personFilter = string.Empty;
    private string _areaFilter = string.Empty;
    private string _changeFilter = string.Empty;

    protected override async Task OnInitializedAsync()
    {
        var state = await AuthState.GetAuthenticationStateAsync();
        _preparedBy = state.User.Identity?.Name ?? "unknown";

        await using var db = await DbFactory.CreateDbContextAsync();
        _entries = await db.AuditLogEntries
            .AsNoTracking()
            .OrderByDescending(e => e.OccurredAtUtc)
            .ToListAsync();

        // The integrity check is the one piece of the technical machinery this report keeps, and it
        // keeps only the answer. Asked once on load rather than per render: it walks the whole chain.
        _chain = await Integrity.VerifyAsync();

        // Open on the last full month, which is the period a review is normally run for.
        var today = DateTime.Today;
        var firstOfThisMonth = new DateTime(today.Year, today.Month, 1);
        _from = firstOfThisMonth.AddMonths(-1);
        _to = firstOfThisMonth.AddDays(-1);
    }

    private List<string> People() =>
        [.. (_entries ?? []).Select(e => e.ActorDisplayName).Where(n => !string.IsNullOrWhiteSpace(n)).Distinct().OrderBy(n => n)];

    private List<AuditLogEntry> Filtered()
    {
        var rows = (_entries ?? []).AsEnumerable();

        if (_from is { } from) rows = rows.Where(e => e.OccurredAtUtc.ToLocalTime().Date >= from.Date);
        if (_to is { } to) rows = rows.Where(e => e.OccurredAtUtc.ToLocalTime().Date <= to.Date);

        if (_personFilter is { Length: > 0 }) rows = rows.Where(e => e.ActorDisplayName == _personFilter);
        if (_areaFilter is { Length: > 0 }) rows = rows.Where(e => AuditTrailLanguage.Area(e.EntityType) == _areaFilter);

        rows = _changeFilter switch
        {
            "added" => rows.Where(e => e.Action == AuditAction.Create),
            "changed" => rows.Where(e => e.Action == AuditAction.Update),
            "removed" => rows.Where(e => e.Action is AuditAction.Delete or AuditAction.Deactivate),
            "access" => rows.Where(e => e.Action is AuditAction.RoleChange or AuditAction.AccessDenied
                                                 or AuditAction.ImpersonationStart or AuditAction.ImpersonationEnd),
            "behalf" => rows.Where(e => e.ActingOnBehalfOf is { Length: > 0 }),
            _ => rows,
        };

        if (_search.Trim() is { Length: > 0 } term)
        {
            rows = rows.Where(e =>
                e.ActorDisplayName.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                (e.ActingOnBehalfOf ?? "").Contains(term, StringComparison.OrdinalIgnoreCase) ||
                (e.Details ?? "").Contains(term, StringComparison.OrdinalIgnoreCase) ||
                (e.Justification ?? "").Contains(term, StringComparison.OrdinalIgnoreCase) ||
                AuditTrailLanguage.Record(e.EntityType).Contains(term, StringComparison.OrdinalIgnoreCase));
        }

        return [.. rows];
    }

    private ReportDocument Build()
    {
        var rows = Filtered();

        // Newest day first, and within a day the earliest first -- a reviewer reads a day forwards.
        var days = rows
            .GroupBy(e => e.OccurredAtUtc.ToLocalTime().Date)
            .OrderByDescending(g => g.Key)
            .ToList();

        return new ReportDocument
        {
            Title = "Audit Trail",
            Subtitle = IntegritySentence(),
            SubtitleTone = _chain is null || !_chain.Verified ? ReportTone.Warning : ReportTone.Normal,
            PreparedBy = _preparedBy,
            Stats =
            [
                new("Days with activity", days.Count.ToString(), ""),
                new("Things people did", rows.Count.ToString(), "in the period"),
                new("People involved", rows.Select(e => e.ActorDisplayName).Distinct().Count().ToString(), ""),
                new("Removed or made inactive", rows.Count(e => e.Action is AuditAction.Delete or AuditAction.Deactivate).ToString(), "worth a look"),
                new("Entry refused", rows.Count(e => e.Action == AuditAction.AccessDenied).ToString(), "sign-ins"),
            ],
            FilterStatement = FilterStatement(),
            Records = [.. days.Select(Day)],
        };
    }

    /// <summary>What the tamper-evidence found, said in a sentence rather than shown as a hash.
    /// The reviewer is being asked to put their name to this record; the one thing they need to
    /// know about how it is stored is whether it can be trusted.</summary>
    private string IntegritySentence()
    {
        if (_chain is null) return "The completeness of this record has not been checked.";

        if (_chain.Verified)
            return $"This record is complete and unaltered — all {_chain.Sealed:N0} entries are accounted for, and none has been edited or removed since it was written.";

        var problems = new List<string>();
        if (_chain.Gaps > 0) problems.Add($"{_chain.Gaps} entr{(_chain.Gaps == 1 ? "y appears" : "ies appear")} to have been removed");
        var altered = _chain.Problems.Count - _chain.Gaps;
        if (altered > 0) problems.Add($"{altered} entr{(altered == 1 ? "y appears" : "ies appear")} to have been edited after being written");
        if (_chain.Unsealed > 0) problems.Add($"{_chain.Unsealed} entr{(_chain.Unsealed == 1 ? "y has" : "ies have")} not yet been sealed");

        return problems.Count == 0
            ? "The completeness of this record could not be confirmed."
            : "WARNING — this record may not be complete: " + string.Join("; ", problems) +
              ". Report this to IT before signing anything off.";
    }

    /// <summary>One day, one sheet. A reviewer works through a period a day at a time and signs off
    /// a day at a time, and a day that runs over a page break is harder to account for than a short
    /// page.</summary>
    private static ReportRecord Day(IGrouping<DateTime, AuditLogEntry> day)
    {
        var entries = day.OrderBy(e => e.OccurredAtUtc).ToList();
        var people = entries.Select(e => e.ActorDisplayName).Distinct().Count();
        var removals = entries.Count(e => e.Action is AuditAction.Delete or AuditAction.Deactivate);
        var refused = entries.Count(e => e.Action == AuditAction.AccessDenied);
        var onBehalf = entries.Count(e => e.ActingOnBehalfOf is { Length: > 0 });

        var facts = new List<ReportFact>
        {
            new("Things done", entries.Count.ToString()),
            new("People involved", people.ToString()),
            new("First thing done at", entries[0].OccurredAtUtc.ToLocalDisplay("HH:mm")),
            new("Last thing done at", entries[^1].OccurredAtUtc.ToLocalDisplay("HH:mm")),
        };

        // These three only appear on a day that has them. A row of zeroes on every page trains the
        // reader to skip the block that is meant to catch their eye.
        if (removals > 0) facts.Add(new("Removed or made inactive", removals.ToString()));
        if (refused > 0) facts.Add(new("Sign-ins refused", refused.ToString()));
        if (onBehalf > 0) facts.Add(new("Done on someone's behalf", onBehalf.ToString()));

        return new ReportRecord(
            day.Key.ToString("dddd, d MMMM yyyy"),
            $"{entries.Count} thing{(entries.Count == 1 ? "" : "s")} done by {people} {(people == 1 ? "person" : "people")}.",
            facts,
            [
                new ReportSubTable(
                    "What happened, in order",
                    [new("Time", 7), new("Who", 17), new("Change", 11), new("Record", 13), new("What happened", 39), new("Why", 13)],
                    [.. entries.Select(e => new[]
                    {
                        e.OccurredAtUtc.ToLocalDisplay("HH:mm"),
                        AuditTrailLanguage.Who(e),
                        AuditTrailLanguage.Change(e.Action),
                        AuditTrailLanguage.Record(e.EntityType),
                        AuditTrailLanguage.What(e),
                        // A reason is only required for removals and access changes, so most rows
                        // have none. "—" says the system did not ask for one, which is different
                        // from somebody declining to give one.
                        e.Justification is { Length: > 0 } why ? why : "—",
                    })],
                    "Nothing was recorded on this day."),
            ]);
    }

    private string FilterStatement()
    {
        var parts = new List<string>
        {
            $"{(_from is { } f ? f.ToString("dd/MM/yyyy") : "the beginning")} to {(_to is { } t ? t.ToString("dd/MM/yyyy") : "today")}",
            _personFilter is { Length: > 0 } ? _personFilter : "Anyone",
            _areaFilter is { Length: > 0 } ? _areaFilter : "Everything",
            _changeFilter switch
            {
                "added" => "Things added",
                "changed" => "Things changed",
                "removed" => "Things removed or made inactive",
                "access" => "Access changes and refusals",
                "behalf" => "Done on someone's behalf",
                _ => "Every kind of change",
            },
        };

        if (_search.Trim() is { Length: > 0 } term) parts.Add($"Search “{term}”");
        return string.Join(" · ", parts);
    }
}
