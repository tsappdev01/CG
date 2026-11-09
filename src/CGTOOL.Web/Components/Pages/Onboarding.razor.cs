using System.Security.Claims;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.EntityFrameworkCore;
using CGTOOL.Web.Data.Governance;

namespace CGTOOL.Web.Components.Pages;

public partial class Onboarding
{
    /// <summary>Per side of the card. A photographed Emirates ID is well under this.</summary>
    private const long MaxCardBytes = UploadLimits.DocumentBytes;

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
    /// <summary>The furthest the record allows: always recomputed from what has been written.</summary>
    private OnboardingStep _step = OnboardingStep.Welcome;

    /// <summary>A step the member has gone back to look at, overriding the one the record puts
    /// them on. Null when they are simply going forwards.
    ///
    /// Kept apart from _step rather than assigned over it so going back cannot make the flow
    /// think a step is unanswered: the record still decides what has been settled, and this only
    /// decides what is on screen.</summary>
    private OnboardingStep? _viewStep;

    private OnboardingStep CurrentStep => _viewStep ?? _step;

    private static readonly (OnboardingStep Step, string Label)[] Rail =
    [
        (OnboardingStep.EmiratesId, "Emirates ID"),
        (OnboardingStep.Dfm, "DFM / NIN"),
        (OnboardingStep.Done, "Done"),
    ];

    /// <summary>Whether the member may jump to a step. Anything up to the furthest they have
    /// reached, so they can go back and change an answer before finishing -- but nothing ahead of
    /// it, which would skip a question, and nothing at all once onboarding is complete.</summary>
    private bool CanGoTo(OnboardingStep step) =>
        _record is { Status: not OnboardingStatus.Completed }
        && step != CurrentStep
        && step <= _step;

    private void GoTo(OnboardingStep step)
    {
        if (_busy || !CanGoTo(step)) return;

        // Leaving a half-typed correction behind would put values on a screen that presents them
        // as the details on file, so the edit is backed out rather than carried along.
        if (_editingEmiratesId) CancelEmiratesIdEdit();
        if (_editingDfm) CancelDfmEdit();

        _editNote = null;
        _viewStep = step == _step ? null : step;
    }

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

    /// <summary>How far the file has got, 0-100 while it is transferring and null otherwise.
    ///
    /// Counted rather than a spinner: the card is a PDF of up to UploadLimits.DocumentBytes over whatever connection
    /// the member happens to be on, and the step before this one was instant. Without a number
    /// the screen looks like it ignored the file -- the only sign anything was happening was a
    /// line of text below the fields, which is not where somebody who has just pressed Choose
    /// File is looking.</summary>
    private double? _uploadPercent;

    /// <summary>What is happening now, beside the bar. Reading the card is a round trip to a
    /// service that cannot be counted, so that stage shows a spinner instead.</summary>
    private string? _uploadStage;

    private bool _uploading;

    /// <summary>The member may continue once there is a card and a number against it. The expiry
    /// is asked for but not insisted on: a member who cannot read it off a worn card should not be
    /// stuck on this screen, and the field is editable later in My Workspace.</summary>
    private bool CanSubmitCard =>
        _record?.EmiratesIdBlobId is not null && EmiratesIdNumber.IsValid(_record.EmiratesIdNumber);

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
        CurrentStep == step ? "on" : step < _step ? "done" : string.Empty;

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
            _record.EmiratesIdBlobId = before.BlobId;
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

        // Stored as digits, which is the form the imported records and the bulk-loaded scans
        // use. A member who typed the separators off the card has the same number, not a
        // different one, and it has to match to be any use.
        _record.EmiratesIdNumber = EmiratesIdNumber.Normalise(_record.EmiratesIdNumber);

