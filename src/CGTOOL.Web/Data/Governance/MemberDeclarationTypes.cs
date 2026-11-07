namespace CGTOOL.Web.Data.Governance;

/// <summary>The declaration types as people read them. The dash is an en dash, as written on the
/// form the wording came from.
///
/// The parent holding company's name is passed in rather than written here -- "Employee - DI PJSC"
/// was one of the places a rename had to reach, and a static array cannot read configuration.</summary>
public static class MemberDeclarationTypes
{
    public static (MemberDeclarationType Value, string Label)[] All(string parentHoldingCompany) =>
    [
        (MemberDeclarationType.BoardMember, "Board Member"),
        (MemberDeclarationType.EmployeeDiPjsc, $"Employee \u2013 {parentHoldingCompany}"),
        (MemberDeclarationType.EmployeeOther, "Employee \u2013 Other"),
        (MemberDeclarationType.Corporate, "Corporate"),
    ];

    public static string Label(MemberDeclarationType? type, string parentHoldingCompany) =>
        type is null ? "Not set" : All(parentHoldingCompany).First(t => t.Value == type).Label;
}
