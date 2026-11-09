using Microsoft.EntityFrameworkCore;
using CGTOOL.Web.Data;

namespace CGTOOL.Web.Data.Governance;

/// <summary>Which screen a member should be on. Derived from what they have answered rather than
/// stored, so a half-finished onboarding resumes where it actually stopped and cannot disagree
/// with itself.</summary>
public enum OnboardingStep { Welcome, EmiratesId, Dfm, Done }

/// <summary>What the member was shown before they edited it.
///
/// Kept so a correction can record which field changed and from what, rather than only that
/// something changed. Whoever checks the card afterwards needs to know what is in dispute, and
/// "the member edited their details" does not tell them.</summary>
public sealed record EmiratesIdValues(string? Number, string? HolderName, string? Nationality, DateTime? Expiry, int? BlobId)
{
    public static EmiratesIdValues From(MemberOnboarding o) => new(
        o.EmiratesIdNumber, o.EmiratesIdHolderName, o.EmiratesIdNationality, o.EmiratesIdExpiryDate,
        o.EmiratesIdBlobId);

    public IReadOnlyList<string> ChangesTo(MemberOnboarding o)
    {
        var changes = new List<string>();
        OnboardingChange.Add(changes, "Emirates ID number", Number, o.EmiratesIdNumber);
        OnboardingChange.Add(changes, "name on the card", HolderName, o.EmiratesIdHolderName);
        OnboardingChange.Add(changes, "nationality", Nationality, o.EmiratesIdNationality);
        OnboardingChange.Add(changes, "expiry date",
            Expiry?.ToString("dd/MM/yyyy"), o.EmiratesIdExpiryDate?.ToString("dd/MM/yyyy"));

        // A replaced copy of the card is a correction in its own right. Without this, somebody
        // who uploaded a better scan and changed no field had it recorded as a confirmation --
        // and the new copy never reached My Workspace, which only takes a card on a correction.
        if (BlobId != o.EmiratesIdBlobId)
        {
            changes.Add(BlobId is null ? "copy of the card supplied" : "copy of the card replaced");
        }

        return changes;
    }
}

public sealed record DfmValues(string? Nin, string? RegisteredName)
{
    public static DfmValues From(MemberOnboarding o) => new(o.NinNumber, o.NinRegisteredName);

    public IReadOnlyList<string> ChangesTo(MemberOnboarding o)
    {
        var changes = new List<string>();
        OnboardingChange.Add(changes, "NIN", Nin, o.NinNumber);
        OnboardingChange.Add(changes, "registered name", RegisteredName, o.NinRegisteredName);
        return changes;
    }
}

internal static class OnboardingChange
{
    /// <summary>Records a field that moved. Trimmed on both sides before comparing, so whitespace
    /// a member's paste brought in does not count as a correction and send a card for checking
    /// that nobody disputed.</summary>
    public static void Add(List<string> into, string what, string? before, string? after)
    {
        if (string.Equals(before?.Trim() ?? "", after?.Trim() ?? "", StringComparison.Ordinal)) return;
        into.Add($"{what}: \"{Shown(before)}\" to \"{Shown(after)}\"");
    }

    private static string Shown(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "blank" : value.Trim();
}

public interface IMemberOnboardingService
{
    /// <summary>Whether this member has to onboard before they can do anything else.</summary>
    Task<bool> IsRequiredAsync(int memberId, CancellationToken ct = default);

    /// <summary>The member's onboarding record, created and pre-filled from the IPO records the
    /// first time it is asked for.</summary>
    Task<MemberOnboarding> GetOrCreateAsync(Member member, CancellationToken ct = default);

    OnboardingStep NextStep(MemberOnboarding onboarding);

