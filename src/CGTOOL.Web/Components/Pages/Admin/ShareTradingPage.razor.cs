using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.EntityFrameworkCore;
using Microsoft.JSInterop;
using CGTOOL.Web.Data;
using CGTOOL.Web.Data.Governance;

namespace CGTOOL.Web.Components.Pages.Admin;

public partial class ShareTradingPage
{
    private const long MaxUploadBytes = 20 * 1024 * 1024;
    private const int UploadChunkBytes = 256 * 1024;

    /// <summary>One row of the Running Position report -- an investor's opening balance (as of just
    /// before From), a running quantity for each date in _positionDates (carried forward from the
    /// last known value on dates that had no upload for this investor), and a closing balance (as
    /// of To). DailyValues is index-aligned with _positionDates.</summary>
    private class RunningPositionRow
    {
        public required string Nin { get; init; }
        public string? InvestorName { get; init; }
        public decimal Opening { get; init; }
        public decimal Closing { get; init; }
        public required List<decimal?> DailyValues { get; init; }
    }

    private List<ShareTradingUpload>? _uploads;
    private List<ShareTradingRecord>? _records;
    private List<DateOnly> _availableReportDates = [];
    private DateOnly? _selectedReportDate;
    private string _activeTab = "report";

    private string _search = string.Empty;
    private string _clientTypeFilter = string.Empty;
    private bool _showPrintPreview;

    // Running Position tab state.
    private DateOnly _positionFrom = DateOnly.FromDateTime(DateTime.Today.AddDays(-9));
    private DateOnly _positionTo = DateOnly.FromDateTime(DateTime.Today);
    private string _positionSearch = string.Empty;
    private List<DateOnly> _positionDates = [];
    private List<RunningPositionRow>? _positionRows;
    private bool _showPositionPrintPreview;

    // Upload tab state.
    private string? _parseError;
    private string? _parsedFileName;
    private List<ShareTradingRecord>? _parsedRecords;
    private int _parsedSkipped;
    private bool _reading;
    private int _readProgress;
    private bool _uploading;
    private int _saveProgress;

    private async Task<string> CurrentActorAsync()
    {
        var state = await AuthState.GetAuthenticationStateAsync();
        return state.User.Identity?.Name ?? "unknown";
    }

    protected override async Task OnInitializedAsync() => await ReloadAsync();

    private async Task ReloadAsync()
    {
        // Its own short-lived context rather than the circuit-scoped ApplicationDbContext:
        // sharing that one lets this race, or outlive, whatever else in the circuit is using
        // it -- which kills the circuit and takes the page with it.
        await using var db = await DbFactory.CreateDbContextAsync();

        _uploads = await db.ShareTradingUploads
            .AsNoTracking()
            .OrderByDescending(u => u.UploadedAtUtc)
            .ToListAsync();

        _availableReportDates = await db.ShareTradingRecords
            .AsNoTracking()
            .Select(r => r.ReportDate)
            .Distinct()
            .OrderByDescending(d => d)
            .ToListAsync();

        if (_selectedReportDate is null || !_availableReportDates.Contains(_selectedReportDate.Value))
        {
            _selectedReportDate = _availableReportDates.Count > 0 ? _availableReportDates[0] : null;
        }

        if (_availableReportDates.Count > 0)
        {
            _positionTo = _availableReportDates[0];
            var earliest = _availableReportDates[^1];
            _positionFrom = _positionTo.AddDays(-9) is var tenDaysBack && tenDaysBack > earliest ? tenDaysBack : earliest;
        }

        await LoadSelectedReportAsync();
    }

    private async Task LoadSelectedReportAsync()
    {
        // Its own short-lived context rather than the circuit-scoped ApplicationDbContext:
        // sharing that one lets this race, or outlive, whatever else in the circuit is using
        // it -- which kills the circuit and takes the page with it.
        await using var db = await DbFactory.CreateDbContextAsync();

        _records = _selectedReportDate is null
            ? []
            : await db.ShareTradingRecords.AsNoTracking().Where(r => r.ReportDate == _selectedReportDate.Value).ToListAsync();
    }

    private async Task OnReportDateSelectedAsync(ChangeEventArgs e)
    {
        if (DateOnly.TryParse(e.Value?.ToString(), out var date))
        {
            _selectedReportDate = date;
            await LoadSelectedReportAsync();
        }
    }

