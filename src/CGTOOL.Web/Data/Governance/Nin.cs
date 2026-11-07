namespace CGTOOL.Web.Data.Governance;

/// <summary>What counts as a National Investor Number: letters and digits only, exactly ten
/// characters.
///
/// One definition, used by every screen that takes one. It used to live as a private method on the
/// declaration page, so My Register -- which captures a relative's NIN months before the
/// declaration asks for it -- accepted anything. A NIN that was fine where it was typed and
/// rejected where it was used is the worst of both.
///
/// This was "at least ten" until it was changed to exactly ten. Anything already stored that is
/// longer now reads as invalid wherever it is shown, which is the point of the change and worth
/// knowing before it surprises someone looking at an old declaration.</summary>
public static class Nin
{
    public const int Length = 10;

    public static bool IsValid(string? nin) =>
        !string.IsNullOrWhiteSpace(nin)
        && nin.Trim().Length == Length
        && nin.Trim().All(char.IsLetterOrDigit);

    /// <summary>True for a value that is present and wrong -- what a field shows a warning for.
    /// An empty box is not yet an error: the person may simply not have reached it.</summary>
    public static bool IsInvalidEntry(string? nin) => !string.IsNullOrWhiteSpace(nin) && !IsValid(nin);

    /// <summary>True once the box holds a whole NIN, which is what a field shows a tick for.
    /// Distinct from IsValid only in reading as what the tick means at the point of use.</summary>
    public static bool IsComplete(string? nin) => IsValid(nin);

    public const string Hint = "Letters and digits only, exactly 10 characters.";

    /// <summary>For log lines and summaries, where a missing NIN has to read as something.</summary>
    public static string Show(string? nin) => string.IsNullOrWhiteSpace(nin) ? "none" : nin.Trim();

    public static string ErrorFor(string what) =>
        $"Enter a valid NIN for {what}: letters and digits only (no special characters), exactly {Length} characters.";
}
