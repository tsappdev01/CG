using System.Reflection;
using CGTOOL.Web.Components.Pages;
using CGTOOL.Web.Data.Governance;

namespace CGTOOL.Checks;

/// <summary>
/// A declaration references the same blob row My Workspace does rather than copying the file. That
/// is deliberate -- a declaration has to keep what was declared -- but it meant deleting a document
/// in My Workspace left the Insider declaration still showing it, still labelled "Loaded from My
/// Workspace", with no way to take it off: the card offered View, Read details and Choose File, and
/// no Remove. The Related Party & COI declaration had a Remove all along; this one did not.
/// </summary>
public static class DeclarationDocumentChecks
{
    public static void Run()
    {
        Check.Section("A declaration's documents can be removed");

        var page = typeof(SubmitInsiderDeclaration);
        const BindingFlags Any = BindingFlags.NonPublic | BindingFlags.Instance;

        foreach (var name in new[] { "RemoveEmiratesId", "RemovePassport", "RemoveTradeLicence", "RemoveAddedDocument" })
        {
            Check.That($"{name} exists, so the member is not stuck with the document", page.GetMethod(name, Any) is not null);
        }

        Check.Section("A card says where its file actually came from");

        var instance = (SubmitInsiderDeclaration)Activator.CreateInstance(page)!;
        var loaded = (HashSet<MemberDocumentKind>)page.GetField("_loadedFromWorkspace", Any)!.GetValue(instance)!;
        var blobs = page.GetField("_workspaceBlobIds", Any)!;
        var label = page.GetMethod("SourceLabel", Any);

        Check.That("the source label is worked out in one place", label is not null);
        if (label is null) return;

        string Say(int? blobId, bool uploaded) =>
            (string)label.Invoke(instance, [MemberDocumentKind.EmiratesId, blobId, uploaded])!;

        // Pulled in this session, and My Workspace still has it.
        loaded.Add(MemberDocumentKind.EmiratesId);
        blobs.SetValue(instance, new HashSet<int> { 7 });
        Check.That("a document still in My Workspace says so", Say(7, true) == "Loaded from My Workspace");

        // The case that started this: removed from My Workspace, still on the declaration. The
        // card used to go on claiming My Workspace as its source.
        blobs.SetValue(instance, new HashSet<int>());
        Check.That("a document My Workspace no longer has does not claim to come from it",
                   Say(7, true) == "Held by this declaration");

        // Carried over from last quarter's declaration: never pulled this session, never in the
        // workspace. Not an error, and not "uploaded here" either.
        loaded.Remove(MemberDocumentKind.EmiratesId);
        Check.That("a document carried over from the last declaration is described honestly",
                   Say(7, true) == "Held by this declaration");

        // Uploaded straight onto the declaration, and the member also keeps it in My Workspace.
        blobs.SetValue(instance, new HashSet<int> { 7 });
        Check.That("a document the workspace also holds says that much", Say(7, true) == "Also in My Workspace");

        Check.That("an empty slot still prompts for a file", Say(null, false).Contains("JPG", StringComparison.Ordinal));

        UploadReachesTheWorkspace();
    }