    Task StartAsync(MemberOnboarding onboarding, string actorName, CancellationToken ct = default);
    Task ConfirmEmiratesIdAsync(MemberOnboarding onboarding, string actorName, CancellationToken ct = default);
    /// <summary>The member edited the details they were shown and saved them.</summary>
    Task CorrectEmiratesIdAsync(MemberOnboarding onboarding, EmiratesIdValues before, string actorName, CancellationToken ct = default);
    Task SubmitEmiratesIdAsync(MemberOnboarding onboarding, string actorName, CancellationToken ct = default);
    Task ConfirmDfmAsync(MemberOnboarding onboarding, string actorName, CancellationToken ct = default);
    Task CorrectDfmAsync(MemberOnboarding onboarding, DfmValues before, string actorName, CancellationToken ct = default);
    Task SubmitNinAsync(MemberOnboarding onboarding, string actorName, CancellationToken ct = default);
    Task RecordNoNinAsync(MemberOnboarding onboarding, string actorName, CancellationToken ct = default);

    /// <summary>Finishes onboarding and copies what was captured into My Workspace.</summary>
    Task CompleteAsync(MemberOnboarding onboarding, Member member, string actorName, CancellationToken ct = default);

    /// <summary>Puts the member back at the start, so they are asked everything again.</summary>
    Task RestartAsync(int memberId, string actorName, CancellationToken ct = default);
}

/// <summary>
/// Drives the one-time onboarding.
///
/// The flow is in one place rather than spread across the screens because the rules that matter are
/// about what may be recorded, not about what is on screen: the IPO records are never corrected
/// from here, confirming an existing record and supplying a new one are different acts, and neither
/// amounts to verification. A screen that set these fields itself would get that wrong eventually.
/// </summary>
public class MemberOnboardingService(
    IDbContextFactory<ApplicationDbContext> dbFactory,
    IMemberOnboardingWriter writer,
    IMemberDocumentWriter documentWriter,
    IMemberDfmProfileWriter dfmWriter,
    IMemberWriter memberWriter,
    IMemberIdentityLookup known,
    IAuditLogger auditLog,
    ILogger<MemberOnboardingService> logger) : IMemberOnboardingService
{
    public async Task<bool> IsRequiredAsync(int memberId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var member = await db.Members.AsNoTracking()
            .Where(m => m.Id == memberId)
            .Select(m => new { m.OnboardingRequired })
            .FirstOrDefaultAsync(ct);

        // A member nobody has flagged is not dragged through it.
        if (member is null || !member.OnboardingRequired) return false;

        var status = await db.MemberOnboardings.AsNoTracking()
            .Where(o => o.MemberId == memberId)
            .Select(o => (OnboardingStatus?)o.Status)
            .FirstOrDefaultAsync(ct);

        return status != OnboardingStatus.Completed;
    }

    public async Task<MemberOnboarding> GetOrCreateAsync(Member member, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(member);

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var existing = await db.MemberOnboardings.AsNoTracking()
            .FirstOrDefaultAsync(o => o.MemberId == member.Id, ct);

        if (existing is not null)
        {
            await FillWhatIsStillUnansweredAsync(existing, member, ct);
            return existing;
        }

        var onboarding = new MemberOnboarding { MemberId = member.Id };
        await PrefillFromWhatWeHoldAsync(onboarding, member, ct);

        onboarding.Id = await writer.InsertAsync(onboarding);
        return onboarding;
    }

    /// <summary>Picks up reference data that arrived after this member started.
    ///
    /// The prefill used to run only when the record was created, on the reasoning that the member
    /// should not have the question change under them between the screen and the answer. That is
    /// right for a step they are part-way through and wrong for one they have not reached: a
    /// member who opened onboarding before their spreadsheet was loaded was put on the "supply
    /// your own" path and stayed there permanently, even once their details were on file.
    ///
    /// So it fills only what is still both blank and unanswered. A step the member has confirmed,
    /// reported a problem with, or supplied something for is left exactly as it is.</summary>
    private async Task FillWhatIsStillUnansweredAsync(MemberOnboarding onboarding, Member member, CancellationToken ct)
    {
        var emiratesIdOpen = onboarding.EmiratesIdSource == OnboardingSource.None && !onboarding.EmiratesIdSettled;
        var dfmOpen = onboarding.DfmSource == OnboardingSource.None && !onboarding.DfmSettled;
        if (!emiratesIdOpen && !dfmOpen) return;

        var before = (onboarding.EmiratesIdSource, onboarding.DfmSource);

        var refreshed = new MemberOnboarding { MemberId = member.Id };
        await PrefillFromWhatWeHoldAsync(refreshed, member, ct);

        if (emiratesIdOpen && refreshed.EmiratesIdSource == OnboardingSource.Reference)
        {
            onboarding.EmiratesIdSource = refreshed.EmiratesIdSource;
            onboarding.EmiratesIdNumber = refreshed.EmiratesIdNumber;
            onboarding.EmiratesIdHolderName = refreshed.EmiratesIdHolderName;
            onboarding.EmiratesIdNationality = refreshed.EmiratesIdNationality;
            onboarding.EmiratesIdHolderNameArabic = refreshed.EmiratesIdHolderNameArabic;
            onboarding.EmiratesIdExpiryDate = refreshed.EmiratesIdExpiryDate;
            onboarding.EmiratesIdBlobId = refreshed.EmiratesIdBlobId;
            onboarding.EmiratesIdStatus = EmiratesIdOnboardingStatus.PendingConfirmation;
        }

        if (dfmOpen && refreshed.DfmSource == OnboardingSource.Reference)
        {
            onboarding.DfmSource = refreshed.DfmSource;
            onboarding.NinNumber = refreshed.NinNumber;
            onboarding.NinRegisteredName = refreshed.NinRegisteredName;
            onboarding.DfmStatus = DfmOnboardingStatus.PendingConfirmation;
        }

        if (before != (onboarding.EmiratesIdSource, onboarding.DfmSource))
        {
            await writer.UpdateAsync(onboarding);
            logger.LogInformation(
                "Onboarding for member {MemberId}: details loaded after they started are now on their screen.",
                member.Id);
        }
    }

    /// <summary>Fills in what the organisation already holds for this member, so onboarding asks
    /// them to check it rather than to supply it again.
    ///
    /// Two places are looked at, and it used to be one. The imported identity data is the first,
    /// and for a while it was the only one -- which meant a member whose Emirates ID was sitting
    /// in their own My Workspace, but who was not in the spreadsheet that was loaded, was told
    /// "we do not have your Emirates ID on file" and asked to upload the document the next screen
    /// would have shown them. Onboarding wrote into My Workspace when it finished and never read
    /// from it, which is the wrong way round for the one question it exists to ask.</summary>
    private async Task PrefillFromWhatWeHoldAsync(MemberOnboarding onboarding, Member member, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        if (string.IsNullOrWhiteSpace(member.Email))
        {
            logger.LogInformation(
                "Onboarding for member {MemberId}: no email on the member record, so nothing can be matched " +
                "against the imported identity data. Their My Workspace record is still used.",
                member.Id);
        }
        else if (await known.FindEmiratesIdAsync(member.Email, ct) is { } card)
        {
            onboarding.EmiratesIdSource = OnboardingSource.Reference;
            onboarding.EmiratesIdNumber = card.Number;
            onboarding.EmiratesIdHolderName = card.NameOnCard;
            onboarding.EmiratesIdNationality = card.Nationality;
            onboarding.EmiratesIdHolderNameArabic = card.NameOnCardArabic;

            // The expiry matters before the member reaches a declaration rather than at it: an
            // expired card is refused at submission, and onboarding is where there is still time
            // to ask for a current copy.
            onboarding.EmiratesIdExpiryDate = card.ExpiryDate;

            // The scan, where one was loaded in bulk. The member sees the card they are being
            // asked about rather than just its number.
            onboarding.EmiratesIdBlobId = card.BlobId;
            onboarding.EmiratesIdStatus = EmiratesIdOnboardingStatus.PendingConfirmation;
        }

        // Nothing imported for them: what they already have in My Workspace is just as much
        // something we hold, and asking for it again is asking twice.
        if (onboarding.EmiratesIdSource == OnboardingSource.None)
        {
            var held = await db.MemberDocuments.AsNoTracking()
                .Where(d => d.MemberId == member.Id
                            && d.Kind == MemberDocumentKind.EmiratesId
                            && (d.BlobId != null || d.DocumentNumber != null))
                .OrderByDescending(d => d.UploadedAtUtc)
                .FirstOrDefaultAsync(ct);

            if (held is not null)
            {
                onboarding.EmiratesIdSource = OnboardingSource.Reference;
                onboarding.EmiratesIdNumber = held.DocumentNumber;
                onboarding.EmiratesIdHolderName = held.HolderName;
                onboarding.EmiratesIdNationality = held.Nationality;
                onboarding.EmiratesIdExpiryDate = held.ExpiryDate;
                onboarding.EmiratesIdBlobId = held.BlobId;
                onboarding.EmiratesIdStatus = EmiratesIdOnboardingStatus.PendingConfirmation;
            }
        }

        if (!string.IsNullOrWhiteSpace(member.Email) && await known.FindDfmAsync(member.Email, ct) is { } dfm)
        {
            onboarding.DfmSource = OnboardingSource.Reference;
            onboarding.NinNumber = dfm.Nin;
            onboarding.NinRegisteredName = dfm.RegisteredName;
            onboarding.DfmStatus = DfmOnboardingStatus.PendingConfirmation;
        }

        if (onboarding.DfmSource == OnboardingSource.None)
        {
            var profile = await db.MemberDfmProfiles.AsNoTracking()
                .FirstOrDefaultAsync(p => p.MemberId == member.Id && p.InvestorNumber != null, ct);

            if (profile is not null)
            {
                onboarding.DfmSource = OnboardingSource.Reference;
                onboarding.NinNumber = profile.InvestorNumber;
                onboarding.NinRegisteredName = profile.FullName;
                onboarding.DfmStatus = DfmOnboardingStatus.PendingConfirmation;
            }
        }
    }

    public OnboardingStep NextStep(MemberOnboarding onboarding)
    {
        ArgumentNullException.ThrowIfNull(onboarding);

        if (onboarding.Status == OnboardingStatus.Completed) return OnboardingStep.Done;
        if (onboarding.StartedAtUtc is null) return OnboardingStep.Welcome;
        if (!onboarding.EmiratesIdSettled) return OnboardingStep.EmiratesId;
        if (!onboarding.DfmSettled) return OnboardingStep.Dfm;
        return OnboardingStep.Done;
    }

    public async Task StartAsync(MemberOnboarding o, string actorName, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(o);
        if (o.StartedAtUtc is not null) return;

        o.StartedAtUtc = DateTime.UtcNow;
        o.Status = OnboardingStatus.InProgress;

        // Nothing on file means the member is being asked to supply it.
        if (o.EmiratesIdSource == OnboardingSource.None)
        {
            o.EmiratesIdStatus = EmiratesIdOnboardingStatus.PendingUpload;
        }

        await writer.UpdateAsync(o);
        await LogAsync(actorName, o, "Started onboarding", ct);
    }

    public async Task ConfirmEmiratesIdAsync(MemberOnboarding o, string actorName, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(o);

        o.EmiratesIdConfirmedAtUtc = DateTime.UtcNow;
        o.EmiratesIdStatus = EmiratesIdOnboardingStatus.Confirmed;
        await writer.UpdateAsync(o);
        await SyncEmiratesIdAsync(o, overwrite: false, ct);
        await LogAsync(actorName, o, "Confirmed the Emirates ID details held in the IPO records", ct);
    }

    /// <summary>Takes the member's own version of their Emirates ID details.
    ///
    /// This replaced a "report a problem" box. Asking someone to describe in prose what was wrong
    /// with a number in front of them, and then leaving the wrong number on the screen, put the
    /// work on whoever read the report and left the member's own record still wrong. Letting them
    /// type the right value is the same act with the answer already in it.
    ///
    /// What they type becomes the record and is marked for checking. It does not overwrite the
    /// reference data it came from: that belongs to the system that supplied it, and the summary
    /// of what moved is kept so whoever checks can see both.</summary>
    public async Task CorrectEmiratesIdAsync(MemberOnboarding o, EmiratesIdValues before, string actorName, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(o);
        ArgumentNullException.ThrowIfNull(before);

        var changes = before.ChangesTo(o);

        // Opened the form, changed nothing, saved. That is a confirmation, and recording it as a
        // correction would queue a card for checking that nobody has disputed.
        if (changes.Count == 0)
        {
            await ConfirmEmiratesIdAsync(o, actorName, ct);
            return;
        }

        var summary = Truncate(string.Join("; ", changes), 1000);

        o.EmiratesIdSource = OnboardingSource.EmployeeSubmitted;

        // Pending verification, not confirmed -- the same standing as a card they uploaded. A
        // member correcting a field is not somebody having checked it.
        o.EmiratesIdStatus = EmiratesIdOnboardingStatus.PendingVerification;
        o.EmiratesIdConfirmedAtUtc = null;
        o.EmiratesIdReportedIssue = summary;
        o.EmiratesIdReportedAtUtc = DateTime.UtcNow;

        await writer.UpdateAsync(o);
        await SyncEmiratesIdAsync(o, overwrite: true, ct);
        await LogAsync(actorName, o, $"Corrected the Emirates ID details on file -- {summary}", ct);
    }

    public async Task SubmitEmiratesIdAsync(MemberOnboarding o, string actorName, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(o);

        o.EmiratesIdSource = OnboardingSource.EmployeeSubmitted;

        // Pending verification, not confirmed. The member supplying a card is not the same as
        // anybody having checked it.
        o.EmiratesIdStatus = EmiratesIdOnboardingStatus.PendingVerification;
        await writer.UpdateAsync(o);
        await SyncEmiratesIdAsync(o, overwrite: true, ct);
        await LogAsync(actorName, o, "Supplied their Emirates ID; recorded as pending verification", ct);
    }
    /// <summary>What step 1 established goes to My Workspace there and then. See
    /// SeedEmiratesIdAsync for why the member's own entry overwrites and a confirmation does
    /// not.</summary>
    private async Task SyncEmiratesIdAsync(MemberOnboarding o, bool overwrite, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await SeedEmiratesIdAsync(db, o, o.MemberId, overwrite, ct);
    }


    public async Task ConfirmDfmAsync(MemberOnboarding o, string actorName, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(o);

        o.DfmConfirmedAtUtc = DateTime.UtcNow;
        o.DfmStatus = DfmOnboardingStatus.Confirmed;
        o.NinResponse = string.IsNullOrWhiteSpace(o.NinNumber) ? o.NinResponse : NinResponse.HasNin;
        await writer.UpdateAsync(o);
        await LogAsync(actorName, o, "Confirmed the DFM details held in the IPO records", ct);
    }

    public async Task CorrectDfmAsync(MemberOnboarding o, DfmValues before, string actorName, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(o);
        ArgumentNullException.ThrowIfNull(before);

        var changes = before.ChangesTo(o);
        if (changes.Count == 0)
        {
            await ConfirmDfmAsync(o, actorName, ct);
            return;
        }

        var summary = Truncate(string.Join("; ", changes), 1000);

        o.NinResponse = NinResponse.HasNin;
        o.DfmSource = OnboardingSource.EmployeeSubmitted;
        o.DfmStatus = DfmOnboardingStatus.PendingVerification;
        o.DfmConfirmedAtUtc = null;
        o.DfmReportedIssue = summary;
        o.DfmReportedAtUtc = DateTime.UtcNow;

        await writer.UpdateAsync(o);
        await LogAsync(actorName, o, $"Corrected the DFM details on file -- {summary}", ct);
    }

    /// <summary>Keeps a summary inside the column that holds it. A correction that touched every
    /// field on a record with long values can run past 1000 characters, and the write would fail
    /// at the end of onboarding rather than anywhere the member could act on.</summary>
    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..(max - 1)] + "\u2026";

