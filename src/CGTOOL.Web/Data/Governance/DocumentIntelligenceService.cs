using Azure;
using Azure.AI.DocumentIntelligence;

namespace CGTOOL.Web.Data.Governance;

/// <summary>Extracted fields from an ID document (Emirates ID or passport) via Azure AI Document
/// Intelligence's prebuilt-idDocument model. Any field can come back null if the model couldn't read
/// it -- the caller pre-fills the corresponding form field but always leaves it editable, since OCR
/// on a photographed ID is never guaranteed accurate.</summary>
public record IdDocumentExtraction(
    string? DocumentNumber,
    string? FirstName,
    string? LastName,
    DateTime? DateOfExpiration,
    string? CountryRegion);

/// <summary>Extracted fields from a trade licence. Unlike Emirates ID/passport, Document Intelligence
/// has no purpose-built prebuilt model for trade licences (there's no such document type in its
/// prebuilt catalog, and training a custom model needs a labelled sample set this app doesn't have),
/// so this runs the general-purpose "prebuilt-document" model and pattern-matches its generic
/// key/value pairs against likely field labels. Materially less reliable than the ID document
/// extraction -- treat it as a rough first pass, not authoritative.</summary>
public record TradeLicenceExtraction(
    string? LicenceNumber,
    string? BusinessName,
    DateTime? ExpiryDate);

public interface IDocumentIntelligenceService
{
    /// <summary>False when Endpoint/ApiKey aren't configured -- callers fall back to manual entry
    /// rather than calling AnalyzeIdDocumentAsync/AnalyzeTradeLicenceAsync.</summary>
    bool IsConfigured { get; }

    Task<IdDocumentExtraction?> AnalyzeIdDocumentAsync(Stream fileStream, CancellationToken cancellationToken = default);

    Task<TradeLicenceExtraction?> AnalyzeTradeLicenceAsync(Stream fileStream, CancellationToken cancellationToken = default);
}

/// <summary>Wraps Azure AI Document Intelligence's prebuilt-idDocument model (Functional Spec §3.3
/// "Automated Document Capture") to read Emirates ID / passport fields from the uploaded file instead
/// of requiring the declarant to type them in.
///
/// SECURITY: Endpoint and ApiKey MUST come from configuration (user-secrets in dev, Key Vault or the
/// App Service's Application Settings in production) -- never hard-code a real endpoint or key here or
/// commit one to appsettings.json. A prior version of the source Functional Spec document embedded a
/// live key in plain text; that was flagged as a security finding and must not be reproduced.</summary>
public class DocumentIntelligenceService : IDocumentIntelligenceService
{
    private readonly DocumentIntelligenceClient? _client;

    public DocumentIntelligenceService(IConfiguration configuration)
    {
        var endpoint = configuration["DocumentIntelligence:Endpoint"];
        var apiKey = configuration["DocumentIntelligence:ApiKey"];

        if (!string.IsNullOrWhiteSpace(endpoint) && !string.IsNullOrWhiteSpace(apiKey))
        {
            _client = new DocumentIntelligenceClient(new Uri(endpoint), new AzureKeyCredential(apiKey));
        }
    }

    public bool IsConfigured => _client is not null;

    public async Task<IdDocumentExtraction?> AnalyzeIdDocumentAsync(Stream fileStream, CancellationToken cancellationToken = default)
    {
        if (_client is null) return null;

        var content = BinaryData.FromStream(fileStream);
        Operation<AnalyzeResult> operation = await _client.AnalyzeDocumentAsync(
            WaitUntil.Completed, "prebuilt-idDocument", content, cancellationToken: cancellationToken);

        var document = operation.Value.Documents.FirstOrDefault();
        if (document is null) return null;

        return new IdDocumentExtraction(
            DocumentNumber: GetString(document, "DocumentNumber"),
            FirstName: GetString(document, "FirstName"),
            LastName: GetString(document, "LastName"),
            DateOfExpiration: GetDate(document, "DateOfExpiration"),
            CountryRegion: GetString(document, "CountryRegion"));
    }

    private static string? GetString(AnalyzedDocument document, string fieldName) =>
        document.Fields.TryGetValue(fieldName, out var field) ? field.Content : null;

    private static DateTime? GetDate(AnalyzedDocument document, string fieldName)
    {
        if (!document.Fields.TryGetValue(fieldName, out var field)) return null;
        try
        {
            return field.ValueDate?.Date;
        }
        catch
        {
            return null;
        }
    }

    // Model ID: "prebuilt-document" doesn't exist on the API version this resource serves (404
    // ModelNotFound) -- it was retired in favor of "prebuilt-layout", the stable general-purpose
    // model available on every Document Intelligence resource/API version. Deliberately does NOT
    // request DocumentAnalysisFeature.KeyValuePairs either: that structured extraction is a paid
    // add-on capability rejected outright by some resource tiers/model combinations (400
    // InvalidArgument, "The feature is invalid or not supported"). Reading the labels out of the
    // plain extracted text instead works everywhere -- see TradeLicenceTextParser.
    public async Task<TradeLicenceExtraction?> AnalyzeTradeLicenceAsync(Stream fileStream, CancellationToken cancellationToken = default)
    {
        if (_client is null) return null;

        var content = BinaryData.FromStream(fileStream);
        Operation<AnalyzeResult> operation = await _client.AnalyzeDocumentAsync(
            WaitUntil.Completed, "prebuilt-layout", content, cancellationToken: cancellationToken);

        var text = operation.Value.Content;
        if (string.IsNullOrWhiteSpace(text)) return null;

        return TradeLicenceTextParser.Parse(text);
    }
}

/// <summary>Used when DocumentIntelligence:Endpoint/ApiKey aren't configured -- AnalyzeIdDocumentAsync
/// is never actually called (callers check IsConfigured first), but a real implementation is always
/// registered so pages can inject IDocumentIntelligenceService unconditionally.</summary>
public class NotConfiguredDocumentIntelligenceService : IDocumentIntelligenceService
{
    public bool IsConfigured => false;

    public Task<IdDocumentExtraction?> AnalyzeIdDocumentAsync(Stream fileStream, CancellationToken cancellationToken = default) =>
        Task.FromResult<IdDocumentExtraction?>(null);

    public Task<TradeLicenceExtraction?> AnalyzeTradeLicenceAsync(Stream fileStream, CancellationToken cancellationToken = default) =>
        Task.FromResult<TradeLicenceExtraction?>(null);
}
