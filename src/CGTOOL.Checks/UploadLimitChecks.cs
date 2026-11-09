using System.Reflection;
using CGTOOL.Web.Data.Governance;

namespace CGTOOL.Checks;

/// <summary>
/// The size limit lived in a const on each page while the sentence a member reads -- "up to 1 MB"
/// -- was typed into the markup separately, seven times over. Raising the limit meant finding all
/// of them, and a screen that enforces one number while promising another is worse than either.
///
/// These check that the number and the words come from one place, and that every page's constant
/// is one of the tiers rather than its own figure.
/// </summary>
public static class UploadLimitChecks
{
    private const long Mb = 1024 * 1024;

    public static void Run()
    {
        Check.Section("Upload limits");

        Check.That("an identity document may be at least 5 MB", UploadLimits.DocumentBytes >= 5 * Mb);

        // A phone photograph of an Emirates ID is routinely over a megabyte, which is what the old
        // limit cost: members scaled images down to get past the check, so the copy on file of an
        // identity document was the least legible one they could make it.
        Check.That("the tiers go up, not sideways",
                   UploadLimits.DocumentBytes <= UploadLimits.SupportingDocumentBytes
                   && UploadLimits.SupportingDocumentBytes <= UploadLimits.BulkBytes);

        Check.That("a picture limit is no smaller than a document's",
                   UploadLimits.PictureBytes >= UploadLimits.DocumentBytes);

        Check.Section("The words agree with the number");

        Check.That("5 MB reads as \"5 MB\"", UploadLimits.Describe(5 * Mb) == "5 MB");
        Check.That("the document limit reads back as itself",
                   UploadLimits.DocumentLimit == UploadLimits.Describe(UploadLimits.DocumentBytes));
        Check.That("the hint on an empty card names the real limit",
                   UploadLimits.DocumentHint.Contains(UploadLimits.DocumentLimit, StringComparison.Ordinal));

        // "Too large" on its own leaves the member guessing by how much.
        var complaint = UploadLimits.TooLarge(7 * Mb);
        Check.That("being told the file is too big names the file's own size",
                   complaint.Contains("7", StringComparison.Ordinal));
        Check.That("and names the limit it broke",
                   complaint.Contains(UploadLimits.DocumentLimit, StringComparison.Ordinal));
        Check.That("a limit other than the default is named too",
                   UploadLimits.TooLarge(30 * Mb, UploadLimits.BulkBytes)
                       .Contains(UploadLimits.BulkLimit, StringComparison.Ordinal));
        Check.That("a part-megabyte file is still explained",
                   UploadLimits.TooLarge(400 * 1024).Contains(UploadLimits.DocumentLimit, StringComparison.Ordinal));

        Check.Section("Every page uses a tier, not its own number");

        var tiers = new HashSet<long>
        {
            UploadLimits.DocumentBytes,
            UploadLimits.SupportingDocumentBytes,
            UploadLimits.BulkBytes,
            UploadLimits.PictureBytes,
        };

        // Walks the built assembly rather than a list someone maintains, so a page added later is
        // checked without being registered anywhere.
        var offenders = new List<string>();
        foreach (var type in typeof(UploadLimits).Assembly.GetTypes())
        {
            foreach (var field in type.GetFields(BindingFlags.NonPublic | BindingFlags.Public
                                                 | BindingFlags.Static | BindingFlags.Instance))
            {
                if (!field.IsLiteral || field.FieldType != typeof(long)) continue;
                if (type == typeof(UploadLimits)) continue;

                var name = field.Name;
                if (!name.Contains("Bytes", StringComparison.Ordinal)) continue;

                var value = (long)field.GetRawConstantValue()!;
                if (!tiers.Contains(value)) offenders.Add($"{type.Name}.{name} = {value / Mb} MB");
            }
        }

        Check.That(offenders.Count == 0
                       ? "every upload limit is one of the tiers"
                       : $"these set their own limit: {string.Join(", ", offenders)}",
                   offenders.Count == 0);
    }
}
