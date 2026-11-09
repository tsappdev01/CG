using System.ComponentModel.DataAnnotations;

namespace CGTOOL.Web.Data.Governance;

/// <summary>What a line records. The form's rows in order: what was held when the period opened,
/// each purchase and each sale during it, and what was held when it closed.</summary>
/// <summary>RETIRED. The share transactions section was removed from the Insider declaration at
/// the business's request, and with it the Declared vs Actual report that read these rows.
///
/// The type, the DbSet and the table stay so that declarations submitted while the section existed
/// still hold what was declared on them -- a declaration is evidence, and dropping the table would
/// destroy it. Nothing writes here any more: the writer's insert and delete are gone, so an edit
/// to an old declaration leaves its rows exactly as they were filed.
///
/// If the business later confirms the history is not needed, the table, this file and the
/// usp_InsiderDeclarationShareEntry_* procedures can go together, in that order.</summary>
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

    /// <summary>The declarant's own line. Kept as a flag rather than a value of Relationship,
    /// exactly as InsiderDeclarationRelative does it, so the two grids sort and read alike.</summary>
    public bool IsSelf { get; set; }

    /// <summary>Which relative, when it is not the declarant. The same list the shareholding grid
    /// above offers -- a holder who appears in one and not the other would be a different person
    /// to each of them.</summary>
    public RelativeRelationship Relationship { get; set; }

    /// <summary>Who, for anyone but the declarant, whose name is already on the declaration.</summary>
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
