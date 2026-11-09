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

        Chevron(insiderMarkup, coiMarkup);
        SectionQuestions(coiMarkup);
    }

    /// <summary>Each RP & COI section opens by asking whether there is anything in it.
    ///
    /// It used to ask with a switch bound to the inverse of the flag it set: the member read "I
    /// have nothing to declare" beside a control that was off, off being what made that sentence
    /// true. Worse, it started there, so a section nobody opened was declared empty on their
    /// behalf. Five buttons-pairs with neither chosen replaced it, and nothing may walk past an
    /// unanswered one.</summary>
    private static void SectionQuestions(string coiMarkup)
    {
        Check.Section("Each section asks its question outright");

        var asked = coiMarkup.Split("<DeclareChoice").Length - 1;
        Check.That($"all five sections ask with buttons, not a switch (found {asked})", asked == 5);

        Check.That("no switch is left asking it",
                   !coiMarkup.Contains("cg-nothing-switch", StringComparison.Ordinal));

        // The specific defect: a control whose checked state is the negation of the value it sets.
        Check.That("nothing is bound to the inverse of the flag it sets",
                   !coiMarkup.Contains("@bind:get=\"!_nothing", StringComparison.Ordinal));

        var page = typeof(CGTOOL.Web.Components.Pages.SubmitRelatedPartyCoiDeclaration);
        const System.Reflection.BindingFlags Any =
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
        var answerFor = page.GetMethod("AnswerFor", Any);
        var answer = page.GetMethod("Answer", Any);
        var stepType = page.GetNestedType("Step", System.Reflection.BindingFlags.NonPublic);

        Check.That("the page can say whether a section is answered", answerFor is not null && answer is not null && stepType is not null);
        if (answerFor is null || answer is null || stepType is null) return;

        var instance = Activator.CreateInstance(page)!;
        var relatives = Enum.Parse(stepType, "Relatives");
        var conflicts = Enum.Parse(stepType, "Conflicts");

        Check.That("a section nobody has answered reads as unanswered, not as empty",
                   answerFor.Invoke(instance, [relatives]) is null
                   && answerFor.Invoke(instance, [conflicts]) is null);

        answer.Invoke(instance, [relatives, true]);
        Check.That("answering \"nothing to declare\" records that answer",
                   answerFor.Invoke(instance, [relatives]) is true);

        answer.Invoke(instance, [relatives, false]);
        Check.That("and changing the answer records the other one",
                   answerFor.Invoke(instance, [relatives]) is false);

        Check.That("answering one section does not answer the rest",
                   answerFor.Invoke(instance, [conflicts]) is null);
    }

    /// <summary>The chevron that tells the member how far through they are.
    ///
    /// The Insider one was written out step by step, each with its own ternary: the numbering
    /// lived in the markup, and on a submitted declaration steps 2 and 3 matched no state at all
    /// and rendered blank. Both are now built from a step list, so the labels, the numbering and
    /// the states come from one place.</summary>
    private static void Chevron(string insiderMarkup, string coiMarkup)
    {
        Check.Section("The step chevron is built from the steps");

        Check.That("neither chevron numbers its steps by hand",
                   insiderMarkup.Contains("WizardSteps", StringComparison.Ordinal)
                   && coiMarkup.Contains("WizardSteps", StringComparison.Ordinal));

        Check.That("both chevrons wrap rather than crowding the edge on a narrow screen",
                   insiderMarkup.Contains("cg-stepper-wrap", StringComparison.Ordinal)
                   && coiMarkup.Contains("cg-stepper-wrap", StringComparison.Ordinal));

        var page = typeof(CGTOOL.Web.Components.Pages.SubmitInsiderDeclaration);
        const System.Reflection.BindingFlags Any =
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
        var stepField = page.GetField("_step", Any);
        var stepClass = page.GetMethod("StepClass", Any);
        var steps = page.GetField("WizardSteps",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

        Check.That("the chevron's state is worked out in one method", stepClass is not null && stepField is not null && steps is not null);
        if (stepClass is null || stepField is null || steps is null) return;

        var instance = Activator.CreateInstance(page)!;
        var list = (Array)steps.GetValue(null)!;
        // A value tuple's elements are fields, and the names live in an attribute rather than on
        // the type, so Item1 is what reflection has to ask for.
        object StepAt(int i)
        {
            var entry = list.GetValue(i)!;
            return entry.GetType().GetField("Item1")!.GetValue(entry)!;
        }
        string ClassOf(int i) => (string)stepClass.Invoke(instance, [StepAt(i)])!;

        Check.That("there are three steps", list.Length == 3);

        stepField.SetValue(instance, StepAt(0));
        Check.That("on the first step, it is the current one and nothing is done yet",
                   ClassOf(0) == "is-current" && ClassOf(1) == string.Empty && ClassOf(2) == string.Empty);

        stepField.SetValue(instance, StepAt(2));
        Check.That("on the last step, the two behind it are done and it is current",
                   ClassOf(0) == "is-done" && ClassOf(1) == "is-done" && ClassOf(2) == "is-current");

        var done = Enum.Parse(stepField.FieldType, "Done");
        stepField.SetValue(instance, done);
        Check.That("on a submitted declaration every step reads as done, not as blank",
                   ClassOf(0) == "is-done" && ClassOf(1) == "is-done" && ClassOf(2) == "is-done");
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
