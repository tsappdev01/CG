using CGTOOL.Web.Data;
using CGTOOL.Web.Data.Governance;
using CGTOOL.Web.Data.Reports;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.EntityFrameworkCore;

namespace CGTOOL.Web.Components.Pages.Admin;

/// <summary>What insiders said they bought and sold, against what the DFM recorded on the same
/// investor number over the same quarter.
///
/// The Insider Trading report shows the exchange's movements and marks which belong to a declared
/// insider. The Insider Detail report shows what someone declared. Neither answers the compliance
/// question, which is whether the two agree -- and they are two different sources: one a statement
/// a person signed, the other a file the exchange produced.
///
/// Matched on the NIN and nothing else. A name can be spelt two ways and a person can declare on
/// behalf of a relative, but the investor number is the same string on both sides, which is why
/// the declaration asks for it for every holder.
///
/// Quantities only. The declaration carries a rate per transaction and the DFM upload carries no
/// price at all, so there is nothing to compare a rate against; a value column would be the
/// declarant's own figure printed twice.</summary>
public partial class InsiderReconciliationReport
{
    [Inject] private IDbContextFactory<ApplicationDbContext> DbFactory { get; set; } = default!;
    [Inject] private AuthenticationStateProvider AuthState { get; set; } = default!;

    private enum Verdict
    {
        /// <summary>Declared and recorded, and the net quantities match.</summary>
        Agrees,

        /// <summary>The DFM recorded movement on this number and the declaration did not mention
        /// it. The finding the report exists for.</summary>
        NotDeclared,

        /// <summary>Declared, and the DFM file shows nothing. Either the upload for that period is
        /// missing or the transaction did not happen as stated.</summary>
        NotAtDfm,

        /// <summary>Both sides have movement and the net quantities disagree.</summary>
        Differs,
    }

    private sealed record Line(
        int Year,
        int Quarter,
        string Nin,
        string HolderName,
        string DeclaredBy,
        decimal DeclaredPurchases,
        decimal DeclaredSales,
        decimal DfmNet,
        Verdict Verdict)
    {
        public decimal DeclaredNet => DeclaredPurchases - DeclaredSales;

        /// <summary>DFM minus declared. Positive means the exchange saw more buying than was
        /// declared; negative, more selling.</summary>
        public decimal Difference => DfmNet - DeclaredNet;
    }

    private bool _loading = true;
    private List<Line> _lines = [];
    private string _preparedBy = "unknown";
    private string _search = string.Empty;
    private int _yearFilter;
    private int _quarterFilter;
    private string _verdictFilter = "exceptions";

    protected override async Task OnInitializedAsync()
    {
        var state = await AuthState.GetAuthenticationStateAsync();
        _preparedBy = state.User.Identity?.Name ?? "unknown";

        await using var db = await DbFactory.CreateDbContextAsync();

        // Submitted only. A draft is what someone is still writing, and holding it against the
        // exchange's record would raise a finding about a sentence half typed.
        var declarations = await db.InsiderDeclarations.AsNoTracking()
            .Include(d => d.Member)
            .Include(d => d.ShareEntries)
            .Include(d => d.DeclarationCycleRun)
            .Where(d => !d.IsDraft)
            .ToListAsync();

        var trades = await db.ShareTradingRecords.AsNoTracking().ToListAsync();

        _lines = Reconcile(declarations, trades);
        _loading = false;
    }

    /// <summary>One line per (quarter, investor number) on either side. Built from the union, not
    /// from the declarations alone -- a movement nobody declared has no declaration to hang off,
    /// and it is the finding worth having.</summary>
    private static List<Line> Reconcile(List<InsiderDeclaration> declarations, List<ShareTradingRecord> trades)
    {
        var lines = new List<Line>();

        // Every quarter either source says anything about.
        var quarters = declarations
            .Where(d => d.DeclarationCycleRun is not null)
            .Select(d => (d.DeclarationCycleRun!.PeriodYear, d.DeclarationCycleRun.PeriodQuarter))
            .Distinct()
            .ToList();

        foreach (var (year, quarter) in quarters.OrderByDescending(q => q.PeriodYear).ThenByDescending(q => q.PeriodQuarter))
        {
            var from = new DateOnly(year, ((quarter - 1) * 3) + 1, 1);
            var to = from.AddMonths(3).AddDays(-1);

            // What was declared in this quarter, by investor number.
            var declared = new Dictionary<string, (decimal Buys, decimal Sells, string Holder, string By)>(StringComparer.OrdinalIgnoreCase);

            foreach (var d in declarations.Where(d => d.DeclarationCycleRun is { } run
                                                      && run.PeriodYear == year && run.PeriodQuarter == quarter))
            {
                var declarant = d.Member?.FullName ?? d.SubmittedByName;

                foreach (var entry in d.ShareEntries.Where(e => e.IsTransaction))
                {
                    var nin = entry.HolderNin?.Trim();
                    if (string.IsNullOrWhiteSpace(nin)) continue;

                    var holder = entry.IsSelf
                        ? declarant
                        : entry.HolderName ?? $"{entry.Relationship} of {declarant}";

                    declared.TryGetValue(nin, out var running);

                    declared[nin] = (
                        running.Buys + (entry.EntryKind == ShareEntryKind.Purchase ? entry.NumberOfShares : 0),
                        running.Sells + (entry.EntryKind == ShareEntryKind.Sale ? entry.NumberOfShares : 0),
                        string.IsNullOrWhiteSpace(running.Holder) ? holder : running.Holder,
                        string.IsNullOrWhiteSpace(running.By) ? declarant : running.By);
                }
            }

            // What the DFM recorded in the same window, by investor number.
            var actual = trades
                .Where(t => t.ReportDate >= from && t.ReportDate <= to)
                .GroupBy(t => t.Nin.Trim(), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    g => g.Key,
                    g => (Net: g.Sum(t => t.OwnedQtyChange), Name: g.Select(t => t.InvestorName).FirstOrDefault(n => !string.IsNullOrWhiteSpace(n))),
                    StringComparer.OrdinalIgnoreCase);

            foreach (var nin in declared.Keys.Union(actual.Keys, StringComparer.OrdinalIgnoreCase))
            {
                var hasDeclared = declared.TryGetValue(nin, out var dec);
                var hasActual = actual.TryGetValue(nin, out var act);

                var declaredNet = hasDeclared ? dec.Buys - dec.Sells : 0m;
                var dfmNet = hasActual ? act.Net : 0m;

                // A declared transaction of zero shares is not a declaration of movement, and a
                // DFM row whose change nets to zero is not movement either.
                var declaredMoved = hasDeclared && (dec.Buys != 0 || dec.Sells != 0);
                var actualMoved = hasActual && act.Net != 0;

                var verdict = (declaredMoved, actualMoved) switch
                {
                    (false, true) => Verdict.NotDeclared,
                    (true, false) => Verdict.NotAtDfm,
                    (true, true) when declaredNet != dfmNet => Verdict.Differs,
                    _ => Verdict.Agrees,
                };

                lines.Add(new Line(
                    year, quarter, nin,
                    hasDeclared && !string.IsNullOrWhiteSpace(dec.Holder) ? dec.Holder : act.Name ?? "Unknown investor",
                    hasDeclared ? dec.By : "Not declared",
                    hasDeclared ? dec.Buys : 0m,
                    hasDeclared ? dec.Sells : 0m,
                    dfmNet,
                    verdict));
            }
        }

        return lines;
    }

