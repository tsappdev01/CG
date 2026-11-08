using CGTOOL.Web.Data.Governance;

namespace CGTOOL.Checks;

/// <summary>
/// The two identifiers people type. Both rules are used by the screens that capture them, the
/// importer that loads them in bulk and the onboarding flow that asks a member to confirm them,
/// so a number accepted in one place has to be accepted in the others.
/// </summary>
public static class IdentityFormatChecks
{
    public static void Run()
    {
        Check.Section("NIN");
        Check.That("exactly ten letters or digits is valid", Nin.IsValid("PKA4076928"));
        Check.That("nine is not", !Nin.IsValid("PKA407692"));
        Check.That("fifteen is not -- what onboarding used to accept", !Nin.IsValid("PKA4076928sssss"));
        Check.That("punctuation is not", !Nin.IsValid("PKA-407692"));
        Check.That("an empty box is not an error yet", !Nin.IsInvalidEntry("  "));
        Check.That("a wrong one is", Nin.IsInvalidEntry("PKA407"));

        Check.Section("Emirates ID number");
        Check.That("fifteen digits beginning 784 is valid", EmiratesIdNumber.IsValid("784197940769280"));
        Check.That("the same number with the card's separators is the same number",
                   EmiratesIdNumber.IsValid("784-1979-4076928-0"));
        Check.That("and normalises to digits",
                   EmiratesIdNumber.Normalise("784-1979-4076928-0") == "784197940769280");
        Check.That("not beginning 784 is not valid", !EmiratesIdNumber.IsValid("123197940769280"));
        Check.That("fourteen digits is not", !EmiratesIdNumber.IsValid("78419794076928"));
        Check.That("a NIN is not an Emirates ID number", !EmiratesIdNumber.IsValid("PKA4076928"));
        Check.That("an empty box is not an error yet", !EmiratesIdNumber.IsInvalidEntry(""));
    }
}
