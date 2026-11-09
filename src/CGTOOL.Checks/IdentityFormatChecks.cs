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
        Check.That("fifteen digits beginning 784 is valid", EmiratesIdNumber.IsValid("784100000000000"));
        Check.That("the same number with the card's separators is the same number",
                   EmiratesIdNumber.IsValid("784-1000-0000000-0"));
        Check.That("and normalises to digits",
                   EmiratesIdNumber.Normalise("784-1000-0000000-0") == "784100000000000");
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

        NameOnTheDocument();
    }

    /// <summary>The name a register of identity documents stores.
    ///
    /// The prebuilt model splits an Emirates ID's one printed name and keeps the first part or
    /// two, so a card printed with five names came back with two and My Workspace showed a name
    /// that was not the name on the ID. The line printed on the card wins when it is the same
    /// name in full -- and only then, because the same label search lands on a passport's
    /// "Surname" line, which would otherwise replace a full name with half of one.</summary>
    private static void NameOnTheDocument()
    {
        Check.Section("The name stored is the name on the document");

        static string? Holder(string? first, string? last, string? printed) =>
            new IdDocumentExtraction(null, first, last, null, null, NameOnDocument: printed).HolderName;

        Check.That("the full printed name wins over the model's shortened one",
                   Holder("Aisha", "Bint", "Aisha Bint Rashid Al Noori Hassan")
                   == "Aisha Bint Rashid Al Noori Hassan");

        Check.That("a printed line that is not the same name is ignored",
                   Holder("Aisha", "Bint", "Hassan Mohammed") == "Aisha Bint");

        Check.That("the case the card prints it in does not matter",
                   Holder("AISHA", "BINT", "Aisha Bint Rashid Al Noori") == "Aisha Bint Rashid Al Noori");

        Check.That("with nothing printed, the model's name is used",
                   Holder("Aisha", "Bint", null) == "Aisha Bint");

        Check.That("with nothing from the model, the printed name is used",
                   Holder(null, null, "Aisha Bint Rashid") == "Aisha Bint Rashid");

        Check.That("a card that read as nothing stores nothing", Holder(null, null, null) is null);
        Check.That("a blank printed line is not mistaken for a name", Holder("Aisha", "Bint", "   ") == "Aisha Bint");
        Check.That("a shorter printed line does not replace a longer name",
                   Holder("Aisha", "Bint", "Aisha") == "Aisha Bint");

        Check.Section("Reading the name line off the card");

        Check.That("the name follows its label",
                   IdDocumentTextParser.FindNameOnCard("Name: Aisha Bint Rashid Al Noori")
                   == "Aisha Bint Rashid Al Noori");

        Check.That("the Arabic half of a bilingual line is dropped",
                   IdDocumentTextParser.FindNameOnCard("Name: Aisha Bint الاسم") == "Aisha Bint");

        Check.That("a line with digits in it is a number, not a name",
                   IdDocumentTextParser.FindNameOnCard("Name: 784-1000-0000000-0") is null);

        Check.That("a line with no name on it reads as nothing",
                   IdDocumentTextParser.FindNameOnCard("Nationality: INDIA") is null);
    }
}
