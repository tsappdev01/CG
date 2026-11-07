using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.EntityFrameworkCore;
using Microsoft.JSInterop;
using CGTOOL.Web.Data;
using CGTOOL.Web.Data.Governance;

namespace CGTOOL.Web.Components.Pages.Admin;

public partial class ShareholderRegisterPage
{
    private const long MaxUploadBytes = 20 * 1024 * 1024;
    private const int UploadChunkBytes = 256 * 1024;
    private const int PageSize = 25;

    /// <summary>Only the columns the Register view/print actually display -- a real upload can carry
    /// tens of thousands of rows, and pulling every one of ShareholderRecord's 30-odd columns just to
    /// list/search them was most of why this screen was slow to load. "View full record" fetches the
    /// complete row separately, on demand, by Id.</summary>
    private class ShareholderListRow
    {
        public required int Id { get; init; }
        public int? SerialNo { get; init; }
        public required string Nin { get; init; }
        public string? Name { get; init; }
        public string? EnglishName { get; init; }
        public string? ClientType { get; init; }
        public string? LifeStatus { get; init; }
        public string? Citizenship { get; init; }
        public string? CitizenshipDescription { get; init; }
        public string? City { get; init; }
        public decimal Qty { get; init; }
        public decimal QtyPercent { get; init; }
        public decimal Frozen { get; init; }
        public DateOnly? LastTransDate { get; init; }
    }

    private List<ShareholderRegisterUpload>? _uploads;
    private List<ShareholderListRow>? _records;
    private int _selectedUploadId;
    private string _activeTab = "register";
    private int _page = 1;

    private string _search = string.Empty;
    private string _clientTypeFilter = string.Empty;
    private string _lifeStatusFilter = string.Empty;
    private bool _showPrintPreview;
    private ShareholderRecord? _viewRecord;

    // Upload tab state.
    private DateOnly _uploadAsOnDate = DateOnly.FromDateTime(DateTime.Today);
    private string? _parseError;
    private string? _parsedFileName;
    private List<ShareholderRecord>? _parsedRecords;
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

    protected override async Task OnInitializedAsync() => await ReloadUploadsAsync();

    private async Task ReloadUploadsAsync()
    {
        // Its own short-lived context rather than the circuit-scoped ApplicationDbContext:
        // sharing that one lets this race, or outlive, whatever else in the circuit is using
        // it -- which kills the circuit and takes the page with it.
        await using var db = await DbFactory.CreateDbContextAsync();

        _uploads = await db.ShareholderRegisterUploads
            .AsNoTracking()
            .OrderByDescending(u => u.AsOnDate).ThenByDescending(u => u.UploadedAtUtc)
            .ToListAsync();

        if (_uploads.Count > 0 && !_uploads.Any(u => u.Id == _selectedUploadId))
        {
            _selectedUploadId = _uploads[0].Id;
        }

        await LoadSelectedSnapshotAsync();
    }

    private async Task LoadSelectedSnapshotAsync()
    {
        // Its own short-lived context rather than the circuit-scoped ApplicationDbContext:
        // sharing that one lets this race, or outlive, whatever else in the circuit is using
        // it -- which kills the circuit and takes the page with it.
        await using var db = await DbFactory.CreateDbContextAsync();

        _page = 1;
        _records = _selectedUploadId == 0
            ? []
            : await db.ShareholderRecords
                .AsNoTracking()
                .Where(r => r.ShareholderRegisterUploadId == _selectedUploadId)
                .Select(r => new ShareholderListRow
                {
                    Id = r.Id,
                    SerialNo = r.SerialNo,
                    Nin = r.Nin,
                    Name = r.Name,
                    EnglishName = r.EnglishName,
                    ClientType = r.ClientType,
                    LifeStatus = r.LifeStatus,
                    Citizenship = r.Citizenship,
                    CitizenshipDescription = r.CitizenshipDescription,
                    City = r.City,
                    Qty = r.Qty,
                    QtyPercent = r.QtyPercent,
                    Frozen = r.Frozen,
                    LastTransDate = r.LastTransDate,
                })
                .ToListAsync();
    }

    private async Task OnUploadSelectedAsync(ChangeEventArgs e)
    {
        if (int.TryParse(e.Value?.ToString(), out var id))
        {
            _selectedUploadId = id;
            await LoadSelectedSnapshotAsync();
        }
    }

