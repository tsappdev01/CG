using PdfSharp.Drawing;
using PdfSharp.Pdf;

namespace CGTOOL.Web.Data.Governance;

/// <summary>Renders a submitted Related Party &amp; Conflict of Interest declaration as the PDF that
/// goes out with the confirmation email, laid out by DeclarationPdfWriter like its Insider Trading
/// counterpart.
///
/// The point of the document is the attestation at the end: the sentence the declarant signed, their
/// drawn signature, and when they signed it. Everything above it is what they signed -- section by
/// section, in the order the wizard asks for them -- so the PDF stands on its own as the record of
/// what was declared, rather than being a receipt that points back at the app.</summary>
public static class RelatedPartyCoiDeclarationPdfBuilder
{
    /// <summary>The file name the attachment goes out under -- the quarter and the member, so a
    /// mailbox full of these can be told apart.</summary>
    public static string FileName(Member member, DeclarationCycleRun run) =>
        $"RelatedPartyCoiDeclaration_Q{run.PeriodQuarter}_{run.PeriodYear}_{Sanitize(member.FullName)}.pdf";

    private static string Sanitize(string name) =>
        new(name.Select(c => char.IsAsciiLetterOrDigit(c) ? c : '_').ToArray());

    /// <param name="signatureFilePath">The signature PNG on disk, or null where there is none -- a
    /// declaration submitted before signatures were captured.</param>
    public static byte[] Build(
        RelatedPartyCoiDeclaration d,
        Member member,
        DeclarationCycleRun run,
        string? logoFilePath,
        string? signatureFilePath)
    {
        using var document = new PdfDocument();
        var logo = logoFilePath is not null && File.Exists(logoFilePath) ? XImage.FromFile(logoFilePath) : null;
        var writer = new DeclarationPdfWriter(document, logo,
            "Related Party & Conflict of Interest Declaration",
            "Confidential — for the intended recipient only. This is a system-generated record of your Related Party & COI declaration.");

        writer.NewPage();

        writer.SectionHeading("Declarant");
        writer.KeyValueTable(
        [
            ("Name", member.FullName),
            ("Entity", member.Company?.Name ?? "—"),
            ("Period", $"Q{run.PeriodQuarter} {run.PeriodYear}"),
            ("Status", d.IsDraft ? "Draft" : "Submitted"),
            ("Submitted", d.IsDraft ? "—" : d.SubmittedAtUtc.ToLocalDisplay("dd MMM yyyy HH:mm")),
            ("Last modified", d.ModifiedAtUtc?.ToLocalDisplay("dd MMM yyyy HH:mm") ?? "—"),
            .. d.SubmittedOnBehalfOf is { Length: > 0 } onBehalf
                ? new[] { ("Submitted by", $"{d.SubmittedByName} on behalf of {onBehalf}") }
                : [("Submitted by", d.SubmittedByName)],
        ]);

        writer.SectionHeading("Relatives");
        if (d.NothingToDeclareRelatives || d.Relatives.Count == 0)
        {
            writer.Paragraph("Nothing to declare.");
        }
        else
        {
            writer.DataTable(
                ["Name", "Relationship"],
                [0.62, 0.38],
                d.Relatives.Select(r => new[] { r.Name, RelationshipLabel(r.Relationship) }));
        }

        Companies(writer, d, CoiCompanyOwnerType.Self,
            "My companies (I own ≥30% of paid-up share capital)", d.NothingToDeclareSelfOwned);

        Companies(writer, d, CoiCompanyOwnerType.Relative,
            "Relatives' companies (a relative owns ≥30%)", d.NothingToDeclareRelativeOwned);

        Companies(writer, d, CoiCompanyOwnerType.BoardOrExecutiveRole,
            "Board roles (I am a board member or senior executive)", d.NothingToDeclareBoardRoles);

        writer.SectionHeading("Conflict of Interest Declaration");
        if (d.NothingToDeclareConflicts || d.Conflicts.Count == 0)
        {
            writer.Paragraph("Nothing to declare.");
        }
        else
        {
            writer.DataTable(
                ["Company / counterparty", "Principal business activity", "Nature of holding", "Nature of interest"],
                [0.28, 0.26, 0.20, 0.26],
                d.Conflicts.Select(c => new[]
                {
                    c.CompanyOrCounterpartyName,
                    c.PrincipalBusinessActivity ?? "",
                    c.NatureOfHolding ?? "",
                    c.NatureOfInterest ?? "",
                }));
        }

        writer.SectionHeading("Attestation");
        writer.Paragraph($"I, {(string.IsNullOrWhiteSpace(d.AttestationName) ? member.FullName : d.AttestationName)}, "
            + "the undersigned, confirm that the information given in this declaration is true, complete and accurate.");
        writer.Gap(6);

        // The typed name says who signed; the drawn signature is the signing, and it is the reason
        // this document is worth attaching at all. Null only where a declaration predates signatures.
        var signature = signatureFilePath is not null && File.Exists(signatureFilePath)
            ? XImage.FromFile(signatureFilePath)
            : null;
        if (signature is not null)
        {
            writer.Signature(signature, maxWidth: 200, maxHeight: 60);
        }

        writer.KeyValueTable(
        [
            ("Signed by", string.IsNullOrWhiteSpace(d.AttestationName) ? "—" : d.AttestationName),
            ("Confirmed complete and accurate", d.AttestationConfirmed ? "Yes" : "No"),
            ("Signed on", d.SignedAtUtc?.ToLocalDisplay("dd MMM yyyy HH:mm") ?? "—"),
            .. signature is null
                ? new[] { ("Signature", "Not signed") }
                : Array.Empty<(string, string)>(),
        ]);

        writer.CloseCurrentPage();
        writer.FinishAllPages();

        using var ms = new MemoryStream();
        document.Save(ms);
        return ms.ToArray();
    }

