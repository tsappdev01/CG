namespace CGTOOL.Web.Data.Governance;

/// <summary>Naming an entity or a department that arrived from somewhere else.
///
/// Both carry a short code that has to be unique, and a directory supplies only a name -- so one
/// is derived from the name and then made unique against what is already in use. Shared because
/// the directory import and "Fill from directory" on a member's record meet the same situation
/// and had answered it differently: the import created the missing department, the button left
/// the field alone and said so, which read as the directory having no department at all.</summary>
public static class ShortCodes
{
    public static string Unique(string name, IEnumerable<string?> taken)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(taken);

        var letters = new string(name.Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();
        var seed = letters.Length == 0 ? "ORG" : letters[..Math.Min(8, letters.Length)];

        var existing = taken.Where(t => t is not null).Select(t => t!.ToUpperInvariant()).ToHashSet();
        if (!existing.Contains(seed)) return seed;

        for (var n = 2; n < 1000; n++)
        {
            var candidate = $"{seed[..Math.Min(6, seed.Length)]}{n}";
            if (!existing.Contains(candidate)) return candidate;
        }

        return $"{seed[..Math.Min(4, seed.Length)]}{Guid.NewGuid().ToString("N")[..4].ToUpperInvariant()}";
    }
}