    public async Task SubmitNinAsync(MemberOnboarding o, string actorName, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(o);

        o.NinResponse = NinResponse.HasNin;
        o.DfmSource = OnboardingSource.EmployeeSubmitted;
        o.DfmStatus = DfmOnboardingStatus.PendingVerification;
        await writer.UpdateAsync(o);
        await LogAsync(actorName, o, $"Supplied a NIN ({o.NinNumber}); recorded as pending verification", ct);
    }

    public async Task RecordNoNinAsync(MemberOnboarding o, string actorName, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(o);

        // "No" is an answer and is kept: it is the record of what was asked and what was said,
        // which is what a later compliance question will want.
        o.NinResponse = NinResponse.NoNin;
        o.NinNumber = null;
        o.NinRegisteredName = null;
        o.DfmSource = OnboardingSource.EmployeeSubmitted;
        o.DfmStatus = DfmOnboardingStatus.NotAvailable;
        await writer.UpdateAsync(o);
        await LogAsync(actorName, o, "Stated they do not have a NIN registered with DFM", ct);
    }

    public async Task CompleteAsync(MemberOnboarding o, Member member, string actorName, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(o);
        ArgumentNullException.ThrowIfNull(member);

        o.Status = OnboardingStatus.Completed;
        o.CompletedAtUtc = DateTime.UtcNow;
        await writer.UpdateAsync(o);

        await SeedWorkspaceAsync(o, member, ct);
        await LogAsync(actorName, o, "Completed onboarding", ct);
    }

