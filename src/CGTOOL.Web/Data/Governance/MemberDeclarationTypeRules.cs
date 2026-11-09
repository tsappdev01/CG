namespace CGTOOL.Web.Data.Governance;

/// <summary>Which declaration a person files, decided from the entity they belong to.
///
/// An imported row never says. Left unset, the person has no form to fill and no capacity to
/// declare in, so an administrator had to open each record and choose one by hand -- which for a
/// list of a few hundred is where the whole import stops being useful.
///
/// The rule is the organisation's, not a guess: people filed under the parent entity declare as
/// its employees, people filed under the board entity declare as board members, and everybody
/// else declares as an employee of another entity. Someone who is genuinely an exception is still
/// changed on their own record afterwards -- see the note on updates in DirectoryImporter.
/// </summary>
public static class MemberDeclarationTypeRules
{
    /// <summary>The entity whose people declare as employees of the parent holding company.</summary>
    public const string ParentEntityName = "DI Park";

    /// <summary>The entity whose people declare as board members.</summary>
    public const string BoardEntityName = "Board Member";

    /// <summary>Applies the entity's rule to a member, writing the capacity and -- when the rule
    /// says board -- the Board of Directors flag with it. Returns true when it wrote anything.
    ///
    /// The two are written together because they are read apart. The capacity shows on the record
    /// and in the Related Party master report; IsBoardMember decides which Related Party &amp; COI
    /// form variant renders (Functional Spec Section 3) and who counts as board-or-executive in the
    /// Related Party Register. Setting only the capacity gave a person imported under the board
    /// entity a record that said "Board Member" while they were served the Executive Management
    /// form and left out of the board grouping -- the record and the form disagreeing about the
    /// same person.
    ///
    /// Fill, never overwrite, and never clear: a capacity an administrator has since changed is a
    /// deliberate exception, and someone can sit on the board of another entity entirely, so the
    /// flag is only ever turned on by this rule.</summary>
    public static bool Apply(Member member, string? entityName)
    {
        ArgumentNullException.ThrowIfNull(member);

        if (member.DeclarationType is not null) return false;

        var type = FromEntityName(entityName);
        member.DeclarationType = type;

        if (type == MemberDeclarationType.BoardMember) member.IsBoardMember = true;

        return true;
    }

    public static MemberDeclarationType FromEntityName(string? entityName)
    {
        var normalised = Normalise(entityName);

        if (normalised.Length == 0) return MemberDeclarationType.EmployeeOther;
        if (normalised == Normalise(ParentEntityName)) return MemberDeclarationType.EmployeeDiPjsc;
        if (normalised == Normalise(BoardEntityName)) return MemberDeclarationType.BoardMember;

        return MemberDeclarationType.EmployeeOther;
    }

    /// <summary>Letters and digits only, upper-cased. Entity names are typed by hand in both the
    /// spreadsheet and Entra, so "DI Park", "DI-PARK" and "di park" are one entity; comparing the
    /// strings as they arrive would send two of those three down the "Other" branch without
    /// anything on screen saying why.</summary>
    private static string Normalise(string? value) =>
        value is null ? string.Empty : new string([.. value.Where(char.IsLetterOrDigit)]).ToUpperInvariant();
}
