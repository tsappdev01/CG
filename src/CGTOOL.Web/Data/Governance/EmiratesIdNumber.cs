namespace CGTOOL.Web.Data.Governance;

/// <summary>What counts as an Emirates ID number: fifteen digits beginning 784.
///
/// The rule was written out in the spreadsheet importer and again in the bulk scan matcher, and
/// nowhere at all on the screens that ask a member to type one -- which is how onboarding came to
/// accept "PKA4076928sssss" in the box. One definition, so a number that is accepted where it is
/// typed is still accepted where it is used.</summary>
public static class EmiratesIdNumber
{
    public const int Length = 15;
    public const string Prefix = "784";

    public static bool IsValid(string? value)
    {
        var digits = Digits(value);
        return digits is { Length: Length } && digits.StartsWith(Prefix, StringComparison.Ordinal);
    }

    /// <summary>Present and wrong -- what a field shows a warning for. An empty box is not an
    /// error yet: the member may simply not have reached it.</summary>
    public static bool IsInvalidEntry(string? value) => !string.IsNullOrWhiteSpace(value) && !IsValid(value);

    /// <summary>Distinct from IsValid only in reading as what the tick beside the box means.</summary>
    public static bool IsComplete(string? value) => IsValid(value);

    public const string Hint = "An Emirates ID number is 15 digits beginning 784.";

    /// <summary>The digits alone. The card prints the number as 784-YYYY-NNNNNNN-C and people type
    /// it as they see it, so a value with the separators in is the same number and is accepted as
    /// one.</summary>
    public static string Digits(string? value) =>
        value is null ? string.Empty : new string(value.Where(char.IsDigit).ToArray());

    /// <summary>The form everything else stores: digits, no separators. The bulk-loaded scans and
    /// the imported spreadsheet are both plain fifteen digits, and a member who typed the dashes
    /// would otherwise have a number that never matches theirs.</summary>
    public static string? Normalise(string? value) => IsValid(value) ? Digits(value) : value?.Trim();
}
