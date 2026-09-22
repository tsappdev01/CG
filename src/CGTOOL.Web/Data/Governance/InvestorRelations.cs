using System.ComponentModel.DataAnnotations;

namespace CGTOOL.Web.Data.Governance;

/// <summary>One upload of the DIC Share Register (Investor Relations &gt; Share Register &gt; Upload) --
/// every upload is kept as its own dated snapshot rather than overwriting the previous one, so the
/// register can be viewed/printed "as on" any previously uploaded date and the upload history itself
/// is the change log.</summary>
public class ShareholderRegisterUpload
{
    public int Id { get; set; }

    /// <summary>The settlement/as-on date this snapshot represents -- confirmed by the uploader, not
    /// trusted from the file's own banner text.</summary>
    public DateOnly AsOnDate { get; set; }

    [Required, MaxLength(260)]
    public string FileName { get; set; } = string.Empty;

    public int RecordCount { get; set; }

    public DateTime UploadedAtUtc { get; set; } = DateTime.UtcNow;

    [Required, MaxLength(160)]
    public string UploadedByName { get; set; } = string.Empty;

    public List<ShareholderRecord> Records { get; set; } = [];
}

/// <summary>One row of a ShareholderRegisterUpload snapshot -- field set/order matches the DIC Share
/// Book export exactly (Serial No. through Payment Preference).</summary>
public class ShareholderRecord
{
    public int Id { get; set; }

    public int ShareholderRegisterUploadId { get; set; }
    public ShareholderRegisterUpload? ShareholderRegisterUpload { get; set; }

    public int? SerialNo { get; set; }

    [Required, MaxLength(40)]
    public string Nin { get; set; } = string.Empty;

    [MaxLength(20)]
    public string? CdsUpdated { get; set; }

    [MaxLength(200)]
    public string? Name { get; set; }

    [MaxLength(200)]
    public string? EnglishName { get; set; }

    [MaxLength(40)]
    public string? LifeStatus { get; set; }

    [MaxLength(40)]
    public string? ClientType { get; set; }

    [MaxLength(40)]
    public string? PassportNo { get; set; }

    [MaxLength(40)]
    public string? FamilyId { get; set; }

    [MaxLength(40)]
    public string? NationalId { get; set; }

    [MaxLength(40)]
    public string? VisaNo { get; set; }

    [MaxLength(40)]
    public string? CommercialLicenseNo { get; set; }

    [MaxLength(40)]
    public string? TradeRegistrationNo { get; set; }

    [MaxLength(20)]
    public string? Citizenship { get; set; }

    [MaxLength(120)]
    public string? CitizenshipDescription { get; set; }

    [MaxLength(40)]
    public string? PoBox { get; set; }

    [MaxLength(80)]
    public string? City { get; set; }

    [MaxLength(20)]
    public string? CountryCode { get; set; }

    [MaxLength(120)]
    public string? CountryName { get; set; }

    [MaxLength(240)]
    public string? Address1 { get; set; }

    [MaxLength(240)]
    public string? Address2 { get; set; }

    [MaxLength(240)]
    public string? Address3 { get; set; }

    [MaxLength(40)]
    public string? Phone1 { get; set; }

    [MaxLength(40)]
    public string? Phone2 { get; set; }

    [MaxLength(40)]
    public string? Fax { get; set; }

    [MaxLength(160)]
    public string? Email { get; set; }

    public decimal Qty { get; set; }

    public decimal QtyPercent { get; set; }

    public decimal Frozen { get; set; }

    public DateOnly? LastTransDate { get; set; }

    [MaxLength(80)]
    public string? PaymentPreference { get; set; }

    /// <summary>DFM's own linking columns for NIN accounts related to this one (e.g. a custodian
    /// account and the underlying investor account cross-referencing each other) -- optional, blank
    /// on most rows.</summary>
    [MaxLength(40)]
    public string? LinkedNinsReference { get; set; }

    [MaxLength(200)]
    public string? LinkedNins { get; set; }
}

/// <summary>One upload of a DFM Shares Trading extract (Investor Relations &gt; Shares Trading &gt;
/// Upload) -- like ShareholderRegisterUpload, every upload is additive (its own batch of records),
/// never overwriting a prior one.</summary>
public class ShareTradingUpload
{
    public int Id { get; set; }

    [Required, MaxLength(260)]
    public string FileName { get; set; } = string.Empty;

    public int RecordCount { get; set; }

    public DateTime UploadedAtUtc { get; set; } = DateTime.UtcNow;

    [Required, MaxLength(160)]
    public string UploadedByName { get; set; } = string.Empty;

    public List<ShareTradingRecord> Records { get; set; } = [];
}

/// <summary>One row of a ShareTradingUpload batch -- one DFM movement for one investor on one report
/// date. OwnedQtyChange's sign is what the Shares Trading report uses to label a row Buy/Sell.</summary>
public class ShareTradingRecord
{
    public int Id { get; set; }

    public int ShareTradingUploadId { get; set; }
    public ShareTradingUpload? ShareTradingUpload { get; set; }

    public DateOnly ReportDate { get; set; }

    [Required, MaxLength(20)]
    public string Symbol { get; set; } = string.Empty;

    /// <summary>DFM's "Investor Number" -- the same NIN the Share Register is keyed by.</summary>
    [Required, MaxLength(40)]
    public string Nin { get; set; } = string.Empty;

    [MaxLength(200)]
    public string? InvestorName { get; set; }

    [MaxLength(40)]
    public string? ClientType { get; set; }

    [MaxLength(20)]
    public string? Nationality { get; set; }

    public decimal PreviousOwnQty { get; set; }

    public decimal CurrentOwnQty { get; set; }

    public decimal OwnedQtyChange { get; set; }
}