    private async Task LoadRunningPositionAsync()
    {
        // Its own short-lived context rather than the circuit-scoped ApplicationDbContext:
        // sharing that one lets this race, or outlive, whatever else in the circuit is using
        // it -- which kills the circuit and takes the page with it.
        await using var db = await DbFactory.CreateDbContextAsync();

        if (_positionFrom > _positionTo)
        {
            _positionRows = [];
            _positionDates = [];
            return;
        }

        var records = await db.ShareTradingRecords
            .AsNoTracking()
            .Where(r => r.ReportDate >= _positionFrom && r.ReportDate <= _positionTo)
            .ToListAsync();

        _positionDates = records.Select(r => r.ReportDate).Distinct().OrderBy(d => d).ToList();

        _positionRows = records
            .GroupBy(r => r.Nin)
            .Select(g =>
            {
                // Uploads are additive and never overwrite (see ShareTradingUpload), so the same
                // investor can legitimately have more than one record for the same ReportDate --
                // e.g. a cumulative "master" file uploaded after an earlier daily extract already
                // covered that date. Highest ShareTradingUploadId (the most recently uploaded row)
                // wins for that date rather than throwing on the duplicate key.
                var byDate = g
                    .GroupBy(r => r.ReportDate)
                    .ToDictionary(dg => dg.Key, dg => dg.OrderByDescending(r => r.ShareTradingUploadId).First());
                var datesForInvestor = byDate.Keys.OrderBy(d => d).ToList();
                var openingRecord = byDate[datesForInvestor[0]];
                var closingRecord = byDate[datesForInvestor[^1]];

                decimal? running = openingRecord.PreviousOwnQty;
                var daily = new List<decimal?>();
                foreach (var d in _positionDates)
                {
                    if (byDate.TryGetValue(d, out var rec)) running = rec.CurrentOwnQty;
                    daily.Add(running);
                }

                return new RunningPositionRow
                {
                    Nin = g.Key,
                    InvestorName = closingRecord.InvestorName,
                    Opening = openingRecord.PreviousOwnQty,
                    Closing = closingRecord.CurrentOwnQty,
                    DailyValues = daily,
                };
            })
            .OrderBy(r => r.Nin)
            .ToList();
    }

    private List<RunningPositionRow> FilteredPositionRows()
    {
        if (_positionRows is null) return [];
        if (string.IsNullOrWhiteSpace(_positionSearch)) return _positionRows;

        var q = _positionSearch.Trim();
        return _positionRows
            .Where(r => r.Nin.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                        (r.InvestorName is not null && r.InvestorName.Contains(q, StringComparison.OrdinalIgnoreCase)))
            .ToList();
    }

    private async Task OnPositionFromChangedAsync(ChangeEventArgs e)
    {
        if (DateOnly.TryParse(e.Value?.ToString(), out var date))
        {
            _positionFrom = date;
            await LoadRunningPositionAsync();
        }
    }

    private async Task OnPositionToChangedAsync(ChangeEventArgs e)
    {
        if (DateOnly.TryParse(e.Value?.ToString(), out var date))
        {
            _positionTo = date;
            await LoadRunningPositionAsync();
        }
    }

    /// <summary>SVG polyline points for a row's per-date running quantity, normalized into a
    /// 0–width x 0–height box. A single non-null value (or none at all) draws a flat mid-height
    /// line rather than dividing by a zero range.</summary>
    /// <summary>Opening plus every date column -- the trend line starts from the investor's actual
    /// starting balance instead of jumping in at the first dated column.</summary>
    private static List<decimal?> SparklineValues(RunningPositionRow row) =>
        [row.Opening, .. row.DailyValues];

    private static string TrendClass(RunningPositionRow row) => row.Closing switch
    {
        var c when c > row.Opening => "trend-up",
        var c when c < row.Opening => "trend-down",
        _ => "trend-flat",
    };

