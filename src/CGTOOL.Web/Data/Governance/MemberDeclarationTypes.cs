namespace CGTOOL.Web.Data.Governance;

/// <summary>The declaration types as people read them. The dash is an en dash, as written on the
/// form the wording came from.</summary>
public static class MemberDeclarationTypes
{
    public static readonly (MemberDeclarationType Value, string Label)[] All =
    [
        (MemberDeclarationType.BoardMember, "Board Member"),
        (MemberDeclarationType.EmployeeDiPjsc, "Employee – DI PJSC"),
        (MemberDeclarationType.EmployeeOther, "Employee – Other"),
        (MemberDeclarationType.Corporate, "Corporate"),
    ];

    public static string Label(MemberDeclarationType? type) =>
        type is null ? "Not set" : All.First(t => t.Value == type).Label;
}
