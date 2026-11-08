using Microsoft.Extensions.Configuration;

namespace CGTOOL.Web.Data.Governance;

/// <summary>The names of the organisation this installation belongs to, read from configuration so
/// a rename is a setting rather than a release.
///
/// The parent holding company appears on forms, reports, PDFs and emails -- "do you or your
/// relatives hold shares in X", "a conflict can be damaging to them and to X". It was written into
/// each of those places as "DI PJSC" or "Dubai Investments PJSC", in two spellings, which meant a
/// rename was a hunt through the markup and the one that was missed was the one in a PDF nobody
/// opens until an auditor does.
///
/// A default rather than a required setting: an installation with no Branding section keeps
/// working and shows the name below, instead of printing a blank where the company should be.</summary>
public class Branding(IConfiguration configuration)
{
    public const string DefaultParentHoldingCompany = "DIP Holding PLC";

    /// <summary>Where the site answers, when nothing is configured.
    ///
    /// This matters more than it looks. A mail client is not this application, so every link and
    /// the logo in an email need an absolute URL, and the address was written into five places by
    /// hand as cg.dubaiinvestments.com -- a host this deployment moved off. Every declaration email
    /// sent carried a link to the wrong site and a logo that did not load.</summary>
    public const string DefaultPublicBaseUrl = "https://cg.dipark.com";

    /// <summary>Branding:ParentHoldingCompany. Whitespace counts as unset -- a key someone emptied
    /// while editing should fall back to the default, not blank every form in the application.</summary>
    public string ParentHoldingCompany =>
        configuration["Branding:ParentHoldingCompany"] is { } configured && !string.IsNullOrWhiteSpace(configured)
            ? configured.Trim()
            : DefaultParentHoldingCompany;

    /// <summary>Branding:PublicBaseUrl, without a trailing slash. The address emails point at.</summary>
    public string PublicBaseUrl => PublicBaseUrlOf(configuration);

    /// <summary>The same value where there is no Branding to inject -- a background service, or a
    /// static template builder.</summary>
    public static string PublicBaseUrlOf(IConfiguration? configuration) =>
        configuration?["Branding:PublicBaseUrl"] is { } configured && !string.IsNullOrWhiteSpace(configured)
            ? configured.Trim().TrimEnd('/')
            : DefaultPublicBaseUrl;
}