    /// <summary>Puts what onboarding established into My Workspace, so the member's first
    /// declaration can pre-fill from it and they are not asked for the same card twice.
    ///
    /// It fills blanks and does not overwrite: a member who already has an Emirates ID on file in
    /// My Workspace has one they put there deliberately, and onboarding has no business replacing
    /// it with something it believes more.</summary>
    private async Task SeedWorkspaceAsync(MemberOnboarding o, Member member, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await SeedEmiratesIdAsync(db, o, member.Id, overwrite: false, ct);
        await SeedDfmProfileAsync(db, o, member, ct);
    }

    /// <summary>Puts the Emirates ID onto the member's My Workspace record.
    ///
    /// Runs when the card is confirmed, corrected or supplied -- not only when the whole of
    /// onboarding finishes. Waiting for the end meant a member who filled in their name on the
    /// card in step 1 and then stopped had typed it into nothing: My Workspace, and so every
    /// declaration that pre-fills from it, still showed no name.
    ///
    /// <paramref name="overwrite"/> is the difference between the two callers. Seeding at the end
    /// fills blanks only: a value already in My Workspace is one the member put there. A
    /// correction or an upload on the onboarding screen is the member typing this value now, so
    /// it wins over whatever was held.</summary>
    private async Task SeedEmiratesIdAsync(ApplicationDbContext db, MemberOnboarding o, int memberId, bool overwrite, CancellationToken ct)
    {
        if (o.EmiratesIdNumber is null && o.EmiratesIdBlobId is null && o.EmiratesIdHolderName is null) return;

        var existing = await db.MemberDocuments.AsNoTracking()
            .FirstOrDefaultAsync(d => d.MemberId == memberId && d.Kind == MemberDocumentKind.EmiratesId, ct);

        if (existing is not null)
        {
            var changed = false;
            bool Takes(string? held) => overwrite || string.IsNullOrWhiteSpace(held);

            if (Takes(existing.DocumentNumber) && o.EmiratesIdNumber is { Length: > 0 })
            {
                existing.DocumentNumber = o.EmiratesIdNumber; changed = true;
            }
            if (Takes(existing.HolderName) && o.EmiratesIdHolderName is { Length: > 0 })
            {
                existing.HolderName = o.EmiratesIdHolderName; changed = true;
            }
            if (Takes(existing.Nationality) && o.EmiratesIdNationality is { Length: > 0 })
            {
                existing.Nationality = o.EmiratesIdNationality; changed = true;
            }
            if ((overwrite || existing.ExpiryDate is null) && o.EmiratesIdExpiryDate is not null)
            {
                existing.ExpiryDate = o.EmiratesIdExpiryDate; changed = true;
            }
            if ((overwrite || existing.BlobId is null) && o.EmiratesIdBlobId is not null)
            {
                existing.BlobId = o.EmiratesIdBlobId;
                existing.FilePath = null;
                existing.OriginalFileName = "Emirates ID.pdf";
                existing.UploadedAtUtc = DateTime.UtcNow;
                changed = true;
            }

            if (changed) await documentWriter.UpdateAsync(existing);
            return;
        }

        await documentWriter.InsertAsync(new MemberDocument
        {
            MemberId = memberId,
            Kind = MemberDocumentKind.EmiratesId,
            // The front of the card is the document; the back is kept on the onboarding record.
            BlobId = o.EmiratesIdBlobId,
            DocumentNumber = o.EmiratesIdNumber,
            HolderName = o.EmiratesIdHolderName,
            Nationality = o.EmiratesIdNationality,
            ExpiryDate = o.EmiratesIdExpiryDate,
            // With the extension on it. The preview works out what a file is from its media type
            // now, but the name is also what the member is shown, and "Emirates ID (from
            // onboarding)" with no extension told them nothing about what would open.
            OriginalFileName = o.EmiratesIdBlobId is null ? null : "Emirates ID.pdf",
            UploadedAtUtc = DateTime.UtcNow,
        });
    }