    /// <summary>One of the three company tables. They hold the same columns, and the relative-owned
    /// one names the relative whose shareholding it is.</summary>
    private static void Companies(
        DeclarationPdfWriter writer,
        RelatedPartyCoiDeclaration d,
        CoiCompanyOwnerType owner,
        string heading,
        bool nothingToDeclare)
    {
        var rows = d.Companies.Where(c => c.OwnerType == owner).ToList();

        writer.SectionHeading(heading);

        if (nothingToDeclare || rows.Count == 0)
        {
            writer.Paragraph("Nothing to declare.");
            return;
        }

        var namesRelative = owner == CoiCompanyOwnerType.Relative;
        string[] headers = namesRelative
            ? ["Legal name of company", "Relative", "Principal business activity", "Nature of holding", "Trade licence", "Expiry"]
            : ["Legal name of company", "Principal business activity", "Nature of holding", "Trade licence", "Expiry"];
        double[] widths = namesRelative
            ? [0.24, 0.14, 0.22, 0.14, 0.15, 0.11]
            : [0.28, 0.26, 0.16, 0.17, 0.13];

        writer.DataTable(headers, widths, rows.Select(c =>
        {
            var expiry = c.TradeLicenseExpiryDate?.ToString("dd MMM yyyy") ?? "";
            return namesRelative
                ? [c.LegalCompanyName, c.CoiRelative?.Name ?? "", c.PrincipalBusinessActivity ?? "",
                   c.NatureOfHolding ?? "", c.TradeLicenseNumber ?? "", expiry]
                : new[] { c.LegalCompanyName, c.PrincipalBusinessActivity ?? "",
                          c.NatureOfHolding ?? "", c.TradeLicenseNumber ?? "", expiry };
        }));
    }

    private static string RelationshipLabel(RelativeRelationship relationship) => relationship switch
    {
        RelativeRelationship.InLaws => "In-Laws",
        RelativeRelationship.FatherInLaw => "Father-in-Law",
        RelativeRelationship.MotherInLaw => "Mother-in-Law",
        RelativeRelationship.Stepchildren => "Children of spouse",
        _ => relationship.ToString(),
    };
}