        _editNote = null;
        await Flow.CorrectEmiratesIdAsync(_record, _emiratesIdBefore!, await ActorAsync());
        _editingEmiratesId = false;
        _emiratesIdBefore = null;
    });

    private Task SubmitEmiratesIdAsync() => RunAsync(async () =>
    {
        _record!.EmiratesIdNumber = EmiratesIdNumber.Normalise(_record.EmiratesIdNumber);
        await Flow.SubmitEmiratesIdAsync(_record, await ActorAsync());
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

            // Back where the record says they are. Answering a step they had gone back to should
            // carry them forward again, not leave them sitting on it.
            _viewStep = null;
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
            _captureNote = UploadLimits.TooLarge(file.Size);
            return;
        }

        _busy = true;
        _uploading = true;
        _uploadPercent = 0;
        _uploadStage = $"Uploading {file.Name}";
        _captureNote = null;
        StateHasChanged();
        try
        {
            // Read in chunks so the bar can move. CopyToAsync would do the same work and report
            // nothing until it was finished, which on a slow connection is the whole wait.
            const int chunkBytes = 64 * 1024;
            using var buffer = new MemoryStream();
            await using (var stream = file.OpenReadStream(MaxCardBytes))
            {
                var chunk = new byte[chunkBytes];
                long soFar = 0;
                int read;
                while ((read = await stream.ReadAsync(chunk)) > 0)
                {
                    await buffer.WriteAsync(chunk.AsMemory(0, read));
                    soFar += read;
                    _uploadPercent = soFar * 100d / Math.Max(1, file.Size);
                    StateHasChanged();
                }
            }

            var content = buffer.ToArray();

            _uploadPercent = null;
            _uploadStage = "Saving the card…";
            StateHasChanged();

            _record.EmiratesIdBlobId = await Documents.SaveAsync(
                content, file.Name, "application/pdf", Client.IpAddress);

            _uploadStage = "Reading the details off the card…";
            StateHasChanged();

            _captureNote = await CaptureAsync(content);
        }
        catch (Exception ex)
        {
            _captureNote = $"That file could not be read ({ex.Message}). Try again.";
        }
        finally
        {
            _busy = false;
            _uploading = false;
            _uploadPercent = null;
            _uploadStage = null;
        }
    }

    /// <summary>Fills what can be read off the card, leaving every field editable.
    ///
    /// Best effort on purpose: an Emirates ID's bilingual layout defeats the reader often enough
    /// that anything it fills has to be checkable, and a member whose card will not read still has
    /// to be able to finish by typing.</summary>
    private async Task<string?> CaptureAsync(byte[] content)
    {
        // Silent before. A member uploaded their card, watched nothing happen to the empty boxes
        // below it, and had no way to tell whether reading had failed or was never switched on.
        if (!DocIntel.IsConfigured)
        {
            return "Automatic reading is not switched on here — please type the details below.";
        }

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

            // Only a spelling the list offers, since the field is a dropdown: the reader returns
            // whatever is printed on the card, and a value that is not an option would show as
            // nothing chosen and quietly save as blank.
            if (Countries.Resolve(read.CountryRegion) is { } known)
            {
                _record!.EmiratesIdNationality = known;
            }

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
        _uploading = true;
        _uploadPercent = 0;
        _uploadStage = $"Uploading {file.Name}";
        StateHasChanged();
        try
        {
            const int chunkBytes = 64 * 1024;
            using var buffer = new MemoryStream();
            await using (var stream = file.OpenReadStream(MaxCardBytes))
            {
                var chunk = new byte[chunkBytes];
                long soFar = 0;
                int read;
                while ((read = await stream.ReadAsync(chunk)) > 0)
                {
                    await buffer.WriteAsync(chunk.AsMemory(0, read));
                    soFar += read;
                    _uploadPercent = soFar * 100d / Math.Max(1, file.Size);
                    StateHasChanged();
                }
            }

            _uploadPercent = null;
            _uploadStage = "Saving…";
            StateHasChanged();

            _record.NinDocumentBlobId = await Documents.SaveAsync(
                buffer.ToArray(), file.Name, file.ContentType, Client.IpAddress);
        }
        finally
        {
            _busy = false;
            _uploading = false;
            _uploadPercent = null;
            _uploadStage = null;
        }
    }
}
