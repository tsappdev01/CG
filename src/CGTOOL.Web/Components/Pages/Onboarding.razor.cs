using System.Security.Claims;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.EntityFrameworkCore;
using CGTOOL.Web.Data.Governance;

namespace CGTOOL.Web.Components.Pages;

public partial class Onboarding
{
    /// <summary>Per side of the card. A photographed Emirates ID is well under this.</summary>
    private const long MaxCardBytes = 5 * 1024 * 1024;

    private bool _loading = true;
    private bool _busy;

    private Member? _member;
    private MemberOnboarding? _record;
    private OnboardingStep _step = OnboardingStep.Welcome;

    private bool _reportingEmiratesId;
    private bool _reportingDfm;
    private string _issueText = string.Empty;
    private bool? _hasNin;
    private string? _captureNote;

    /// <summary>The member may continue once there is a card and a number against it. The expiry
    /// is asked for but not insisted on: a member who cannot read it off a worn card should not be
    /// stuck on this screen, and the field is editable later in My Workspace.</summary>
    private bool CanSubmitCard =>
        _record?.EmiratesIdFrontBlobId is not null && !string.IsNullOrWhiteSpace(_record.EmiratesIdNumber);

    protected override async Task OnInitializedAsync()
    {
        _member = await ResolveMemberAsync();
        if (_member is not null)
        {
            _record = await Flow.GetOrCreateAsync(_member);
            _step = Flow.NextStep(_record);

            // Someone who has already finished and comes back to the address should see that,
            // not be walked through it again.
            if (_record.Status == OnboardingStatus.Completed) _step = OnboardingStep.Done;
        }

        _loading = false;
    }

    /// <summary>Who this is. The same rule the rest of the application uses, including acting on
    /// someone's behalf -- an assistant completing onboarding for the person they act for is the
    /// same act as completing their declaration.</summary>
    private async Task<Member?> ResolveMemberAsync()
    {
        await using var db = await DbFactory.CreateDbContextAsync();

        if (ActingAs.Read(HttpContextAccessor.HttpContext) is { } acting)
        {
            return await db.Members.FirstOrDefaultAsync(m => m.Id == acting.MemberId);
        }

        var state = await AuthState.GetAuthenticationStateAsync();
        var userId = state.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return userId is null ? null : await db.Members.FirstOrDefaultAsync(m => m.ApplicationUserId == userId);
    }

    private async Task<string> ActorAsync()
    {
        var state = await AuthState.GetAuthenticationStateAsync();
        return state.User.Identity?.Name ?? "unknown";
    }

    private string StepClass(OnboardingStep step) =>
        _step == step ? "on" : step < _step ? "done" : string.Empty;

    private static string Show(string? value) => string.IsNullOrWhiteSpace(value) ? "—" : value;

    // ---------------------------------------------------------------- the flow

    private Task StartAsync() => RunAsync(async () =>
    {
        await Flow.StartAsync(_record!, await ActorAsync());
    });

    private Task ConfirmEmiratesIdAsync() => RunAsync(async () =>
    {
        await Flow.ConfirmEmiratesIdAsync(_record!, await ActorAsync());
    });

    private Task ReportEmiratesIdAsync() => RunAsync(async () =>
    {
        await Flow.ReportEmiratesIdIssueAsync(_record!, _issueText.Trim(), await ActorAsync());
        _reportingEmiratesId = false;
        _issueText = string.Empty;
    });

    private Task SubmitEmiratesIdAsync() => RunAsync(async () =>
    {
        await Flow.SubmitEmiratesIdAsync(_record!, await ActorAsync());
    });

    private Task ConfirmDfmAsync() => RunAsync(async () =>
    {
        await Flow.ConfirmDfmAsync(_record!, await ActorAsync());
    });

    private Task ReportDfmAsync() => RunAsync(async () =>
    {
        await Flow.ReportDfmIssueAsync(_record!, _issueText.Trim(), await ActorAsync());
        _reportingDfm = false;
        _issueText = string.Empty;
    });

    private Task SubmitNinAsync() => RunAsync(async () =>
    {
        await Flow.SubmitNinAsync(_record!, await ActorAsync());
    });

