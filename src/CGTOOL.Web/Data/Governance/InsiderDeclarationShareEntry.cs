using System.ComponentModel.DataAnnotations;

namespace CGTOOL.Web.Data.Governance;

/// <summary>Whose shares a line is about. The form the wording comes from has three columns --
/// Self, Spouse, Relatives -- and a line belongs to exactly one of them.</summary>
public enum ShareHolderKind
{
    Self,
    Spouse,
    Relative,
}

/// <summary>What a line records. The form's rows in order: what was held when the period opened,
/// each purchase and each sale during it, and what was held when it closed.</summary>
public enum ShareEntryKind
{
    OpeningBalance,
    Purchase,
    Sale,
    ClosingBalance,
}

/// <summary>One line of the share movement table on the Insider Declaration: who held them, what
/// happened, when, how many and at what rate.
///
/// A flat list rather than the printed form's six-column grid of Self / Spouse / Relatives against
/// a fixed set of rows. The paper form allows three dated purchases and three dated sales and
/// footnotes that more go on an appended sheet; a list has no such limit, carries the holder's own
/// name and NIN rather than a column heading, and is what every report and the PDF read back.
///
/// Rate is nullable because a balance has none -- what was held at the start of the period was not
/// bought at a price during it. Date likewise: only a purchase or a sale happened on a day.</summary>
public class InsiderDeclarationShareEntry
{
    public int Id { get; set; }

    public int InsiderDeclarationId { get; set; }
    public InsiderDeclaration? InsiderDeclaration { get; set; }

    public ShareHolderKind HolderKind { get; set; }

    /// <summary>Who, for a spouse or a relative. Left null for Self, whose name is the
    /// declarant's and is already on the declaration.</summary>
    [MaxLength(200)]
    public string? HolderName { get; set; }

    [MaxLength(40)]
    public string? HolderNin { get; set; }

    public ShareEntryKind EntryKind { get; set; }

    /// <summary>The day of a purchase or a sale. Null on a balance.</summary>
    public DateTime? TransactionDate { get; set; }

    public long NumberOfShares { get; set; }

    /// <summary>Rate of sale or purchase. Null on a balance. Four decimal places, which is what a
    /// DFM price carries.</summary>
    public decimal? Rate { get; set; }

    public bool IsTransaction => EntryKind is ShareEntryKind.Purchase or ShareEntryKind.Sale;
}
