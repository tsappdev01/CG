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
