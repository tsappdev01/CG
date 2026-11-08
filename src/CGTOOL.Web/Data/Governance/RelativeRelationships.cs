namespace CGTOOL.Web.Data.Governance;

/// <summary>
/// How a relationship is named, everywhere it is named.
///
/// This was written out twelve times -- nine label maps and three dropdown lists -- and they had
/// drifted apart, which is how the same person came to read as "Father-in-Law" on the form they
/// signed and "Father-in-law" on the report about it, and a stepchild as "Children of spouse" on
/// one declaration and "Stepchildren" on the other. Every copy also ended in
/// `_ => relationship.ToString()`, so a value nobody had thought to label fell back to the enum
/// member's name and looked deliberate.
///
/// One definition. A screen, a PDF and a report cannot disagree about a word they all read from
/// here.
/// </summary>
public static class RelativeRelationships
{
    /// <summary>What a declarant may choose. InLaws is deliberately absent: it predates the
    /// specific in-law values and nothing offers it any more, but rows already carry it, so Label
    /// still answers for it.</summary>
    public static readonly (RelativeRelationship Value, string Label)[] Options =
    [
        (RelativeRelationship.Father, "Father"),
        (RelativeRelationship.Mother, "Mother"),
        (RelativeRelationship.Brother, "Brother"),
        (RelativeRelationship.Sister, "Sister"),
        (RelativeRelationship.Children, "Children"),
        (RelativeRelationship.Spouse, "Spouse"),
        (RelativeRelationship.FatherInLaw, "Father-in-Law"),
        (RelativeRelationship.MotherInLaw, "Mother-in-Law"),
        (RelativeRelationship.Stepchildren, "Stepchildren"),
    ];

    public static string Label(RelativeRelationship relationship) => relationship switch
    {
        RelativeRelationship.InLaws => "In-Laws",
        RelativeRelationship.FatherInLaw => "Father-in-Law",
        RelativeRelationship.MotherInLaw => "Mother-in-Law",
        _ => relationship.ToString(),
    };

    /// <summary>What belongs in a "Share Held By" column.
    ///
    /// The declarant's own holding is kept as a row in the same grid as their relatives', with
    /// IsSelf set and the relationship left meaningless -- and RelativeRelationship.Mother is
    /// zero, so a renderer that printed the relationship anyway had the declarant down as his own
    /// mother. Asking this one question instead makes that impossible to repeat.</summary>
    public static string HeldBy(bool isSelf, RelativeRelationship relationship) =>
        isSelf ? "Self" : Label(relationship);
}
