namespace CGTOOL.Web.Data.Governance;

/// <summary>What kind of legal entity a company on the register is. Stored as its own value rather
/// than inferred from the name or the group, because "Government-Owned Company" and "Subsidiary" are
/// judgements about ownership that nothing in the record makes for you.</summary>
public enum CompanyEntityType
{
    Individual,
    Company,
    GovernmentEntity,
    GovernmentOwnedCompany,
    Subsidiary,
    Associate,
    JointVenture,
    InvestmentFund,
    Partnership,
    OtherLegalEntity,
}

public static class CompanyEntityTypes
{
    public static readonly (CompanyEntityType Value, string Label)[] All =
    [
        (CompanyEntityType.Individual, "Individual"),
        (CompanyEntityType.Company, "Company"),
        (CompanyEntityType.GovernmentEntity, "Government Entity"),
        (CompanyEntityType.GovernmentOwnedCompany, "Government-Owned Company"),
        (CompanyEntityType.Subsidiary, "Subsidiary"),
        (CompanyEntityType.Associate, "Associate"),
        (CompanyEntityType.JointVenture, "Joint Venture"),
        (CompanyEntityType.InvestmentFund, "Investment Fund"),
        (CompanyEntityType.Partnership, "Partnership"),
        (CompanyEntityType.OtherLegalEntity, "Other Legal Entity"),
    ];

    public static string Label(CompanyEntityType? type) =>
        type is null ? "Not set" : All.First(t => t.Value == type).Label;
}
