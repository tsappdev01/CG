namespace CGTOOL.Web.Data.Governance;

/// <summary>
/// How a relationship is named, everywhere it is named.
///
/// This was written out twelve times -- nine label maps and three dropdown lists -- and they had
/// drifted apart, which is how the same person came to read as "Father-in-Law" on the form they
/// signed and "Father-in-law" on the report about it. Every copy also ended in
/// `_ => relationship.ToString()`, so a value nobody had thought to label fell back to the enum
/// member's name and looked deliberate.
///
/// One definition, with one deliberate difference in it. The Related Party and Conflict of
/// Interest forms call RelativeRelationship.Stepchildren "Children of spouse"; everywhere else it
/// is "Stepchildren". That is a real distinction those forms make, so it is named here rather
/// than left to each file to remember -- which is precisely what the copies had stopped doing:
/// the COI detail report had no arm for the value at all and printed "Stepchildren" on a report
/// about a form that said "Children of spouse".
/// </summary>
public static class RelativeRelationships
{
    // Declared before the two arrays that read it: static fields initialise in declaration
    // order, and with this after them Selectable was still null when they ran -- every dropdown
    // would have thrown a TypeInitializationException on first render.
    private static readonly RelativeRelationship[] Selectable =
    [
        RelativeRelationship.Father,
        RelativeRelationship.Mother,
        RelativeRelationship.Brother,
        RelativeRelationship.Sister,
        RelativeRelationship.Children,
        RelativeRelationship.Spouse,
        RelativeRelationship.FatherInLaw,
        RelativeRelationship.MotherInLaw,
        RelativeRelationship.Stepchildren,
    ];

    /// <summary>What a declarant may choose. InLaws is deliberately absent: it predates the
    /// specific in-law values and nothing offers it any more, but rows already carry it, so the
    /// labels still answer for it.</summary>
    public static readonly (RelativeRelationship Value, string Label)[] Options =
        [.. Selectable.Select(v => (v, Label(v)))];

    /// <summary>The same choices in the wording the COI forms use.</summary>
    public static readonly (RelativeRelationship Value, string Label)[] CoiOptions =
        [.. Selectable.Select(v => (v, CoiLabel(v)))];

    public static string Label(RelativeRelationship relationship) => relationship switch
    {
        RelativeRelationship.InLaws => "In-Laws",
        RelativeRelationship.FatherInLaw => "Father-in-Law",
        RelativeRelationship.MotherInLaw => "Mother-in-Law",
        _ => relationship.ToString(),
    };

    /// <summary>The wording the Related Party and COI forms use, and so the wording every report
    /// about one of those forms has to use.</summary>
    public static string CoiLabel(RelativeRelationship relationship) => relationship switch
    {
        RelativeRelationship.Stepchildren => "Children of spouse",
        _ => Label(relationship),
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
