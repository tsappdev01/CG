using PdfSharp.Drawing;
using PdfSharp.Pdf;

namespace CGTOOL.Web.Data.Governance;

/// <summary>Renders a submitted Insider Trading declaration as a corporate-branded PDF (DI navy/gold,
/// matching WelcomeEmailTemplate's email styling and the My Declarations print preview) for attachment
/// to the post-submission confirmation email. SQL Server Database Mail's sp_send_dbmail has no
/// "attach these bytes" parameter, so the caller (SqlDbMailSender.SendWithFileAttachmentAsync) writes
/// the returned bytes to a shared folder first and references that path via @file_attachments.</summary>
public static class InsiderDeclarationPdfBuilder
{
    public static byte[] Build(InsiderDeclaration d, Member member, DeclarationCycleRun run, string? logoFilePath)
    {
        using var document = new PdfDocument();
        var logo = logoFilePath is not null && File.Exists(logoFilePath) ? XImage.FromFile(logoFilePath) : null;
        var writer = new DeclarationPdfWriter(document, logo,
            "Insider Trading Declaration — Confirmation",
            "Confidential — for the intended recipient only. This is a system-generated record of your Insider Trading declaration.");

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
        ]);

        writer.SectionHeading("Declaration Summary");
        writer.KeyValueTable(
        [
            ("Holds a NIN", d.HasNin ? $"Yes ({d.NinNumber})" : "No"),
            ("Relatives have a NIN", d.RelativesHaveNin ? "Yes" : "No"),
            ("Holds shares in DI (self or relatives)", d.HoldsShares ? "Yes" : "No"),
            ("Emirates ID", string.IsNullOrEmpty(d.EmiratesIdPath) ? "—" : $"{d.EmiratesIdNumber} — {d.EmiratesIdNameOnCard}, expires {d.EmiratesIdExpiryDate:dd MMM yyyy}"),
            ("Passport", string.IsNullOrEmpty(d.PassportPath) ? "—" : $"{d.PassportNumber} ({d.PassportIssuingCountry}), expires {d.PassportExpiryDate:dd MMM yyyy}"),
            .. string.IsNullOrEmpty(d.TradeLicencePath)
                ? Array.Empty<(string, string)>()
                : [("Trade Licence", $"{d.TradeLicenceNumber} — {d.TradeLicenceLegalName}" +
                    (d.TradeLicenceExpiryDate is null ? "" : $", expires {d.TradeLicenceExpiryDate:dd MMM yyyy}"))],
        ]);

        if (d.RelativesHaveNin && d.NinHolders.Count > 0)
        {
            writer.SectionHeading("Relatives' NIN");
            writer.DataTable(
                ["Share Held By", "Name of Share Holder", "NIN", "Additional"],
                [0.20, 0.30, 0.22, 0.28],
                d.NinHolders.Select(h => new[] { RelationshipLabel(h.Relationship), h.NameOfShareHolder, h.NinNumber, h.Additional ?? "" }));
        }

        if (d.HoldsShares && d.Relatives.Count > 0)
        {
            writer.SectionHeading("Shareholding");
            writer.DataTable(
                ["Share Held By", "Name of Share Holder", "NIN", "Additional"],
                [0.20, 0.30, 0.22, 0.28],
                d.Relatives.Select(r => new[]
                {
                    r.IsSelf ? "Self" : RelationshipLabel(r.Relationship),
                    r.IsSelf ? member.FullName : r.RelativeName,
                    r.NinNumber ?? "",
                    r.Additional ?? "",
                }));
        }

        writer.CloseCurrentPage();
        writer.FinishAllPages();

        using var ms = new MemoryStream();
        document.Save(ms);
        return ms.ToArray();
    }

    private static string RelationshipLabel(RelativeRelationship relationship) => relationship switch
    {
        RelativeRelationship.InLaws => "In-Laws",
        RelativeRelationship.FatherInLaw => "Father-in-Law",
        RelativeRelationship.MotherInLaw => "Mother-in-Law",
        _ => relationship.ToString(),
    };
}