    private async Task ViewRecordAsync(int id)
    {
        // Its own short-lived context rather than the circuit-scoped ApplicationDbContext:
        // sharing that one lets this race, or outlive, whatever else in the circuit is using
        // it -- which kills the circuit and takes the page with it.
        await using var db = await DbFactory.CreateDbContextAsync();
        _viewRecord = await db.ShareholderRecords.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id);
    }
    private List<string> AvailableClientTypes() => (_records ?? [])
        .Select(r => r.ClientType).Where(v => !string.IsNullOrWhiteSpace(v)).Select(v => v!).Distinct().OrderBy(v => v).ToList();

    private List<string> AvailableLifeStatuses() => (_records ?? [])
        .Select(r => r.LifeStatus).Where(v => !string.IsNullOrWhiteSpace(v)).Select(v => v!).Distinct().OrderBy(v => v).ToList();

    private List<ShareholderListRow> FilteredRecords()
    {
        if (_records is null) return [];
        IEnumerable<ShareholderListRow> query = _records;

        if (!string.IsNullOrEmpty(_clientTypeFilter)) query = query.Where(r => r.ClientType == _clientTypeFilter);
        if (!string.IsNullOrEmpty(_lifeStatusFilter)) query = query.Where(r => r.LifeStatus == _lifeStatusFilter);

        if (!string.IsNullOrWhiteSpace(_search))
        {
            var q = _search.Trim();
            query = query.Where(r =>
                r.Nin.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                (r.EnglishName is not null && r.EnglishName.Contains(q, StringComparison.OrdinalIgnoreCase)));
        }

        return query.OrderBy(r => r.SerialNo ?? int.MaxValue).ThenBy(r => r.Nin).ToList();
    }

    // Pagination is client-side (over the already-loaded, narrow-projection snapshot), same pattern
    // as User Management -- but only the current 25-row page is ever rendered, which is what actually
    // keeps a 15,000+ row register fast to display (Blazor Server has to diff every rendered row).
    private int TotalPages => Math.Max(1, (int)Math.Ceiling(FilteredRecords().Count / (double)PageSize));

    private List<ShareholderListRow> PagedRecords()
    {
        var totalPages = TotalPages;
        if (_page > totalPages) _page = totalPages;
        if (_page < 1) _page = 1;
        return FilteredRecords().Skip((_page - 1) * PageSize).Take(PageSize).ToList();
    }

    private void GoToPage(int page) => _page = page;

    // "No of Transactions" has no dedicated transaction log in the register itself -- each holder's
    // only date field is LastTransDate, so "transactions this week/last week/this month" is read as
    // a count of holders whose most recent trade falls in that window against the current snapshot.
    private int TransactionsInRange(DateOnly from, DateOnly to) =>
        (_records ?? []).Count(r => r.LastTransDate is { } d && d >= from && d <= to);

    private (int Holders, decimal Qty, decimal QtyPercent, decimal Frozen, int ThisWeek, int LastWeek, int ThisMonth) Overview()
    {
        var records = _records ?? [];

        // "This week/last week/this month" is measured against the selected snapshot's own as-on
        // date, not the real calendar date -- a register uploaded weeks or months after the date it
        // represents (the normal case) would otherwise show 0 transactions for every window, since
        // every Last Trans. Date would already be older than "this week" by the time it's uploaded.
        var asOnDate = _uploads?.FirstOrDefault(u => u.Id == _selectedUploadId)?.AsOnDate ?? DateOnly.FromDateTime(DateTime.Today);
        var startOfWeek = asOnDate.AddDays(-(int)asOnDate.DayOfWeek);
        var startOfLastWeek = startOfWeek.AddDays(-7);
        var endOfLastWeek = startOfWeek.AddDays(-1);
        var startOfMonth = new DateOnly(asOnDate.Year, asOnDate.Month, 1);

        return (
            records.Count,
            records.Sum(r => r.Qty),
            records.Sum(r => r.QtyPercent),
            records.Sum(r => r.Frozen),
            TransactionsInRange(startOfWeek, asOnDate),
            TransactionsInRange(startOfLastWeek, endOfLastWeek),
            TransactionsInRange(startOfMonth, asOnDate));
    }

    private async Task PrintAsync() => await JS.InvokeVoidAsync("print");

    private void SwitchTab(string tab)
    {
        _activeTab = tab;
        if (tab == "upload")
        {
            _parseError = null;
            _parsedFileName = null;
            _parsedRecords = null;
            _parsedSkipped = 0;
        }
    }

    private async Task OnFileSelectedAsync(InputFileChangeEventArgs e)
    {
        _parseError = null;
        _parsedRecords = null;
        _parsedFileName = null;
        _parsedBytes = null;

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

            var result = InvestorRelationsUploadParser.ParseShareholderRegister(memory);
            _parsedRecords = result.Records;
            _parsedSkipped = result.SkippedRows;
            _parsedFileName = file.Name;

            // Held until the upload is confirmed, not written now: a file read and then abandoned
            // at the preview should leave nothing behind.
            _parsedBytes = memory.ToArray();

            if (_parsedRecords.Count == 0)
            {
                _parseError = "No usable rows were found in this file -- check that it has a Serial No./NIN header row and at least one data row.";
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
            var filePath = await KeepUploadedFileAsync();
            var uploadId = await Writer.InsertUploadAsync(_uploadAsOnDate, _parsedFileName, filePath, _parsedRecords.Count, actor);
            _saveProgress = 40;
            StateHasChanged();

            await Writer.BulkInsertRecordsAsync(uploadId, _parsedRecords);
            _saveProgress = 90;
            StateHasChanged();

            await AuditLog.LogAsync(actor, AuditAction.Create, nameof(ShareholderRegisterUpload), uploadId.ToString(),
                $"Uploaded {_parsedFileName} -- {_parsedRecords.Count} shareholder(s) as on {_uploadAsOnDate:dd/MM/yyyy}" +
                (_parsedSkipped > 0 ? $" ({_parsedSkipped} row(s) skipped -- missing NIN)" : string.Empty));
            _saveProgress = 100;

            Toasts.ShowSuccess($"Share Register uploaded -- {_parsedRecords.Count} shareholder(s) as on {_uploadAsOnDate:dd/MM/yyyy}.");

            _selectedUploadId = uploadId;
            _parsedRecords = null;
            _parsedFileName = null;
            _parsedSkipped = 0;
            _activeTab = "register";
            await ReloadUploadsAsync();
        }
        finally
        {
            _uploading = false;
            _saveProgress = 0;
        }
    }

    /// <summary>A blank workbook with the headers this upload is read by, so a file built by hand
    /// starts from the right column names rather than discovering them through a failed upload.</summary>
    private async Task DownloadRegisterTemplateAsync()
    {
        var bytes = InvestorRelationsTemplates.ShareRegister();
        await JS.InvokeVoidAsync("downloadFileFromBase64", "Share-Register-template.xlsx",
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", Convert.ToBase64String(bytes));
    }

    private byte[]? _parsedBytes;

    /// <summary>Files the uploaded workbook itself alongside the rows parsed out of it.
    ///
    /// A register is read from the parsed rows, but a reconciliation argues with the source: a
    /// figure that looks wrong is settled by opening the file it came out of, and "we uploaded it
    /// in September" is not a file. Kept under its own as-on date so the folder reads as the
    /// upload history does.
    ///
    /// Returns null, rather than failing the upload, if the file cannot be written. The rows are
    /// the register; the copy is evidence, and losing the evidence is not a reason to lose the
    /// register with it -- the audit entry says it happened.</summary>
    private async Task<string?> KeepUploadedFileAsync()
    {
        if (_parsedBytes is null || _parsedFileName is null) return null;

        try
        {
            var directory = Path.Combine(Env.WebRootPath, "uploads", "investor-relations", "share-register");
            Directory.CreateDirectory(directory);

            // The member's own file name is not used on disk: it comes from their machine and two
            // uploads a month apart are both called "SHARE REGISTERED.xlsx". The original name is
            // on the history row beside the link.
            var fileName = $"{_uploadAsOnDate:yyyy-MM-dd}-{Guid.NewGuid():N}.xlsx";
            await File.WriteAllBytesAsync(Path.Combine(directory, fileName), _parsedBytes);

            return $"/uploads/investor-relations/share-register/{fileName}";
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Keeping the uploaded Share Register file failed; the rows were still saved.");
            return null;
        }
    }
}