    private Task RecordNoNinAsync() => RunAsync(async () =>
    {
        await Flow.RecordNoNinAsync(_record!, await ActorAsync());
    });

    private Task CompleteAsync() => RunAsync(async () =>
    {
        await Flow.CompleteAsync(_record!, _member!, await ActorAsync());
    });

    /// <summary>Runs a step and re-reads where the member now is.
    ///
    /// The step is always recomputed from the record rather than advanced by hand, so the screen
    /// cannot get ahead of what has actually been written -- and a member who closes the tab and
    /// comes back lands on the same screen for the same reason.</summary>
    private async Task RunAsync(Func<Task> action)
    {
        if (_record is null || _busy) return;

        _busy = true;
        StateHasChanged();
        try
        {
            await action();
            _step = Flow.NextStep(_record);
        }
        finally
        {
            _busy = false;
        }
    }

    // ---------------------------------------------------------------- uploads

    private async Task UploadCardAsync(InputFileChangeEventArgs e, bool front)
    {
        if (_record is null) return;

        var file = e.File;
        var contentType = file.ContentType switch
        {
            "image/png" => "image/png",
            "image/jpeg" => "image/jpeg",
            "application/pdf" => "application/pdf",
            _ => null,
        };

        if (contentType is null)
        {
            _captureNote = "Only PDF, JPG or PNG files can be uploaded.";
            return;
        }

        if (file.Size > MaxCardBytes)
        {
            _captureNote = "That file is larger than 5 MB.";
            return;
        }

        _busy = true;
        _captureNote = front ? "Reading the front of the card…" : "Saving…";
        StateHasChanged();
        try
        {
            using var buffer = new MemoryStream();
            await using (var stream = file.OpenReadStream(MaxCardBytes))
            {
                await stream.CopyToAsync(buffer);
            }

            var content = buffer.ToArray();
            var blobId = await Documents.SaveAsync(content, file.Name, contentType, Client.IpAddress);

            if (front) _record.EmiratesIdFrontBlobId = blobId;
            else _record.EmiratesIdBackBlobId = blobId;

            // Only the front carries the printed details worth reading.
            _captureNote = front ? await CaptureAsync(content) : null;
        }
        catch (Exception ex)
        {
            _captureNote = $"That file could not be read ({ex.Message}). Try again.";
        }
        finally
        {
            _busy = false;
        }
    }

    /// <summary>Fills what can be read off the card, leaving every field editable.
    ///
    /// Best effort on purpose: an Emirates ID's bilingual layout defeats the reader often enough
    /// that anything it fills has to be checkable, and a member whose card will not read still has
    /// to be able to finish by typing.</summary>
    private async Task<string?> CaptureAsync(byte[] content)
    {
        if (!DocIntel.IsConfigured) return null;

        try
        {
            using var stream = new MemoryStream(content, writable: false);
            var read = await DocIntel.AnalyzeIdDocumentAsync(stream);
            if (read is null) return "Nothing could be read from that — please type the details below.";

            if (!string.IsNullOrWhiteSpace(read.DocumentNumber)) _record!.EmiratesIdNumber = read.DocumentNumber;
            if (read.DateOfExpiration is not null) _record!.EmiratesIdExpiryDate = read.DateOfExpiration;

            var name = string.Join(" ", new[] { read.FirstName, read.LastName }
                .Where(part => !string.IsNullOrWhiteSpace(part)));
            if (!string.IsNullOrWhiteSpace(name)) _record!.EmiratesIdHolderName = name;

            return "We filled in what we could read — please check it before continuing.";
        }
        catch
        {
            return "We could not read that automatically — please type the details below.";
        }
    }

    private async Task UploadNinDocumentAsync(InputFileChangeEventArgs e)
    {
        if (_record is null) return;

        var file = e.File;
        if (file.Size > MaxCardBytes) return;

        _busy = true;
        StateHasChanged();
        try
        {
            using var buffer = new MemoryStream();
            await using (var stream = file.OpenReadStream(MaxCardBytes))
            {
                await stream.CopyToAsync(buffer);
            }

            _record.NinDocumentBlobId = await Documents.SaveAsync(
                buffer.ToArray(), file.Name, file.ContentType, Client.IpAddress);
        }
        finally
        {
            _busy = false;
        }
    }
}
