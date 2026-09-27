namespace CGTOOL.Web.Data.Governance;

/// <summary>What counts as a National Investor Number: letters and digits only, at least ten
/// characters (Insider Trading Declaration Functional Spec §3.1/§8).
///
/// One definition, used by every screen that takes one. It used to live as a private method on the
/// declaration page, so My Register -- which captures a relative's NIN months before the
/// declaration asks for it -- accepted anything. A NIN that was fine where it was typed and
/// rejected where it was used is the worst of both.</summary>
public static class Nin
{
    public const int MinimumLength = 10;

    public static bool IsValid(string? nin) =>
        !string.IsNullOrWhiteSpace(nin)
        && nin.Trim().Length >= MinimumLength
        && nin.Trim().All(char.IsLetterOrDigit);

    /// <summary>True for a value that is present and wrong -- what a field shows a warning for.
    /// An empty box is not yet an error: the person may simply not have reached it.</summary>
    public static bool IsInvalidEntry(string? nin) => !string.IsNullOrWhiteSpace(nin) && !IsValid(nin);

    public const string Hint = "Letters and digits only, at least 10 characters.";

    /// <summary>For log lines and summaries, where a missing NIN has to read as something.</summary>
    public static string Show(string? nin) => string.IsNullOrWhiteSpace(nin) ? "none" : nin.Trim();

    public static string ErrorFor(string what) =>
        $"Enter a valid NIN for {what}: letters and digits only (no special characters), at least {MinimumLength} characters.";
}
