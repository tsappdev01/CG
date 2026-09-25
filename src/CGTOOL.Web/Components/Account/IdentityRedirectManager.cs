using System.Diagnostics.CodeAnalysis;
using Microsoft.AspNetCore.Components;

namespace CGTOOL.Web.Components.Account;

internal sealed class IdentityRedirectManager(NavigationManager navigationManager)
{
    public const string StatusCookieName = "Identity.StatusMessage";

    private static readonly CookieBuilder StatusCookieBuilder = new()
    {
        SameSite = SameSiteMode.Strict,
        HttpOnly = true,
        IsEssential = true,
        MaxAge = TimeSpan.FromSeconds(5),
    };

    // NavigateTo below throws NavigationException by design, and the framework catches it to
    // perform the redirect (see the comment at the call site). Nothing in our code catches it, so
    // under Just My Code the debugger reports it as "Exception User-Unhandled" and halts on every
    // sign-in, sign-out and Manage redirect -- which looks exactly like a hung login. Press
    // Continue (F5) and the redirect completes; nothing is actually wrong.
    //
    // There is no code fix for this. DebuggerDisableUserUnhandledExceptionsAttribute does not
    // help, though it reads as though it should: it suppresses the break only for an exception
    // that the attributed method *catches*, and this one passes straight through. It belongs on
    // the framework method that catches it, which is not ours to annotate. It was tried here and
    // removed.
    //
    // The fix is a per-developer Visual Studio setting -- Exception Settings, "Continue When
    // Unhandled in User Code" on Microsoft.AspNetCore.Components.NavigationException. See
    // "The debugger stops on NavigationException" in README.md.
    [DoesNotReturn]
    public void RedirectTo(string? uri)
    {
        uri ??= "";

        // Prevent open redirects.
        if (!Uri.IsWellFormedUriString(uri, UriKind.Relative))
        {
            uri = navigationManager.ToBaseRelativePath(uri);
        }

        // During static rendering, NavigateTo throws a NavigationException which is handled by the framework as a redirect.
        // So as long as this is called from a statically rendered Identity component, the InvalidOperationException is never thrown.
        navigationManager.NavigateTo(uri);
        throw new InvalidOperationException($"{nameof(IdentityRedirectManager)} can only be used during static rendering.");
    }

    [DoesNotReturn]
    public void RedirectTo(string uri, Dictionary<string, object?> queryParameters)
    {
        var uriWithoutQuery = navigationManager.ToAbsoluteUri(uri).GetLeftPart(UriPartial.Path);
        var newUri = navigationManager.GetUriWithQueryParameters(uriWithoutQuery, queryParameters);
        RedirectTo(newUri);
    }

    [DoesNotReturn]
    public void RedirectToWithStatus(string uri, string message, HttpContext context)
    {
        context.Response.Cookies.Append(StatusCookieName, message, StatusCookieBuilder.Build(context));
        RedirectTo(uri);
    }

    private string CurrentPath => navigationManager.ToAbsoluteUri(navigationManager.Uri).GetLeftPart(UriPartial.Path);

    [DoesNotReturn]
    public void RedirectToCurrentPage() => RedirectTo(CurrentPath);

    [DoesNotReturn]
    public void RedirectToCurrentPageWithStatus(string message, HttpContext context)
        => RedirectToWithStatus(CurrentPath, message, context);
}
