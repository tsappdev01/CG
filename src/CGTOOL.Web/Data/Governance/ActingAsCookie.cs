using Microsoft.AspNetCore.DataProtection;

namespace CGTOOL.Web.Data.Governance;

/// <summary>Carries "who I am currently acting on behalf of" between requests.
///
/// A cookie rather than circuit state, because a circuit dies on refresh and losing this silently
/// would mean the next declaration filed goes in under the signed-in user's own name. It is signed
/// with the data protector -- the value is a Member id, and a cookie the browser can edit would
/// otherwise let anyone act as anyone. HttpOnly and SameSite=Strict: nothing in the page needs to
/// read it, and no other site should be able to make the browser send it.</summary>
public class ActingAsCookie(IDataProtectionProvider dataProtection)
{
    private const string CookieName = "cgtool_acting_as";

    private readonly IDataProtector _protector = dataProtection.CreateProtector("ActingAs");

    public ActingAsSnapshot? Read(HttpContext? httpContext)
    {
        var value = httpContext?.Request.Cookies[CookieName];
        if (string.IsNullOrEmpty(value)) return null;

        try
        {
            var parts = _protector.Unprotect(value).Split('|', 2);
            return parts.Length == 2 && int.TryParse(parts[0], out var id)
                ? new ActingAsSnapshot(id, parts[1])
                : null;
        }
        catch (System.Security.Cryptography.CryptographicException)
        {
            // Tampered with, or protected under a key this instance no longer has. Either way the
            // answer is "acting as nobody", not an error page.
            return null;
        }
    }

    public void Write(HttpContext httpContext, int memberId, string memberName) =>
        httpContext.Response.Cookies.Append(CookieName, _protector.Protect($"{memberId}|{memberName}"), Options());

    public void Clear(HttpContext httpContext) => httpContext.Response.Cookies.Delete(CookieName, Options());

    // No Expires: it goes when the browser session does, which is what "for this session" means.
    private static CookieOptions Options() => new()
    {
        HttpOnly = true,
        IsEssential = true,
        SameSite = SameSiteMode.Strict,
        Secure = true,
        Path = "/",
    };
}
