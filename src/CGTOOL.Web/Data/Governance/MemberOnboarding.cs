using System.ComponentModel.DataAnnotations;

namespace CGTOOL.Web.Data.Governance;

/// <summary>Where a member is in the one-time onboarding.</summary>
public enum OnboardingStatus { Pending, InProgress, Completed }

/// <summary>What is known about the member's Emirates ID, and how sure we are of it.</summary>
public enum EmiratesIdOnboardingStatus
{
    NotAvailable,

    /// <summary>Found in the IPO records and waiting for the member to say it is right.
    ///
    /// Not one of the four statuses the specification lists for Emirates ID, and added for the same
    /// reason its own DFM list has "Pending Confirmation": "we found it and are waiting to be told
    /// it is correct" is a real state, and without it that case is indistinguishable from having
    /// nothing on file.</summary>
    PendingConfirmation,

    /// <summary>Nothing on file and the member has been asked to upload it.</summary>
    PendingUpload,

    /// <summary>The member supplied it. Theirs is the only word for it so far.</summary>
    PendingVerification,

    /// <summary>The member confirmed what the IPO records already said.</summary>
    Confirmed,
}

public enum DfmOnboardingStatus { NotAvailable, PendingConfirmation, PendingVerification, Confirmed }

/// <summary>The member's answer to "do you have a NIN?".</summary>
public enum NinResponse { NotAsked, HasNin, NoNin }

/// <summary>Where a piece of information came from. Not the same question as whether anyone has
/// checked it.</summary>
public enum OnboardingSource
{
    None,

    /// <summary>From the identity data loaded from the employee spreadsheet.</summary>
    Reference,

    /// <summary>The member supplied it themselves during onboarding.</summary>
    EmployeeSubmitted,
}

/// <summary>
/// One member's onboarding: what they were shown, what they confirmed, what they supplied, and when.
///
/// Its own table rather than columns on Member, because this is a record of an event -- a person
/// sat down once and answered some questions -- and because the thing it mostly holds is
/// provenance. Nearly every field here has a companion saying where it came from and whether
/// anyone has checked it, and spreading twenty of those across the member record would bury the
/// member record.
///
/// The distinction the specification is firmest about: confirming what the IPO records say and
/// supplying something new are different acts with different weight, and neither one means the
/// information has been independently verified. So "the member confirmed it" (…ConfirmedAtUtc),
/// "the member supplied it" (Source) and "somebody checked it" (VerifiedAtUtc) are three separate
/// columns, and nothing sets the third except a person.
/// </summary>
public class MemberOnboarding
{
    public int Id { get; set; }

    public int MemberId { get; set; }
    public Member? Member { get; set; }

    public OnboardingStatus Status { get; set; } = OnboardingStatus.Pending;

    public DateTime? StartedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }

    // ---------------------------------------------------------------- Emirates ID

    public EmiratesIdOnboardingStatus EmiratesIdStatus { get; set; } = EmiratesIdOnboardingStatus.NotAvailable;

    public OnboardingSource EmiratesIdSource { get; set; } = OnboardingSource.None;

    [MaxLength(100)]
    public string? EmiratesIdNumber { get; set; }

    [MaxLength(200)]
    public string? EmiratesIdHolderName { get; set; }

    [MaxLength(200)]
    public string? EmiratesIdHolderNameArabic { get; set; }

    [MaxLength(80)]
    public string? EmiratesIdNationality { get; set; }

    public DateTime? EmiratesIdExpiryDate { get; set; }

    /// <summary>When the member said the details were right. Null if they never got that far, or
    /// if they reported a problem instead.</summary>
    public DateTime? EmiratesIdConfirmedAtUtc { get; set; }

    /// <summary>What the member said was wrong with the record they were shown. The IPO record is
    /// never changed from here -- this is a report for somebody to act on.</summary>
    [MaxLength(1000)]
    public string? EmiratesIdReportedIssue { get; set; }

    public DateTime? EmiratesIdReportedAtUtc { get; set; }

    /// <summary>The card, as one PDF. Either the copy already held for this member, or the one
    /// they supplied. Not a front and a back: the scans the organisation holds are a single file
    /// per person with both sides in it, and storing a member's in two pieces would leave whoever
    /// checks them unable to compare the two sources.</summary>
    public int? EmiratesIdBlobId { get; set; }

    /// <summary>Set only by a person with the authority to say so. Uploading a card or confirming
    /// a record does not set it.</summary>
    public DateTime? EmiratesIdVerifiedAtUtc { get; set; }

    [MaxLength(160)]
    public string? EmiratesIdVerifiedByName { get; set; }

    // ---------------------------------------------------------------- DFM / NIN

    public DfmOnboardingStatus DfmStatus { get; set; } = DfmOnboardingStatus.NotAvailable;

    public OnboardingSource DfmSource { get; set; } = OnboardingSource.None;

    /// <summary>Whether the member says they have a NIN. Asked only when the DFM records had
    /// nothing, and a "no" is itself an answer worth keeping.</summary>
    public NinResponse NinResponse { get; set; } = NinResponse.NotAsked;

    [MaxLength(100)]
    public string? NinNumber { get; set; }

    [MaxLength(200)]
    public string? NinRegisteredName { get; set; }

    [MaxLength(500)]
    public string? DfmOtherInformation { get; set; }

    public DateTime? DfmConfirmedAtUtc { get; set; }

    [MaxLength(1000)]
    public string? DfmReportedIssue { get; set; }

    public DateTime? DfmReportedAtUtc { get; set; }

    /// <summary>The NIN confirmation or DFM statement, if the member had one to hand.</summary>
    public int? NinDocumentBlobId { get; set; }

    public DateTime? DfmVerifiedAtUtc { get; set; }

    [MaxLength(160)]
    public string? DfmVerifiedByName { get; set; }

    // ---------------------------------------------------------------- derived

    /// <summary>The Emirates ID step is done when the member has either confirmed what was on file
    /// or supplied their own -- or reported a problem with what they were shown, which is an answer
    /// and should not trap them on the step.</summary>
    public bool EmiratesIdSettled =>
        EmiratesIdStatus is EmiratesIdOnboardingStatus.Confirmed or EmiratesIdOnboardingStatus.PendingVerification
        || EmiratesIdReportedAtUtc is not null;

    public bool DfmSettled =>
        DfmStatus is DfmOnboardingStatus.Confirmed or DfmOnboardingStatus.PendingVerification
        || NinResponse is NinResponse.NoNin
        || DfmReportedAtUtc is not null;
}
