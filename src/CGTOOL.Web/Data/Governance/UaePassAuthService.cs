using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace CGTOOL.Web.Data.Governance;

/// <summary>The subset of UAE PASS's UserInfo claims this app actually needs to record who verified
/// a declaration. UAE PASS's own field names/casing (fullnameEN, mobile, idn, etc.) aren't guaranteed
/// stable across environments, so ExtractIdentity below reads them defensively.</summary>
public record UaePassIdentity(string Uuid, string FullName, string? Email, string? MobileNumber, string? EmiratesId);

public interface IUaePassAuthService
{
    /// <summary>False when ClientId/ClientSecret aren't configured -- callers must not gate anything
    /// on UAE PASS verification while this is false (falls back to the pre-UAE-PASS submission flow).</summary>
    bool IsConfigured { get; }

    /// <summary>Builds the URL to send the browser to for UAE PASS login. <paramref name="state"/>
    /// should be an opaque, tamper-proof token (e.g. IDataProtector-protected) the callback can use to
    /// identify what triggered the login -- UAE PASS returns it unchanged.</summary>
    string BuildAuthorizeUrl(string state, string redirectUri);

    /// <summary>Exchanges the authorization code UAE PASS redirected back with for an access token,
    /// then calls UserInfo to resolve the declarant's identity. Returns null on any failure (network,
    /// non-success response, or unreadable payload) -- callers should treat that as "verification did
    /// not complete" and let the declarant retry, not as an exception-worthy condition.</summary>
    Task<UaePassIdentity?> HandleCallbackAsync(string code, string redirectUri, CancellationToken cancellationToken = default);

    /// <summary>UAE PASS requires the exact same redirect_uri on both the authorize request and the
    /// callback's token exchange, and it must match a value that was pre-registered with UAE PASS for
    /// this ClientId -- the app's own dynamically-computed URL (e.g. "https://host/uaepass/callback")
    /// only works if that's what was actually registered. Set UaePass:RedirectUri to override it with
    /// whatever was registered instead (e.g. a fixed demo/POC value like a shared sandbox client's).</summary>
    string ResolveRedirectUri(string computedDefault);
}

