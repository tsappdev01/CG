using System.Reflection;
using CGTOOL.Web.Data.Governance;

namespace CGTOOL.Checks;

/// <summary>
/// Every surface that names a relationship has to name it the same way.
///
/// How a relationship is named was once written out twelve times and the copies drifted, so the
/// same person read as "Father-in-Law" on the form they signed and "Father-in-law" on the report
/// about it. These checks are what stops that happening again: they walk the assembly rather
/// than a list someone has to maintain, so a tenth labeller added tomorrow is checked too.
/// </summary>
public static class RelationshipChecks
{
    public static void Run()
    {
        var values = Enum.GetValues<RelativeRelationship>();

        var labellers = typeof(GovernanceRoles).Assembly.GetTypes()
            .SelectMany(t => t.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
            .Where(m => m.Name is "RelationshipLabel" or "RelationLabel")
            .Where(m => m.GetParameters() is [{ } p] && p.ParameterType == typeof(RelativeRelationship))
            .ToList();

        // The Related Party and COI forms say "Children of spouse" where everything else says
        // "Stepchildren". A report about one of those forms has to use that form's word.
        static bool IsCoi(MethodInfo m) =>
            m.DeclaringType!.Name.Contains("Coi", StringComparison.OrdinalIgnoreCase)
            || m.DeclaringType.Name.Contains("RelatedParty", StringComparison.OrdinalIgnoreCase);

        var coi = labellers.Where(IsCoi).ToList();
        var standard = labellers.Where(m => !IsCoi(m)).ToList();

        Check.Section($"Relationship labels ({labellers.Count} surfaces: {coi.Count} COI, {standard.Count} standard)");
        Check.That("every surface that had its own label map is still reachable", labellers.Count >= 9);
        Check.That("both families are populated", coi.Count > 0 && standard.Count > 0);

        foreach (var (family, name, expected) in new[]
                 {
                     (coi, "COI", (Func<RelativeRelationship, string>)RelativeRelationships.CoiLabel),
                     (standard, "standard", RelativeRelationships.Label),
                 })
        {
            foreach (var v in values)
            {
                var want = expected(v);
                var disagreeing = family
                    .Select(m => (m, got: (string)m.Invoke(null, [v])!))
                    .Where(x => x.got != want)
                    .Select(x => $"{x.m.DeclaringType!.Name} says \"{x.got}\"")
                    .ToList();

                Check.That($"{name}: {v} is \"{want}\"" +
                           (disagreeing.Count > 0 ? "  -- " + string.Join("; ", disagreeing) : ""),
                           disagreeing.Count == 0);
            }
        }

        Check.Section("Relationship labels: the two wordings differ only where intended");
        var differ = values.Where(v => RelativeRelationships.Label(v) != RelativeRelationships.CoiLabel(v)).ToList();
        Check.That($"only Stepchildren differs (differs: {string.Join(", ", differ)})",
                   differ is [RelativeRelationship.Stepchildren]);

        Check.Section("Relationship labels: nothing falls through to an enum member name");
        foreach (var v in values)
        {
            // "FatherInLaw" is what `_ => relationship.ToString()` produces for a value nobody
            // wrote an arm for, and it looks deliberate on a page.
            var label = RelativeRelationships.Label(v);
            Check.That($"{v} is labelled, not stringified", !(label == v.ToString() && label.Skip(1).Any(char.IsUpper)));
        }

        Check.Section("Relationship dropdowns");
        Check.That("standard options carry the standard labels",
                   RelativeRelationships.Options.All(o => o.Label == RelativeRelationships.Label(o.Value)));
        Check.That("COI options carry the COI labels",
                   RelativeRelationships.CoiOptions.All(o => o.Label == RelativeRelationships.CoiLabel(o.Value)));
        Check.That("both offer the same values in the same order",
                   RelativeRelationships.Options.Select(o => o.Value)
                       .SequenceEqual(RelativeRelationships.CoiOptions.Select(o => o.Value)));

        Check.Section("The declarant's own row");
        // Relationship is meaningless when IsSelf is set, and Mother is the enum's zero -- which
        // is how a submission report once had the declarant down as his own mother.
        Check.That("Self reads as Self whatever the relationship holds",
                   values.All(v => RelativeRelationships.HeldBy(true, v) == "Self"));
        Check.That("a relative reads as the relationship",
                   RelativeRelationships.HeldBy(false, RelativeRelationship.Spouse) == "Spouse");
    }
}