    /// <summary>Puts the DFM details into My Workspace.
    ///
    /// This was missing, and the omission was invisible from the code: onboarding wrote the
    /// Emirates ID and nothing else, so a member who confirmed their NIN in step 2 finished the
    /// flow, opened My Workspace and found the investor panel empty. Nothing had gone wrong that
    /// anything reported -- the answer simply had nowhere to land -- so it read as the system
    /// having lost it, which is the worst way for a one-time flow to fail.
    ///
    /// Fills blanks only, like the Emirates ID half: a member who has already filled this panel in
    /// themselves has the version they meant, and onboarding has no business preferring its
    /// own.</summary>
    private async Task SeedDfmProfileAsync(ApplicationDbContext db, MemberOnboarding o, Member member, CancellationToken ct)
    {
        // "I do not have a NIN" is an answer worth keeping on the onboarding record, but it is not
        // an investor profile: writing a row of blanks would show a DFM registration in My
        // Workspace for a member who has just said they have none.
        if (string.IsNullOrWhiteSpace(o.NinNumber)) return;

        var profile = await db.MemberDfmProfiles.AsNoTracking()
            .FirstOrDefaultAsync(p => p.MemberId == member.Id, ct)
            ?? new MemberDfmProfile { MemberId = member.Id };

        var changed = false;

        if (string.IsNullOrWhiteSpace(profile.InvestorNumber))
        {
            profile.InvestorNumber = o.NinNumber.Trim(); changed = true;
        }

        // The name the exchange has, where onboarding captured one, and the name on the Emirates
        // ID otherwise. Both are the member's legal name; neither is the directory's spelling,
        // which is why this is not taken from Member.FullName.
        var name = FirstNonBlank(o.NinRegisteredName, o.EmiratesIdHolderName);
        if (string.IsNullOrWhiteSpace(profile.FullName) && name is not null)
        {
            profile.FullName = name; changed = true;
        }

        // Only a spelling the list offers. Nationality is a <select> in My Workspace, so a value
        // that is not one of the options renders as "nothing chosen" and would be written back as
        // blank the first time the member saved the panel for any other reason.
        if (string.IsNullOrWhiteSpace(profile.Nationality)
            && Countries.Resolve(o.EmiratesIdNationality) is { } known)
        {
            profile.Nationality = known; changed = true;
        }

        if (string.IsNullOrWhiteSpace(profile.EmailAddress) && !string.IsNullOrWhiteSpace(member.Email))
        {
            profile.EmailAddress = member.Email; changed = true;
        }

        if (changed) await dfmWriter.SaveAsync(profile);
    }