    /// <summary>An Emirates ID uploaded while filling in the Insider declaration used to go only
    /// onto that declaration. My Workspace never heard about it, and the Related Party &amp; COI
    /// declaration -- which pre-fills from My Workspace -- showed nothing, so the member was asked
    /// for the same card twice. The upload handler now writes the member's standing record too.
    ///
    /// Read from the source rather than the assembly: what matters is that the one upload path
    /// every slot goes through still makes the call, and a private async call leaves nothing in
    /// the public surface to reflect over.</summary>
    private static void UploadReachesTheWorkspace()
    {
        Check.Section("A document uploaded on a declaration reaches My Workspace");

        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "src", "CGTOOL.Web"))) dir = dir.Parent;
        if (dir is null)
        {
            Check.That("the source can be read from here", false);
            return;
        }

        var file = Path.Combine(dir.FullName, "src", "CGTOOL.Web", "Components", "Pages", "SubmitInsiderDeclaration.razor.cs");
        if (!File.Exists(file))
        {
            Check.That("the Insider declaration's code-behind is where it is expected", false);
            return;
        }

        var source = File.ReadAllText(file);
        var handler = source.IndexOf("private async Task UploadIdDocumentAsync", StringComparison.Ordinal);
        var next = handler < 0 ? -1 : source.IndexOf("\n    // ", handler + 1, StringComparison.Ordinal);
        var body = handler < 0 ? string.Empty : source[handler..(next < 0 ? source.Length : next)];

        Check.That("every slot still uploads through one handler", handler >= 0);
        Check.That("that handler puts the file on the member's My Workspace record",
                   body.Contains("SyncUploadToWorkspaceAsync", StringComparison.Ordinal));
        Check.That("the three identity slots and the added types are mapped onto a document kind",
                   source.Contains("\"emirates-id\" => SyncToWorkspaceAsync", StringComparison.Ordinal)
                   && source.Contains("\"passport\" => SyncToWorkspaceAsync", StringComparison.Ordinal)
                   && source.Contains("\"trade-licence\" => SyncToWorkspaceAsync", StringComparison.Ordinal)
                   && source.Contains("kind.StartsWith(\"type-\"", StringComparison.Ordinal));

        // Deliberate, and the kind of decision that gets quietly reversed by somebody tidying up
        // an inconsistent-looking switch. My Workspace keeps untyped Other documents as a list,
        // so the declaration's single Other slot matches no row there: an upsert would overwrite
        // an unrelated document and an insert would add one on every re-upload.
        Check.That("the free Other slot is left alone, because My Workspace keeps a list of those",
                   source.Contains("\"other-document\" => Task.CompletedTask", StringComparison.Ordinal));

        Check.That("replacing a file re-dates the record, so My Workspace does not show the old date",
                   source.Contains("existing.UploadedAtUtc = DateTime.UtcNow", StringComparison.Ordinal));

        OnboardingReachesTheWorkspace(dir.FullName);
    }

    /// <summary>Onboarding used to put the Emirates ID into My Workspace only when the whole of
    /// onboarding completed, so a member who typed the name on their card in step 1 and stopped
    /// had typed it into nothing. And the correction screen let them fix every field except the
    /// card itself: it showed the copy held with no way to replace it.</summary>
    private static void OnboardingReachesTheWorkspace(string root)
    {
        Check.Section("Onboarding's Emirates ID reaches My Workspace");

        var service = Path.Combine(root, "src", "CGTOOL.Web", "Data", "Governance", "MemberOnboardingService.cs");
        var page = Path.Combine(root, "src", "CGTOOL.Web", "Components", "Pages", "Onboarding.razor");

        Check.That("the onboarding service and page are where they are expected",
                   File.Exists(service) && File.Exists(page));
        if (!File.Exists(service) || !File.Exists(page)) return;

        var source = File.ReadAllText(service);

        foreach (var method in new[] { "ConfirmEmiratesIdAsync", "CorrectEmiratesIdAsync", "SubmitEmiratesIdAsync" })
        {
            var at = source.IndexOf($"public async Task {method}", StringComparison.Ordinal);
            var end = at < 0 ? -1 : source.IndexOf("\n    public ", at + 1, StringComparison.Ordinal);
            var body = at < 0 ? string.Empty : source[at..(end < 0 ? source.Length : end)];
            Check.That($"{method} puts the card on the member's My Workspace record",
                       body.Contains("SyncEmiratesIdAsync", StringComparison.Ordinal));
        }

        Check.That("a correction the member typed wins over what was held, a confirmation does not",
                   source.Contains("SyncEmiratesIdAsync(o, overwrite: true", StringComparison.Ordinal)
                   && source.Contains("SyncEmiratesIdAsync(o, overwrite: false", StringComparison.Ordinal));

        Check.That("replacing the copy of the card counts as a correction",
                   source.Contains("BlobId != o.EmiratesIdBlobId", StringComparison.Ordinal));

        var markup = File.ReadAllText(page);
        var pickers = markup.Split("OnChange=\"UploadCardAsync\"").Length - 1;
        Check.That($"the card can be uploaded from the correction screen as well as the first step (found {pickers})",
                   pickers >= 2);
    }
}
