namespace CGTOOL.Web.Data.Governance;

/// <summary>Occupations offered wherever one is captured. Free text gave "IT", "I.T.", "Information
/// Technology" and "Software" for one answer, which no report can group -- the same reason the
/// nationality list exists.
///
/// "Other" is kept as an escape hatch rather than pretending the list is complete: picking it opens
/// a box, and what is typed there is stored in the same column as a listed answer. So a value read
/// back from the database is simply "in the list" or "not in the list" -- there is no second column
/// to keep in step, and a list that grows later reclassifies old answers on its own.</summary>
public static class Occupations
{
    public const string Other = "Other";

    public static readonly string[] All =
    [
        "Accountant / Accounting",
        "Administration",
        "Architect",
        "Banking / Financial Services",
        "Business Owner",
        "Consultant",
        "Contractor",
        "Director / Board Member",
        "Doctor / Medical Professional",
        "Education / Teaching",
        "Engineer",
        "Entrepreneur",
        "Finance",
        "Government Employee",
        "Human Resources",
        "Information Technology / IT",
        "Insurance",
        "Investor",
        "Legal / Lawyer",
        "Marketing",
        "Media / Journalism",
        "Real Estate",
        "Sales",
        "Self-Employed",
        "Student",
        "Technology / Software",
        "Trading / General Trading",
        "Transport / Logistics",
        "Manufacturing",
        "Construction",
        "Retail",
        "Wholesale",
        "Hospitality / Tourism",
        "Healthcare",
        "Pharmaceutical",
        "Oil & Gas / Energy",
        "Telecommunications",
        "Agriculture / Farming",
        "Import / Export",
        "Professional Services",
        "Freelancer",
        "Retired",
        "Homemaker",
        "Unemployed",
    ];

    public static bool IsListed(string? occupation) =>
        !string.IsNullOrWhiteSpace(occupation) && All.Contains(occupation);
}
