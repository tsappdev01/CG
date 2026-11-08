using Microsoft.EntityFrameworkCore;
using CGTOOL.Web.Data;

namespace CGTOOL.Web.Data.Governance;

/// <summary>Which screen a member should be on. Derived from what they have answered rather than
/// stored, so a half-finished onboarding resumes where it actually stopped and cannot disagree
/// with itself.</summary>
public enum OnboardingStep { Welcome, EmiratesId, Dfm, Done }

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
    Task ReportEmiratesIdIssueAsync(MemberOnboarding onboarding, string issue, string actorName, CancellationToken ct = default);
    Task SubmitEmiratesIdAsync(MemberOnboarding onboarding, string actorName, CancellationToken ct = default);
    Task ConfirmDfmAsync(MemberOnboarding onboarding, string actorName, CancellationToken ct = default);
    Task ReportDfmIssueAsync(MemberOnboarding onboarding, string issue, string actorName, CancellationToken ct = default);
    Task SubmitNinAsync(MemberOnboarding onboarding, string actorName, CancellationToken ct = default);
    Task RecordNoNinAsync(MemberOnboarding onboarding, string actorName, CancellationToken ct = default);

    /// <summary>Finishes onboarding and copies what was captured into My Workspace.</summary>
    Task CompleteAsync(MemberOnboarding onboarding, Member member, string actorName, CancellationToken ct = default);
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
        await PrefillFromReferenceAsync(onboarding, member, ct);

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
        await PrefillFromReferenceAsync(refreshed, member, ct);

        if (emiratesIdOpen && refreshed.EmiratesIdSource == OnboardingSource.Reference)
        {
            onboarding.EmiratesIdSource = refreshed.EmiratesIdSource;
            onboarding.EmiratesIdNumber = refreshed.EmiratesIdNumber;
            onboarding.EmiratesIdHolderName = refreshed.EmiratesIdHolderName;
            onboarding.EmiratesIdNationality = refreshed.EmiratesIdNationality;
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

    private async Task PrefillFromReferenceAsync(MemberOnboarding onboarding, Member member, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(member.Email))
        {
            logger.LogInformation(
                "Onboarding for member {MemberId}: no email on the member record, so nothing can be matched " +
                "against the imported identity data and the member will be asked for their details.",
                member.Id);
            return;
        }

        if (await known.FindEmiratesIdAsync(member.Email, ct) is { } card)
        {
            onboarding.EmiratesIdSource = OnboardingSource.Reference;
            onboarding.EmiratesIdNumber = card.Number;
            onboarding.EmiratesIdHolderName = card.NameOnCard;
            onboarding.EmiratesIdNationality = card.Nationality;

            // The scan, where one was loaded in bulk. The member sees the card they are being
            // asked about rather than just its number.
            onboarding.EmiratesIdBlobId = card.BlobId;
            onboarding.EmiratesIdStatus = EmiratesIdOnboardingStatus.PendingConfirmation;
        }

        if (await known.FindDfmAsync(member.Email, ct) is { } dfm)
        {
            onboarding.DfmSource = OnboardingSource.Reference;
            onboarding.NinNumber = dfm.Nin;
            onboarding.NinRegisteredName = dfm.RegisteredName;
            onboarding.DfmStatus = DfmOnboardingStatus.PendingConfirmation;
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
        await LogAsync(actorName, o, "Confirmed the Emirates ID details held in the IPO records", ct);
    }

    public async Task ReportEmiratesIdIssueAsync(MemberOnboarding o, string issue, string actorName, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(o);

        // The record itself is untouched -- this is a report, and correcting IPO is somebody
        // else's job through their own process.
        o.EmiratesIdReportedIssue = issue;
        o.EmiratesIdReportedAtUtc = DateTime.UtcNow;
        await writer.UpdateAsync(o);
        await LogAsync(actorName, o, $"Reported a problem with the Emirates ID on file: {issue}", ct);
    }

    public async Task SubmitEmiratesIdAsync(MemberOnboarding o, string actorName, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(o);

        o.EmiratesIdSource = OnboardingSource.EmployeeSubmitted;

        // Pending verification, not confirmed. The member supplying a card is not the same as
        // anybody having checked it.
        o.EmiratesIdStatus = EmiratesIdOnboardingStatus.PendingVerification;
        await writer.UpdateAsync(o);
        await LogAsync(actorName, o, "Supplied their Emirates ID; recorded as pending verification", ct);
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

    public async Task ReportDfmIssueAsync(MemberOnboarding o, string issue, string actorName, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(o);

        o.DfmReportedIssue = issue;
        o.DfmReportedAtUtc = DateTime.UtcNow;
        await writer.UpdateAsync(o);
        await LogAsync(actorName, o, $"Reported a problem with the DFM details on file: {issue}", ct);
    }

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
        if (o.EmiratesIdNumber is null && o.EmiratesIdBlobId is null) return;

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var existing = await db.MemberDocuments.AsNoTracking()
            .FirstOrDefaultAsync(d => d.MemberId == member.Id && d.Kind == MemberDocumentKind.EmiratesId, ct);

        if (existing is not null)
        {
            var changed = false;
            if (string.IsNullOrWhiteSpace(existing.DocumentNumber) && o.EmiratesIdNumber is { Length: > 0 })
            {
                existing.DocumentNumber = o.EmiratesIdNumber; changed = true;
            }
            if (string.IsNullOrWhiteSpace(existing.HolderName) && o.EmiratesIdHolderName is { Length: > 0 })
            {
                existing.HolderName = o.EmiratesIdHolderName; changed = true;
            }
            if (string.IsNullOrWhiteSpace(existing.Nationality) && o.EmiratesIdNationality is { Length: > 0 })
            {
                existing.Nationality = o.EmiratesIdNationality; changed = true;
            }
            if (existing.ExpiryDate is null && o.EmiratesIdExpiryDate is not null)
            {
                existing.ExpiryDate = o.EmiratesIdExpiryDate; changed = true;
            }
            if (existing.BlobId is null && o.EmiratesIdBlobId is not null)
            {
                existing.BlobId = o.EmiratesIdBlobId; changed = true;
            }

            if (changed) await documentWriter.UpdateAsync(existing);
            return;
        }

        await documentWriter.InsertAsync(new MemberDocument
        {
            MemberId = member.Id,
            Kind = MemberDocumentKind.EmiratesId,
            // The front of the card is the document; the back is kept on the onboarding record.
            BlobId = o.EmiratesIdBlobId,
            DocumentNumber = o.EmiratesIdNumber,
            HolderName = o.EmiratesIdHolderName,
            Nationality = o.EmiratesIdNationality,
            ExpiryDate = o.EmiratesIdExpiryDate,
            OriginalFileName = o.EmiratesIdBlobId is null ? null : "Emirates ID (from onboarding)",
            UploadedAtUtc = DateTime.UtcNow,
        });
    }

    private Task LogAsync(string actorName, MemberOnboarding o, string detail, CancellationToken ct) =>
        auditLog.LogAsync(actorName, AuditAction.Update, nameof(MemberOnboarding), o.Id.ToString(), detail);
}