    private static string SparklinePoints(List<decimal?> values, double width, double height)
    {
        var known = values.Where(v => v.HasValue).Select(v => v!.Value).ToList();
        if (known.Count == 0) return "";

        var min = known.Min();
        var max = known.Max();
        var range = max - min;

        var points = new List<string>();
        for (var i = 0; i < values.Count; i++)
        {
            var x = values.Count == 1 ? 0 : i * width / (values.Count - 1);
            var v = values[i] ?? min;
            var y = range == 0 ? height / 2 : height - (double)((v - min) / range) * height;
            points.Add($"{x.ToString("0.##")},{y.ToString("0.##")}");
        }
        return string.Join(" ", points);
    }

    private static string TransactionType(decimal change) => change switch
    {
        > 0 => "Buy",
        < 0 => "Sell",
        _ => "No Change",
    };

    private List<string> AvailableClientTypes() => (_records ?? [])
        .Select(r => r.ClientType).Where(v => !string.IsNullOrWhiteSpace(v)).Select(v => v!).Distinct().OrderBy(v => v).ToList();

    private List<ShareTradingRecord> FilteredRecords()
    {
        if (_records is null) return [];
        IEnumerable<ShareTradingRecord> query = _records;

        if (!string.IsNullOrEmpty(_clientTypeFilter)) query = query.Where(r => r.ClientType == _clientTypeFilter);

        if (!string.IsNullOrWhiteSpace(_search))
        {
            var q = _search.Trim();
            query = query.Where(r =>
                r.Nin.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                (r.InvestorName is not null && r.InvestorName.Contains(q, StringComparison.OrdinalIgnoreCase)));
        }

        return query.OrderByDescending(r => r.OwnedQtyChange < 0 ? -r.OwnedQtyChange : r.OwnedQtyChange).ThenBy(r => r.Nin).ToList();
    }

    private async Task PrintAsync() => await JS.InvokeVoidAsync("print");

    private async Task SwitchTab(string tab)
    {
        _activeTab = tab;
        if (tab == "upload")
        {
            _parseError = null;
            _parsedFileName = null;
            _parsedRecords = null;
            _parsedSkipped = 0;
        }
        else if (tab == "position" && _positionRows is null)
        {
            await LoadRunningPositionAsync();
        }
    }

    private async Task OnFileSelectedAsync(InputFileChangeEventArgs e)
    {
        _parseError = null;
        _parsedRecords = null;
        _parsedFileName = null;

        var file = e.File;
        if (!file.Name.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase))
        {
            _parseError = "Only .xlsx files are supported.";
            return;
        }
        if (file.Size > MaxUploadBytes)
        {
            _parseError = "File is too large (max 20 MB).";
            return;
        }

        _reading = true;
        _readProgress = 0;
        StateHasChanged();

        try
        {
            await using var stream = file.OpenReadStream(MaxUploadBytes);
            using var memory = new MemoryStream();
            var buffer = new byte[UploadChunkBytes];
            long totalRead = 0;
            int read;
            while ((read = await stream.ReadAsync(buffer)) > 0)
            {
                await memory.WriteAsync(buffer.AsMemory(0, read));
                totalRead += read;
                _readProgress = file.Size == 0 ? 100 : (int)(totalRead * 100 / file.Size);
                StateHasChanged();
            }
            memory.Position = 0;

            var result = InvestorRelationsUploadParser.ParseShareTrading(memory);
            _parsedRecords = result.Records;
            _parsedSkipped = result.SkippedRows;
            _parsedFileName = file.Name;

            if (_parsedRecords.Count == 0)
            {
                _parseError = "No usable rows were found in this file -- check that it has a Report Date/Investor Number header row and at least one data row.";
            }
        }
        catch (Exception ex)
        {
            _parseError = $"Could not read this file: {ex.Message}";
        }
        finally
        {
            _reading = false;
        }
    }

    private async Task ConfirmUploadAsync()
    {
        if (_parsedRecords is null or { Count: 0 } || _parsedFileName is null || _uploading) return;
        _uploading = true;
        _saveProgress = 10;
        StateHasChanged();

        try
        {
            var actor = await CurrentActorAsync();
            var uploadId = await Writer.InsertUploadAsync(_parsedFileName, _parsedRecords.Count, actor);
            _saveProgress = 40;
            StateHasChanged();

            await Writer.BulkInsertRecordsAsync(uploadId, _parsedRecords);
            _saveProgress = 90;
            StateHasChanged();

            var dateRange = _parsedRecords.Select(r => r.ReportDate).Distinct().OrderBy(d => d).ToList();
            var dateSummary = dateRange.Count == 1 ? dateRange[0].ToString("dd/MM/yyyy") : $"{dateRange[0]:dd/MM/yyyy} – {dateRange[^1]:dd/MM/yyyy}";

            await AuditLog.LogAsync(actor, AuditAction.Create, nameof(ShareTradingUpload), uploadId.ToString(),
                $"Uploaded {_parsedFileName} -- {_parsedRecords.Count} trading record(s), report date(s) {dateSummary}" +
                (_parsedSkipped > 0 ? $" ({_parsedSkipped} row(s) skipped)" : string.Empty));
            _saveProgress = 100;

            Toasts.ShowSuccess($"Shares Trading data uploaded -- {_parsedRecords.Count} record(s).");

            _selectedReportDate = dateRange.Count > 0 ? dateRange[^1] : _selectedReportDate;
            _parsedRecords = null;
            _parsedFileName = null;
            _parsedSkipped = 0;
            _activeTab = "report";
            await ReloadAsync();
        }
        finally
        {
            _uploading = false;
            _saveProgress = 0;
        }
    }
}
