using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Primitives;
using CGTOOL.Web.Components.Account.Pages;
using CGTOOL.Web.Components.Account.Pages.Manage;
using CGTOOL.Web.Data;

namespace Microsoft.AspNetCore.Routing;

internal static class IdentityComponentsEndpointRouteBuilderExtensions
{
    /// <summary>The external-login provider name the Entra scheme is registered under in
    /// Program.cs. The two have to agree, so it is named once here rather than typed twice.</summary>
    private const string EntraLoginProvider = "AzureAD";

    /// <summary>Keeps a return URL to somewhere in this application. A path that is not plainly
    /// local -- an absolute URL, a protocol-relative "//evil.example", a backslash the browser
    /// will read as a slash -- is dropped for the home page rather than repaired, since a lock
    /// screen that can be pointed at another site is a phishing instrument.</summary>
    private static string LocalOrHome(string? url) =>
        !string.IsNullOrWhiteSpace(url)
        && url.StartsWith('/')
        && !url.StartsWith("//", StringComparison.Ordinal)
        && !url.StartsWith("/\\", StringComparison.Ordinal)
            ? url
            : "/";

    // These endpoints are required by the Identity Razor components defined in the /Components/Account/Pages directory of this project.
    public static IEndpointConventionBuilder MapAdditionalIdentityEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var accountGroup = endpoints.MapGroup("/Account");

        accountGroup.MapPost("/PerformExternalLogin", (
            HttpContext context,
            [FromServices] SignInManager<ApplicationUser> signInManager,
            [FromForm] string provider,
            [FromForm] string returnUrl) =>
        {
            IEnumerable<KeyValuePair<string, StringValues>> query = [
                new("ReturnUrl", returnUrl),
                new("Action", ExternalLogin.LoginCallbackAction)];

            var redirectUrl = UriHelper.BuildRelative(
                context.Request.PathBase,
                "/Account/ExternalLogin",
                QueryString.Create(query));

            var properties = signInManager.ConfigureExternalAuthenticationProperties(provider, redirectUrl);
            return TypedResults.Challenge(properties, [provider]);
        });

        // The idle lock lands here. It is a real sign-out and then a fresh Entra challenge, not an
        // overlay with a password box: the person comes back through the front door and returns to
        // the screen they were on.
        //
        // The sign-out is the part that makes the lock mean anything. While it was only a flag on a
        // component, refreshing the page cleared it -- the session was never actually closed. Here
        // the authentication cookie is gone before the redirect leaves, so Back and F5 land on the
        // sign-in page like any other signed-out visitor.
        accountGroup.MapGet("/Lock", async (
            HttpContext context,
            [FromServices] SignInManager<ApplicationUser> signInManager,
            [FromServices] IAuthenticationSchemeProvider schemes,
            [FromQuery] string? returnUrl) =>
        {
            // A GET that signs you out can be forged from another site -- an <img> tag is enough.
            // The harm is a nuisance rather than a breach, but the browser tells us when a request
            // came from somewhere else, so take it at its word. Browsers that do not send the
            // header are allowed through: refusing them would break the lock for them entirely.
            if (context.Request.Headers["Sec-Fetch-Site"] == "cross-site")
            {
                return Results.LocalRedirect("~/");
            }

            var target = LocalOrHome(returnUrl);

            // Worked out before signing out, not after: once the cookie is gone there is no user to
            // ask which provider they came in with. External members have no Entra account and must
            // go to the sign-in form instead, or the lock would strand them at Microsoft.
            var user = await signInManager.UserManager.GetUserAsync(context.User);
            var viaEntra = user is not null
                && (await signInManager.UserManager.GetLoginsAsync(user))
                    .Any(login => login.LoginProvider == EntraLoginProvider);
            var entraRegistered = await schemes.GetSchemeAsync(EntraLoginProvider) is not null;

            await signInManager.SignOutAsync();

            if (!viaEntra || !entraRegistered)
            {
                return Results.LocalRedirect($"~/Account/Login?ReturnUrl={Uri.EscapeDataString(target)}");
            }

            IEnumerable<KeyValuePair<string, StringValues>> query = [
                new("ReturnUrl", target),
                new("Action", ExternalLogin.LoginCallbackAction)];

            var redirectUrl = UriHelper.BuildRelative(
                context.Request.PathBase,
                "/Account/ExternalLogin",
                QueryString.Create(query));

            var properties = signInManager.ConfigureExternalAuthenticationProperties(EntraLoginProvider, redirectUrl);

            // Read back by OnRedirectToIdentityProvider in Program.cs. Without it Entra honours its
            // own live session and returns a token without challenging anyone.
            properties.Items["prompt"] = "login";

            return Results.Challenge(properties, [EntraLoginProvider]);
        });

        accountGroup.MapPost("/Logout", async (
            ClaimsPrincipal user,
            SignInManager<ApplicationUser> signInManager,
            [FromForm] string returnUrl) =>
        {
            await signInManager.SignOutAsync();
            return TypedResults.LocalRedirect($"~/{returnUrl}");
        });

        var manageGroup = accountGroup.MapGroup("/Manage").RequireAuthorization();

        manageGroup.MapPost("/LinkExternalLogin", async (
            HttpContext context,
            [FromServices] SignInManager<ApplicationUser> signInManager,
            [FromForm] string provider) =>
        {
            // Clear the existing external cookie to ensure a clean login process
            await context.SignOutAsync(IdentityConstants.ExternalScheme);

            var redirectUrl = UriHelper.BuildRelative(
                context.Request.PathBase,
                "/Account/Manage/ExternalLogins",
                QueryString.Create("Action", ExternalLogins.LinkLoginCallbackAction));

            var properties = signInManager.ConfigureExternalAuthenticationProperties(provider, redirectUrl, signInManager.UserManager.GetUserId(context.User));
            return TypedResults.Challenge(properties, [provider]);
        });

        var loggerFactory = endpoints.ServiceProvider.GetRequiredService<ILoggerFactory>();
        var downloadLogger = loggerFactory.CreateLogger("DownloadPersonalData");

        manageGroup.MapPost("/DownloadPersonalData", async (
            HttpContext context,
            [FromServices] UserManager<ApplicationUser> userManager,
            [FromServices] AuthenticationStateProvider authenticationStateProvider) =>
        {
            var user = await userManager.GetUserAsync(context.User);
            if (user is null)
            {
                return Results.NotFound($"Unable to load user with ID '{userManager.GetUserId(context.User)}'.");
            }

            var userId = await userManager.GetUserIdAsync(user);
            downloadLogger.LogInformation("User with ID '{UserId}' asked for their personal data.", userId);

            // Only include personal data for download
            var personalData = new Dictionary<string, string>();
            var personalDataProps = typeof(ApplicationUser).GetProperties().Where(
                prop => Attribute.IsDefined(prop, typeof(PersonalDataAttribute)));
            foreach (var p in personalDataProps)
            {
                personalData.Add(p.Name, p.GetValue(user)?.ToString() ?? "null");
            }

            var logins = await userManager.GetLoginsAsync(user);
            foreach (var l in logins)
            {
                personalData.Add($"{l.LoginProvider} external login provider key", l.ProviderKey);
            }

            personalData.Add("Authenticator Key", (await userManager.GetAuthenticatorKeyAsync(user))!);
            var fileBytes = JsonSerializer.SerializeToUtf8Bytes(personalData);

            context.Response.Headers.TryAdd("Content-Disposition", "attachment; filename=PersonalData.json");
            return TypedResults.File(fileBytes, contentType: "application/json", fileDownloadName: "PersonalData.json");
        });

        return accountGroup;
    }
}