    private static string? FirstNonBlank(params string?[] values) =>
        values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))?.Trim();

    /// <summary>Puts the member back at the start.
    ///
    /// The record is cleared rather than deleted: its id is what the audit entries for the
    /// previous run point at, and removing the row would leave that history pointing at nothing.
    /// The reference data is deliberately not copied back in here -- GetOrCreateAsync fills it on
    /// the way in, so a restart picks up whatever is on file now rather than what was on file when
    /// they first went through.</summary>
    public async Task RestartAsync(int memberId, string actorName, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var member = await db.Members.AsNoTracking().FirstOrDefaultAsync(m => m.Id == memberId, ct)
            ?? throw new InvalidOperationException($"There is no member {memberId} to restart onboarding for.");

        var existing = await db.MemberOnboardings.AsNoTracking()
            .FirstOrDefaultAsync(o => o.MemberId == memberId, ct);

        if (existing is not null)
        {
            Clear(existing);
            await writer.UpdateAsync(existing);
        }

        // The flag is what the gate actually reads. Clearing the record without setting it would
        // reset everything and then never ask the member anything.
        if (!member.OnboardingRequired)
        {
            member.OnboardingRequired = true;
            await memberWriter.UpdateAsync(member);
        }

        await auditLog.LogAsync(actorName, AuditAction.Update, nameof(MemberOnboarding),
            existing?.Id.ToString() ?? memberId.ToString(),
            $"Restarted onboarding for {member.FullName}; they will be asked for their details again from the beginning.");
    }

    /// <summary>Back to the state a record that has never been started is in. Field by field
    /// rather than by replacing the object, because the row keeps its id.</summary>
    private static void Clear(MemberOnboarding o)
    {
        o.Status = OnboardingStatus.Pending;
        o.StartedAtUtc = null;
        o.CompletedAtUtc = null;

        o.EmiratesIdStatus = EmiratesIdOnboardingStatus.NotAvailable;
        o.EmiratesIdSource = OnboardingSource.None;
        o.EmiratesIdNumber = null;
        o.EmiratesIdHolderName = null;
        o.EmiratesIdHolderNameArabic = null;
        o.EmiratesIdNationality = null;
        o.EmiratesIdExpiryDate = null;
        o.EmiratesIdConfirmedAtUtc = null;
        o.EmiratesIdReportedIssue = null;
        o.EmiratesIdReportedAtUtc = null;

        // Only the link is dropped. The file itself stays in DocumentBlobs, and anything already
        // copied into My Workspace keeps pointing at it: a restart re-asks the questions, it does
        // not throw away the member's documents.
        o.EmiratesIdBlobId = null;
        o.EmiratesIdVerifiedAtUtc = null;
        o.EmiratesIdVerifiedByName = null;

        o.DfmStatus = DfmOnboardingStatus.NotAvailable;
        o.DfmSource = OnboardingSource.None;
        o.NinResponse = NinResponse.NotAsked;
        o.NinNumber = null;
        o.NinRegisteredName = null;
        o.DfmOtherInformation = null;
        o.DfmConfirmedAtUtc = null;
        o.DfmReportedIssue = null;
        o.DfmReportedAtUtc = null;
        o.NinDocumentBlobId = null;
        o.DfmVerifiedAtUtc = null;
        o.DfmVerifiedByName = null;
    }

    private Task LogAsync(string actorName, MemberOnboarding o, string detail, CancellationToken ct) =>
        auditLog.LogAsync(actorName, AuditAction.Update, nameof(MemberOnboarding), o.Id.ToString(), detail);
}
