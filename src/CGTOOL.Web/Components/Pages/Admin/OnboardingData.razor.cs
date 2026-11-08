using Microsoft.AspNetCore.Components.Forms;
using Microsoft.EntityFrameworkCore;
using CGTOOL.Web.Data.Governance;

namespace CGTOOL.Web.Components.Pages.Admin;

public partial class OnboardingData
{
    /// <summary>Per scan. The folder these came from holds files up to about two megabytes, with
    /// one at four and a half.</summary>
    private const long MaxScanBytes = 10 * 1024 * 1024;

    /// <summary>How many scans one upload may carry. The real folder has 226.</summary>
    private const int MaxScanCount = 1000;

    private bool _busy;
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
                // than one called out by name.
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
}