    private List<int> Years() => [.. _lines.Select(l => l.Year).Distinct().OrderByDescending(y => y)];

    private List<Line> Filtered()
    {
        IEnumerable<Line> lines = _lines;

        if (_yearFilter > 0) lines = lines.Where(l => l.Year == _yearFilter);
        if (_quarterFilter > 0) lines = lines.Where(l => l.Quarter == _quarterFilter);

        lines = _verdictFilter switch
        {
            "" => lines,
            "exceptions" => lines.Where(l => l.Verdict != Verdict.Agrees),
            var name when Enum.TryParse<Verdict>(name, out var wanted) => lines.Where(l => l.Verdict == wanted),
            _ => lines,
        };

        if (_search.Trim() is { Length: > 0 } term)
        {
            lines = lines.Where(l =>
                l.HolderName.Contains(term, StringComparison.OrdinalIgnoreCase)
                || l.Nin.Contains(term, StringComparison.OrdinalIgnoreCase)
                || l.DeclaredBy.Contains(term, StringComparison.OrdinalIgnoreCase));
        }

        return [.. lines
            .OrderByDescending(l => l.Year).ThenByDescending(l => l.Quarter)
            .ThenBy(l => l.Verdict).ThenBy(l => l.HolderName)];
    }

    private static string VerdictLabel(Verdict verdict) => verdict switch
    {
        Verdict.Agrees => "Agrees",
        Verdict.NotDeclared => "Not declared",
        Verdict.NotAtDfm => "Not seen at the DFM",
        _ => "Quantities differ",
    };

    private string FilterStatement()
    {
        var parts = new List<string>
        {
            _yearFilter > 0 ? _yearFilter.ToString() : "All years",
            _quarterFilter > 0 ? $"Q{_quarterFilter}" : "All quarters",
            _verdictFilter switch
            {
                "" => "Every line",
                "exceptions" => "Exceptions only",
                var name when Enum.TryParse<Verdict>(name, out var wanted) => VerdictLabel(wanted),
                _ => "Every line",
            },
        };

        if (_search.Trim() is { Length: > 0 } term) parts.Add($"matching \"{term}\"");
        return string.Join(" · ", parts);
    }

    private ReportDocument Build()
    {
        var lines = Filtered();

        return new ReportDocument
        {
            Title = "Declared vs Actual Trading",
            Subtitle = "Insider declarations compared with the DFM's own record for the same investor number and quarter.",
            PreparedBy = _preparedBy,
            Stats =
            [
                new("Investor numbers", lines.Count.ToString(), "in range"),
                new("Not declared", lines.Count(l => l.Verdict == Verdict.NotDeclared).ToString(), "moved at the DFM"),
                new("Not seen at the DFM", lines.Count(l => l.Verdict == Verdict.NotAtDfm).ToString(), "declared only"),
                new("Quantities differ", lines.Count(l => l.Verdict == Verdict.Differs).ToString(), ""),
            ],
            FilterStatement = FilterStatement(),
            Columns =
            [
                new("S. No", 5),
                new("Period", 8),
                new("NIN", 12),
                new("Share Holder", 19),
                new("Declared By", 16),
                new("Declared Buys", 9, Numeric: true),
                new("Declared Sells", 9, Numeric: true),
                new("DFM Net", 8, Numeric: true),
                new("Difference", 8, Numeric: true),
                new("Outcome", 13),
            ],
            Rows =
            [
                .. lines.Select((l, i) => new[]
                {
                    (i + 1).ToString(),
                    $"Q{l.Quarter} {l.Year}",
                    l.Nin,
                    l.HolderName,
                    l.DeclaredBy,
                    l.DeclaredPurchases.ToString("N0"),
                    l.DeclaredSales.ToString("N0"),
                    l.DfmNet.ToString("N0"),
                    l.Difference.ToString("N0"),
                    VerdictLabel(l.Verdict),
                })
            ],
        };
    }
}
