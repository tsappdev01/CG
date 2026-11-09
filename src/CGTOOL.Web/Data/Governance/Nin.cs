namespace CGTOOL.Web.Data.Governance;

/// <summary>What counts as a National Investor Number: letters and digits only, exactly ten
/// characters.
///
/// One definition, used by every screen that takes one. It used to live as a private method on the
/// declaration page, so My Workspace -- which captures a relative's NIN months before the
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

    /// <summary>A NIN as it is stored: trimmed, and a short all-digit number padded to ten with
    /// leading zeros.
    ///
    /// The exchange's older numbers are written seven and eight digits long in the files we are
    /// given, and are the same number as the ten-digit form with the zeros restored. Without this
    /// they import, fail the rule on every screen that shows them, and stop those members at the
    /// DFM step of onboarding.
    ///
    /// Applied at every point a NIN enters the system, not only where the short ones come from.
    /// The reconciliation report joins what members declared against what the DFM reported, by
    /// investor number: padding one side and not the other would turn every one of those
    /// investors into a "not declared" or "not at DFM" finding that is purely an artefact of the
    /// padding -- on exactly the senior people whose numbers are the short ones.
    ///
    /// Only all-digit values are padded. A short value with a letter in it is not a NIN missing
    /// its zeros, and inventing one out of stray text would be worse than leaving it to be
    /// rejected.</summary>
    public static string? Normalise(string? nin)
    {
        if (string.IsNullOrWhiteSpace(nin)) return nin;

        var trimmed = nin.Trim();
        return trimmed.Length < Length && trimmed.All(char.IsDigit)
            ? trimmed.PadLeft(Length, '0')
            : trimmed;
    }

    /// <summary>For log lines and summaries, where a missing NIN has to read as something.</summary>
    public static string Show(string? nin) => string.IsNullOrWhiteSpace(nin) ? "none" : nin.Trim();

    public static string ErrorFor(string what) =>
        $"Enter a valid NIN for {what}: letters and digits only (no special characters), exactly {Length} characters.";
}
