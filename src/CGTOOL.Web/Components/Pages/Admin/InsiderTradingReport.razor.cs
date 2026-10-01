using CGTOOL.Web.Data;
using CGTOOL.Web.Data.Governance;
using CGTOOL.Web.Data.Reports;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.EntityFrameworkCore;

namespace CGTOOL.Web.Components.Pages.Admin;

/// <summary>The legacy Insider Trading Report: DI share movements over a date range, against the
/// NINs insiders declared.
///
/// Two differences from the legacy report, both forced by what CGTOOL actually holds. The uploaded
/// DFM data carries quantities, not prices, so there is no monetary value to print and the report
/// shows opening, closing and net quantity instead of a Value column -- a column of zeroes would
/// be worse than no column. And the report can tell a declared insider from any other investor,
/// which the legacy one could not, because the NIN on the movement is matched against the NINs on
/// submitted declarations; that match is the point of running it.</summary>
public partial class InsiderTradingReport
{
    [Inject] private IDbContextFactory<ApplicationDbContext> DbFactory { get; set; } = default!;
    [Inject] private AuthenticationStateProvider AuthState { get; set; } = default!;

    private List<ShareTradingRecord>? _records;

    /// <summary>NIN to the person who declared it, built from submitted declarations -- their own
    /// NIN and their relatives'. A movement on a NIN in here is a movement by an insider.</summary>
    private Dictionary<string, string> _insiderNins = [];

    private string _preparedBy = "unknown";
    private string _search = string.Empty;
    private DateTime? _startDate;
    private DateTime? _endDate;
    private string _movementFilter = "moved";
    private string _insiderFilter = "insiders";

    protected override async Task OnInitializedAsync()
    {
        var state = await AuthState.GetAuthenticationStateAsync();
        _preparedBy = state.User.Identity?.Name ?? "unknown";

        await using var db = await DbFactory.CreateDbContextAsync();

        _records = await db.ShareTradingRecords.AsNoTracking().OrderBy(r => r.ReportDate).ThenBy(r => r.InvestorName).ToListAsync();

        var declarations = await db.InsiderDeclarations
            .AsNoTracking()
            .Include(d => d.Member)
            .Include(d => d.Relatives)
            .Include(d => d.NinHolders)
            .Where(d => !d.IsDraft)
            .ToListAsync();

        foreach (var d in declarations)
        {
            var declarant = d.Member?.FullName ?? d.SubmittedByName;

            Add(d.NinNumber, declarant);
            Add(d.SharesNinNumber, declarant);
            foreach (var r in d.Relatives) Add(r.NinNumber, $"{r.RelativeName} (relative of {declarant})");
            foreach (var h in d.NinHolders) Add(h.NinNumber, $"{h.NameOfShareHolder} (relative of {declarant})");
        }

        // Default to the current year, which is the range this is almost always run for.
        _startDate = new DateTime(DateTime.Today.Year, 1, 1);
        _endDate = DateTime.Today;

        void Add(string? nin, string who)
        {
            if (nin is { Length: > 0 } && !_insiderNins.ContainsKey(nin.Trim()))
                _insiderNins[nin.Trim()] = who;
        }
    }

    private List<ShareTradingRecord> Filtered()
    {
        var rows = (_records ?? []).AsEnumerable();

        if (_startDate is { } from) rows = rows.Where(r => r.ReportDate >= DateOnly.FromDateTime(from));
        if (_endDate is { } to) rows = rows.Where(r => r.ReportDate <= DateOnly.FromDateTime(to));

        rows = _movementFilter switch
        {
            "moved" => rows.Where(r => r.OwnedQtyChange != 0),
            "buy" => rows.Where(r => r.OwnedQtyChange > 0),
            "sell" => rows.Where(r => r.OwnedQtyChange < 0),
            _ => rows,
        };

        if (_insiderFilter == "insiders")
            rows = rows.Where(r => _insiderNins.ContainsKey(r.Nin.Trim()));

        if (_search.Trim() is { Length: > 0 } term)
        {
            rows = rows.Where(r =>
                (r.InvestorName ?? string.Empty).Contains(term, StringComparison.OrdinalIgnoreCase) ||
                r.Nin.Contains(term, StringComparison.OrdinalIgnoreCase));
        }

        return [.. rows];
    }

    private ReportDocument Build()
    {
        var rows = Filtered();

        return new ReportDocument
        {
            Title = "Insider Trading Report",
            Subtitle = "DI share movements over the period, matched against declared insider National Investor Numbers.",
            PreparedBy = _preparedBy,
            Stats =
            [
                new("Movements", rows.Count.ToString(), "in range"),
                new("By declared insiders", rows.Count(r => _insiderNins.ContainsKey(r.Nin.Trim())).ToString(), ""),
                new("Increases", rows.Count(r => r.OwnedQtyChange > 0).ToString(), "net buy"),
                new("Decreases", rows.Count(r => r.OwnedQtyChange < 0).ToString(), "net sell"),
            ],
            FilterStatement = FilterStatement(),
            Columns =
            [
                new("S. No", 5),
                new("NIN", 12),
                new("Name of Share Holder", 20),
                new("Declared by", 17),
                new("Type", 8),
                new("Date", 10),
                new("Opening", 9, Numeric: true),
                new("Closing", 9, Numeric: true),
                new("Volume", 10, Numeric: true),
            ],
            Rows =
            [
                .. rows.Select((r, i) => new[]
                {
                    (i + 1).ToString(),
                    r.Nin,
                    r.InvestorName ?? "—",
                    _insiderNins.TryGetValue(r.Nin.Trim(), out var who) ? who : "Not a declared insider",
                    r.OwnedQtyChange > 0 ? "Buy" : r.OwnedQtyChange < 0 ? "Sell" : "No change",
                    r.ReportDate.ToString("dd/MM/yyyy"),
                    r.PreviousOwnQty.ToString("0.##"),
                    r.CurrentOwnQty.ToString("0.##"),
                    Math.Abs(r.OwnedQtyChange).ToString("0.##"),
                })
            ],
        };
    }

    private string FilterStatement()
    {
        var parts = new List<string>
        {
            $"{(_startDate is { } f ? f.ToString("dd/MM/yyyy") : "any date")} to {(_endDate is { } t ? t.ToString("dd/MM/yyyy") : "any date")}",
            _movementFilter switch
            {
                "moved" => "Movements only",
                "buy" => "Increases only",
                "sell" => "Decreases only",
                _ => "Including no movement",
            },
            _insiderFilter == "insiders" ? "Declared insiders only" : "Every investor",
        };
        if (_search.Trim() is { Length: > 0 } term) parts.Add($"Search “{term}”");
        return string.Join(" · ", parts);
    }
}