/// <summary>Wraps UAE PASS's OAuth2 login (Authorization Code flow) so a declarant can prove their
/// identity before a Related Party &amp; COI declaration can be finally submitted. This is an identity
/// check only -- it does not use UAE PASS's remote e-signature (eSignSP/CreateSignProcess) APIs, so
/// the declaration's typed-name attestation checkbox remains the actual "signature."
///
/// SECURITY: ClientId/ClientSecret MUST come from configuration (user-secrets in dev, Key Vault or the
/// App Service's Application Settings in production) -- never hard-code real values here or commit
/// them to appsettings.json, same rule as DocumentIntelligenceService/AzureAd.
///
/// NOTE: BaseUrl/Scope/AcrValues here match UAE PASS's own published POC config for the
/// "sandbox_stage" staging client (https://docs.uaepass.ae/quick-start-guide-uae-pass-staging-environment) --
/// staging is a materially different app registration from production (id.uaepass.ae), so
/// "sandbox_stage" only authenticates against stg-id.uaepass.ae and will fail with
/// invalid_client/application.not.found against production.</summary>
public class UaePassAuthService(IHttpClientFactory httpClientFactory, IConfiguration configuration, ILogger<UaePassAuthService> logger) : IUaePassAuthService
{
    private readonly string _baseUrl = (configuration["UaePass:BaseUrl"] ?? string.Empty).TrimEnd('/');
    private readonly string _clientId = configuration["UaePass:ClientId"] ?? string.Empty;
    private readonly string _clientSecret = configuration["UaePass:ClientSecret"] ?? string.Empty;
    private readonly string _scope = configuration["UaePass:Scope"] ?? string.Empty;
    private readonly string _acrValues = configuration["UaePass:AcrValues"] ?? "urn:safelayer:tws:policies:authentication:level:low";
    private readonly string _authorizeEndpoint = configuration["UaePass:Endpoints:Authorize"] ?? "idshub/authorize";
    private readonly string _accessTokenEndpoint = configuration["UaePass:Endpoints:AccessToken"] ?? "idshub/token?grant_type=authorization_code&redirect_uri={{{authRedirecturi}}}&code={{{Code}}}";
    private readonly string _userInfoEndpoint = configuration["UaePass:Endpoints:UserInfo"] ?? "idshub/userinfo";
    private readonly string _redirectUriOverride = configuration["UaePass:RedirectUri"] ?? string.Empty;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_clientId) && !string.IsNullOrWhiteSpace(_clientSecret);

    public string ResolveRedirectUri(string computedDefault) =>
        string.IsNullOrWhiteSpace(_redirectUriOverride) ? computedDefault : _redirectUriOverride;

    public string BuildAuthorizeUrl(string state, string redirectUri)
    {
        var query = string.Join("&",
            $"client_id={Uri.EscapeDataString(_clientId)}",
            "response_type=code",
            $"scope={Uri.EscapeDataString(_scope)}",
            $"acr_values={Uri.EscapeDataString(_acrValues)}",
            $"redirect_uri={Uri.EscapeDataString(redirectUri)}",
            $"state={Uri.EscapeDataString(state)}");

        return $"{_baseUrl}/{_authorizeEndpoint.TrimStart('/')}?{query}";
    }

    public async Task<UaePassIdentity?> HandleCallbackAsync(string code, string redirectUri, CancellationToken cancellationToken = default)
    {
        try
        {
            var http = httpClientFactory.CreateClient("UaePass");

            var tokenPath = _accessTokenEndpoint
                .Replace("{{{authRedirecturi}}}", Uri.EscapeDataString(redirectUri))
                .Replace("{{{Code}}}", Uri.EscapeDataString(code))
                .TrimStart('/');

            using var tokenRequest = new HttpRequestMessage(HttpMethod.Post, tokenPath);
            tokenRequest.Headers.Authorization = new AuthenticationHeaderValue("Basic",
                Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_clientId}:{_clientSecret}")));

            using var tokenResponse = await http.SendAsync(tokenRequest, cancellationToken);
            if (!tokenResponse.IsSuccessStatusCode)
            {
                logger.LogWarning("UAE PASS token exchange failed with status {StatusCode}", tokenResponse.StatusCode);
                return null;
            }

            using var tokenDoc = JsonDocument.Parse(await tokenResponse.Content.ReadAsStringAsync(cancellationToken));
            if (!tokenDoc.RootElement.TryGetProperty("access_token", out var accessTokenElement))
            {
                logger.LogWarning("UAE PASS token response did not contain an access_token");
                return null;
            }
            var accessToken = accessTokenElement.GetString();
            if (string.IsNullOrWhiteSpace(accessToken)) return null;

            using var userInfoRequest = new HttpRequestMessage(HttpMethod.Get, _userInfoEndpoint.TrimStart('/'));
            userInfoRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

            using var userInfoResponse = await http.SendAsync(userInfoRequest, cancellationToken);
            if (!userInfoResponse.IsSuccessStatusCode)
            {
                logger.LogWarning("UAE PASS UserInfo call failed with status {StatusCode}", userInfoResponse.StatusCode);
                return null;
            }

            using var userInfoDoc = JsonDocument.Parse(await userInfoResponse.Content.ReadAsStringAsync(cancellationToken));
            return ExtractIdentity(userInfoDoc.RootElement);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "UAE PASS callback handling failed");
            return null;
        }
    }

    private static UaePassIdentity? ExtractIdentity(JsonElement root)
    {
        var uuid = FirstNonEmpty(root, "uuid", "sub");
        if (string.IsNullOrWhiteSpace(uuid)) return null;

        var fullName = FirstNonEmpty(root, "fullnameEN", "fullnameAR", "name") ?? "UAE PASS user";
        var email = FirstNonEmpty(root, "email");
        var mobile = FirstNonEmpty(root, "mobile", "mobileNo", "phoneNumber");
        var emiratesId = FirstNonEmpty(root, "idn", "idnNumber");

        return new UaePassIdentity(uuid, fullName, email, mobile, emiratesId);
    }

    private static string? FirstNonEmpty(JsonElement root, params string[] propertyNames)
    {
        foreach (var name in propertyNames)
        {
            if (root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String)
            {
                var s = value.GetString();
                if (!string.IsNullOrWhiteSpace(s)) return s;
            }
        }
        return null;
    }
}

/// <summary>Used when UaePass:ClientId/ClientSecret aren't configured -- callers must check
/// IsConfigured (false here) before offering UAE PASS verification, so neither method should ever
/// actually be invoked; they throw rather than silently no-op if a caller forgets that check.</summary>
public class NotConfiguredUaePassAuthService : IUaePassAuthService
{
    public bool IsConfigured => false;

    public string ResolveRedirectUri(string computedDefault) => computedDefault;

    public string BuildAuthorizeUrl(string state, string redirectUri) =>
        throw new InvalidOperationException("UAE PASS is not configured.");

    public Task<UaePassIdentity?> HandleCallbackAsync(string code, string redirectUri, CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException("UAE PASS is not configured.");
}
