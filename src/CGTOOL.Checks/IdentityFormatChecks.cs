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

/// <summary>
/// The nationality list and the spellings that resolve onto it. Two entries for one country is
/// what Countries exists to prevent, so the variants in the files we are given are mapped rather
/// than added -- which only works while every mapping points at a name the dropdown actually
/// offers.
/// </summary>
public static class CountryChecks
{
    public static void Run()
    {
        Check.Section("Nationalities");

        var missing = Countries.Aliases
            .Where(a => !Countries.All.Contains(a.Value, StringComparer.Ordinal))
            .Select(a => $"\"{a.Key}\" -> \"{a.Value}\"")
            .ToList();
        Check.That($"every alias points at a name the dropdown offers{(missing.Count > 0 ? " -- " + string.Join("; ", missing) : "")}",
                   missing.Count == 0);

        Check.That("the dropdown has no duplicates",
                   Countries.All.Length == Countries.All.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Check.That("the default is on the list", Countries.All.Contains(Countries.Default, StringComparer.Ordinal));

        // The long forms an Emirates ID export writes, which the file we were given carries.
        foreach (var (written, expected) in new[]
                 {
                     ("State Of Palestine", "Palestine"),
                     ("Commonwealth Of Dominica", "Dominica"),
                     ("Syrian Arab Republic", "Syria"),
                     ("Iran (Islamic Republic Of)", "Iran"),
                     ("UAE", Countries.Default),
                     ("united arab emirates", Countries.Default),
                 })
        {
            Check.That($"\"{written}\" resolves to \"{expected}\"", Countries.Resolve(written) == expected);
        }

        Check.That("a name nobody recognises resolves to nothing, rather than a guess",
                   Countries.Resolve("#REF!") is null && Countries.Resolve("Atlantis") is null);
        Check.That("blank resolves to nothing", Countries.Resolve("  ") is null && Countries.Resolve(null) is null);

        // Every MRZ code already had to map onto the list; the same rule, checked the same way.
        Check.That("every nationality the dropdown offers resolves to itself",
                   Countries.All.All(c => Countries.Resolve(c) == c));
    }
}
