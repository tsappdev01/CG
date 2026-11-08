using System.Security.Claims;
using Microsoft.AspNetCore.Components;
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

    /// <summary>The page the member was trying to open when onboarding interrupted them, so
    /// finishing puts them back there rather than on a dashboard they then have to navigate
    /// away from.</summary>
    [SupplyParameterFromQuery(Name = "returnUrl")]
    private string? ReturnUrl { get; set; }

    /// <summary>Where "finish" goes. Only a path within this application is accepted: the value
    /// arrives in the address bar, and a redirect that will send a signed-in member to any URL it
    /// is handed is an open redirect. Anything else falls back to the dashboard.</summary>
    private string ReturnTarget =>
        ReturnUrl is { Length: > 0 } url
        && url.StartsWith('/')
        // A second slash OR backslash: "//evil.example" is protocol-relative, and browsers
        // normalise "/\evil.example" to the same thing, so testing only for "//" lets the
        // backslash form straight through.
        && (url.Length == 1 || (url[1] != '/' && url[1] != '\\'))
        && !url.Contains(':', StringComparison.Ordinal)
            ? url
            : "/";

    private string ReturnLabel => ReturnTarget == "/" ? "Go to my dashboard" : "Continue to my declaration";

    private Member? _member;
    private MemberOnboarding? _record;
    private OnboardingStep _step = OnboardingStep.Welcome;

    private bool _editingEmiratesId;
    private bool _editingDfm;

    /// <summary>What the member was shown before they started editing. Kept so cancelling puts
    /// the screen back, and so saving can record which fields actually moved -- the inputs bind
    /// straight onto the record, so by the time they press save the original is gone.</summary>
    private EmiratesIdValues? _emiratesIdBefore;
    private DfmValues? _dfmBefore;

    private bool? _hasNin;
    private string? _captureNote;
    private string? _editNote;

    /// <summary>The member may continue once there is a card and a number against it. The expiry
    /// is asked for but not insisted on: a member who cannot read it off a worn card should not be
    /// stuck on this screen, and the field is editable later in My Workspace.</summary>
    private bool CanSubmitCard =>
        _record?.EmiratesIdBlobId is not null && !string.IsNullOrWhiteSpace(_record.EmiratesIdNumber);

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

    private void BeginEditEmiratesId()
    {
        _emiratesIdBefore = EmiratesIdValues.From(_record!);
        _editNote = null;
        _editingEmiratesId = true;
    }

    /// <summary>Puts back what was on the screen before. The inputs write straight onto the
    /// record, so leaving the edit without this would keep half-typed values on a screen that
    /// says they are the details we hold.</summary>
    private void CancelEmiratesIdEdit()
    {
        if (_emiratesIdBefore is { } before)
        {
            _record!.EmiratesIdNumber = before.Number;
            _record.EmiratesIdHolderName = before.HolderName;
            _record.EmiratesIdNationality = before.Nationality;
            _record.EmiratesIdExpiryDate = before.Expiry;
        }

        _editNote = null;
        _editingEmiratesId = false;
    }

    private Task SaveEmiratesIdAsync() => RunAsync(async () =>
    {
        if (string.IsNullOrWhiteSpace(_record!.EmiratesIdNumber))
        {
            _editNote = "Please give your Emirates ID number.";
            return;
        }

        _editNote = null;
        await Flow.CorrectEmiratesIdAsync(_record, _emiratesIdBefore!, await ActorAsync());
        _editingEmiratesId = false;
        _emiratesIdBefore = null;
    });

    private Task SubmitEmiratesIdAsync() => RunAsync(async () =>
    {
        await Flow.SubmitEmiratesIdAsync(_record!, await ActorAsync());
    });

    private Task ConfirmDfmAsync() => RunAsync(async () =>
    {
        await Flow.ConfirmDfmAsync(_record!, await ActorAsync());
    });

    private void BeginEditDfm()
    {
        _dfmBefore = DfmValues.From(_record!);
        _editNote = null;
        _editingDfm = true;
    }

    private void CancelDfmEdit()
    {
        if (_dfmBefore is { } before)
        {
            _record!.NinNumber = before.Nin;
            _record.NinRegisteredName = before.RegisteredName;
        }

        _editNote = null;
        _editingDfm = false;
    }

    private Task SaveDfmAsync() => RunAsync(async () =>
    {
        // The same rule My Workspace and the declarations apply, so a NIN that is good here is
        // good there -- a member should not get past onboarding with one that the next screen
        // they see will reject.
        if (!Nin.IsValid(_record!.NinNumber))
        {
            _editNote = $"A NIN is {Nin.Length} letters or digits. Please check it.";
            return;
        }

        _editNote = null;
        await Flow.CorrectDfmAsync(_record, _dfmBefore!, await ActorAsync());
        _editingDfm = false;
        _dfmBefore = null;
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

    /// <summary>Takes the member's Emirates ID as one PDF.
    ///
    /// One file, not a front and a back. The scans the organisation already holds are a single PDF
    /// per person with both sides in it, so asking a member for two would store their card in a
    /// shape nothing else here uses -- and leave whoever checks it wondering which of the two the
    /// bulk-loaded ones correspond to.</summary>
    private async Task UploadCardAsync(InputFileChangeEventArgs e)
    {
        if (_record is null) return;

        var file = e.File;

        // PDF only, and checked on the name as well as the type: a browser that reports nothing
        // useful for ContentType would otherwise have the upload refused for the wrong reason.
        var isPdf = file.ContentType == "application/pdf"
            || file.Name.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase);
        if (!isPdf)
        {
            _captureNote = "Please upload the card as a PDF.";
            return;
        }

        if (file.Size > MaxCardBytes)
        {
            _captureNote = $"That file is {file.Size / (1024 * 1024)} MB. The limit is 5 MB.";
            return;
        }

        _busy = true;
        _captureNote = "Reading the card…";
        StateHasChanged();
        try
        {
            using var buffer = new MemoryStream();
            await using (var stream = file.OpenReadStream(MaxCardBytes))
            {
                await stream.CopyToAsync(buffer);
            }

            var content = buffer.ToArray();
            _record.EmiratesIdBlobId = await Documents.SaveAsync(
                content, file.Name, "application/pdf", Client.IpAddress);

            _captureNote = await CaptureAsync(content);
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
