using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using CGTOOL.Web.Data;

namespace CGTOOL.Web.Data.Governance;

/// <summary>
/// What the organisation already knows about a person's Emirates ID and DFM registration, loaded
/// once from the employee spreadsheet and matched on email address.
///
/// Its own table, separate from the member record and from onboarding, because it is the thing
/// being confirmed. Onboarding shows a member what is held here and asks whether it is right; what
/// they answer, and anything they supply themselves, is written to their onboarding record
/// instead. This row is never changed by that, which is the point -- a member saying "that is
/// wrong" raises a report for somebody to act on, it does not silently rewrite the reference data
/// underneath everyone else.
/// </summary>
public class MemberIdentityReference
{
    public int Id { get; set; }

    /// <summary>The match key. Email, because it is what the spreadsheet and the member record
    /// have in common and the only thing in the file that is reliably unique.</summary>
    [MaxLength(320)]
    public string Email { get; set; } = string.Empty;

    [MaxLength(200)]
    public string? FullName { get; set; }

    [MaxLength(200)]
    public string? CompanyName { get; set; }

    /// <summary>National Investor Number registered with DFM.</summary>
    [MaxLength(100)]
    public string? Nin { get; set; }

    /// <summary>The name exactly as it appears on the card, which is often longer than the name
    /// the person goes by -- that difference is the whole reason it is shown for confirmation.</summary>
    [MaxLength(200)]
    public string? NameOnEmiratesId { get; set; }

    [MaxLength(100)]
    public string? EmiratesIdNumber { get; set; }

    [MaxLength(80)]
    public string? Nationality { get; set; }

    /// <summary>The holder's name in Arabic, as the card prints it beside the English. Kept
    /// because the card carries it and a reviewer comparing a scan against a record is comparing
    /// both halves; nothing reads it for matching.</summary>
    [MaxLength(200)]
    public string? NameOnEmiratesIdArabic { get; set; }

    /// <summary>When the card expires. A declaration cannot be submitted on an expired Emirates
    /// ID, so knowing this before the member reaches that point is the difference between asking
    /// them for a new copy during onboarding and stopping them at submission.</summary>
    public DateTime? EmiratesIdExpiryDate { get; set; }

    /// <summary>A scan of the card, loaded in bulk and matched to this row by its file name.
    ///
    /// The files are named after the Emirates ID number, which is why they can be attached without
    /// anyone mapping them by hand -- and why this column hangs off the reference row rather than
    /// off the member: the number is what the file knows about, and the reference row is the only
    /// thing that knows which number belongs to which person.</summary>
    public int? EmiratesIdBlobId { get; set; }

    [MaxLength(260)]
    public string? EmiratesIdFileName { get; set; }

    public DateTime ImportedAtUtc { get; set; } = DateTime.UtcNow;

    [MaxLength(260)]
    public string? SourceFileName { get; set; }
}

/// <summary>What is on file for a person, for onboarding to read back to them.</summary>
public sealed record KnownEmiratesId(
    string? NameOnCard, string? Number, string? Nationality, int? BlobId,
    string? NameOnCardArabic = null, DateTime? ExpiryDate = null);

public sealed record KnownDfm(string? Nin, string? RegisteredName);

public interface IMemberIdentityLookup
{
    Task<KnownEmiratesId?> FindEmiratesIdAsync(string? email, CancellationToken ct = default);
    Task<KnownDfm?> FindDfmAsync(string? email, CancellationToken ct = default);
}

/// <summary>Reads the imported reference data. A person with no row, or a row with the relevant
/// half blank, is simply "not found" -- which is the path where onboarding asks them.</summary>
public class MemberIdentityLookup(IDbContextFactory<ApplicationDbContext> dbFactory) : IMemberIdentityLookup
{
    public async Task<KnownEmiratesId?> FindEmiratesIdAsync(string? email, CancellationToken ct = default)
    {
        var row = await FindAsync(email, ct);
        return row is null || string.IsNullOrWhiteSpace(row.EmiratesIdNumber)
            ? null
            : new KnownEmiratesId(
                row.NameOnEmiratesId ?? row.FullName,
                row.EmiratesIdNumber,
                row.Nationality,
                row.EmiratesIdBlobId,
                row.NameOnEmiratesIdArabic,
                row.EmiratesIdExpiryDate);
    }

    public async Task<KnownDfm?> FindDfmAsync(string? email, CancellationToken ct = default)
    {
        var row = await FindAsync(email, ct);
        return row is null || string.IsNullOrWhiteSpace(row.Nin)
            ? null
            : new KnownDfm(row.Nin, row.NameOnEmiratesId ?? row.FullName);
    }

    private async Task<MemberIdentityReference?> FindAsync(string? email, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(email)) return null;

        var key = email.Trim();
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.MemberIdentityReferences.AsNoTracking()
            .FirstOrDefaultAsync(r => r.Email == key, ct);
    }
}
