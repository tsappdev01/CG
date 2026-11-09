namespace CGTOOL.Checks;

/// <summary>
/// Both declarations conclude from their own review step. Business asked twice for the
/// "Confirm and submit" window to go -- once on the Insider declaration, once on the Related
/// Party &amp; COI one -- so Submit on the review sheet is what files the declaration.
///
/// The COI window carried the signature, which is stored on the declaration and reproduced on
/// the RP &amp; COI reports. Dropping the window must not drop the signature with it, so this
/// also checks the pad is still on the page. That is the half of the change that is easy to
/// lose and impossible to notice until a report prints without a signature.
///
/// Walks the markup: the defect is in the HTML and the compiler has nothing to say about it.
/// </summary>
public static class SubmissionFlowChecks
{
    public static void Run()
    {
        Check.Section("A declaration is submitted from its review step");

        var root = FindRepositoryRoot();
        if (root is null)
        {
            Check.That("the markup can be read from here", false);
            return;
        }

        var pages = Path.Combine(root, "src", "CGTOOL.Web", "Components", "Pages");
        var insider = Path.Combine(pages, "SubmitInsiderDeclaration.razor");
        var coi = Path.Combine(pages, "SubmitRelatedPartyCoiDeclaration.razor");

        Check.That("both declaration pages are where they are expected",
                   File.Exists(insider) && File.Exists(coi));
        if (!File.Exists(insider) || !File.Exists(coi)) return;

        var insiderMarkup = File.ReadAllText(insider);
        var coiMarkup = File.ReadAllText(coi);

        Check.That("the Insider declaration has no undertakings window before Submit",
                   !insiderMarkup.Contains("cg-undertakings-title", StringComparison.Ordinal)
                   && !insiderMarkup.Contains("_showUndertakings", StringComparison.Ordinal));

        Check.That("the RP & COI declaration has no \"Confirm and submit\" window before Submit",
                   !coiMarkup.Contains("cg-attestation-title", StringComparison.Ordinal)
                   && !coiMarkup.Contains("_showAttestation", StringComparison.Ordinal));

        Check.That("the RP & COI declaration still captures a signature",
                   coiMarkup.Contains("<SignaturePad", StringComparison.Ordinal)
                   && coiMarkup.Contains("_attestationConfirmed", StringComparison.Ordinal));

        Check.That("each declaration still has a Submit that calls SubmitAsync",
                   insiderMarkup.Contains("OnClick=\"SubmitAsync\"", StringComparison.Ordinal)
                   && coiMarkup.Contains("OnClick=\"SubmitAsync\"", StringComparison.Ordinal));
    }

    private static string? FindRepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "src", "CGTOOL.Web"))) return dir.FullName;
            dir = dir.Parent;
        }
        return null;
    }
}
