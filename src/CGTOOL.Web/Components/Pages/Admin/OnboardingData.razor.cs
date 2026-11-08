using System.Security.Claims;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.EntityFrameworkCore;
using CGTOOL.Web.Data.Governance;

namespace CGTOOL.Web.Components.Pages.Admin;

public partial class OnboardingData
{
    /// <summary>The spreadsheet is a few hundred kilobytes; this is headroom, not a target.</summary>
    private const long MaxWorkbookBytes = 20 * 1024 * 1024;

    /// <summary>Per scan. The folder these came from holds files up to about two megabytes.</summary>
    private const long MaxScanBytes = 10 * 1024 * 1024;

    /// <summary>How many scans one upload may carry. The real folder has 226.</summary>
    private const int MaxScanCount = 1000;

    private bool _busy;

    private IReadOnlyList<IdentityImportRow>? _parsed;
    private IReadOnlyList<string>? _parseProblems;
    private IdentityImportResult? _import;

    private List<EmiratesIdAttachOutcome>? _scanResults;
    private string? _scanProgress;

    private int _referenceCount, _withEmiratesId, _withScan, _withNin;

    protected override Task OnInitializedAsync() => RefreshCountsAsync();

    private async Task RefreshCountsAsync()
    {
        await using var db = await DbFactory.CreateDbContextAsync();
        _referenceCount = await db.MemberIdentityReferences.CountAsync();
        _withEmiratesId = await db.MemberIdentityReferences.CountAsync(r => r.EmiratesIdNumber != null && r.EmiratesIdNumber != "");
        _withScan = await db.MemberIdentityReferences.CountAsync(r => r.EmiratesIdBlobId != null);
        _withNin = await db.MemberIdentityReferences.CountAsync(r => r.Nin != null && r.Nin != "");
    }

    private async Task OnSpreadsheetAsync(InputFileChangeEventArgs e)
    {
        _parsed = null;
        _import = null;
        _parseProblems = null;

        if (e.File.Size > MaxWorkbookBytes)
        {
            Toasts.ShowError("That file is larger than 20 MB.");
            return;
        }

        _busy = true;
        StateHasChanged();
        try
        {
            // Read whole before parsing: ClosedXML seeks, and the browser's stream does not.
            using var buffer = new MemoryStream();
            await using (var stream = e.File.OpenReadStream(MaxWorkbookBytes))
            {
                await stream.CopyToAsync(buffer);
            }

            buffer.Position = 0;
            _parsed = Importer.Parse(buffer, out var problems);
            _parseProblems = problems;

            Toasts.ShowSuccess($"Read {_parsed.Count} rows. Run a dry run before loading.");
        }
        catch (Exception ex)
        {
            Toasts.ShowError($"Could not read that file: {ex.Message}");
        }
        finally
        {
            _busy = false;
        }
    }

    private async Task RunImportAsync(bool dryRun)
    {
        if (_parsed is null) return;

        _busy = true;
        StateHasChanged();
        try
        {
            _import = await Importer.ImportAsync(
                _parsed, "employee spreadsheet", dryRun, createMissingMembers: false, await ActorAsync());

            if (!dryRun)
            {
                await RefreshCountsAsync();
                Toasts.ShowSuccess($"Loaded: {_import.ReferencesInserted} added, {_import.ReferencesUpdated} updated.");
            }
        }
        catch (Exception ex)
        {
            Toasts.ShowError($"The import failed: {ex.Message}");
        }
        finally
        {
            _busy = false;
        }
    }

    private async Task OnScansAsync(InputFileChangeEventArgs e)
    {
        _scanResults = [];
        _busy = true;
        StateHasChanged();

        try
        {
            var files = e.GetMultipleFiles(MaxScanCount);
            var index = 0;

            foreach (var file in files)
            {
                index++;

                // Reported rather than skipped silently: a card nobody notices is missing is worse
                // than one that is called out by name.
                if (file.Size > MaxScanBytes)
                {
                    _scanResults.Add(new EmiratesIdAttachOutcome(file.Name, null, false, "Larger than 10 MB."));
                    continue;
                }

                _scanProgress = $"Reading {index} of {files.Count}: {file.Name}";
                StateHasChanged();

                try
                {
                    using var buffer = new MemoryStream();
                    await using (var stream = file.OpenReadStream(MaxScanBytes))
                    {
                        await stream.CopyToAsync(buffer);
                    }

                    _scanResults.Add(await Attacher.AttachAsync(
                        file.Name, buffer.ToArray(), file.ContentType, Client.IpAddress));
                }
                catch (Exception ex)
                {
                    _scanResults.Add(new EmiratesIdAttachOutcome(file.Name, null, false, ex.Message));
                }
            }

            _scanProgress = null;
            await RefreshCountsAsync();

            var attached = _scanResults.Count(r => r.Attached);
            Toasts.ShowSuccess($"{attached} of {_scanResults.Count} scans attached.");
        }
        finally
        {
            _busy = false;
        }
    }

    private async Task<string> ActorAsync()
    {
        var state = await AuthState.GetAuthenticationStateAsync();
        return state.User.FindFirst(ClaimTypes.Name)?.Value ?? state.User.Identity?.Name ?? "unknown";
    }
}
