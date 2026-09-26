using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using CGTOOL.Web.Data;
using CGTOOL.Web.Data.Governance;

namespace CGTOOL.Web.Components.Account;

/// <summary>Starting and ending an Administrator's "log in as" support session (UM-03).
///
/// These are HTTP endpoints rather than component event handlers because signing in writes the
/// authentication cookie, and a cookie is a response header. Inside an interactive Blazor circuit the
/// response has long since been sent -- the page is being driven over a websocket -- so the attempt
/// throws "Headers are read-only, response has already started." The buttons post a form to these
/// endpoints instead, which is a fresh request whose headers nobody has written yet.</summary>
internal static class AdminImpersonationEndpoints
{
    public static IEndpointConventionBuilder MapAdminImpersonationEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var group = endpoints.MapGroup("/Account").RequireAuthorization();

        group.MapPost("/ImpersonateStart", async (
            HttpContext context,
            [FromForm] string userId,
            [FromServices] UserManager<ApplicationUser> userManager,
            [FromServices] SignInManager<ApplicationUser> signInManager,
            [FromServices] IAuditLogger auditLog,
            [FromServices] ClientContext clientContext) =>
        {
            // The page only shows the button to an Administrator; the endpoint has to say so itself,
            // since a form post can be made without ever loading that page.
            if (!context.User.IsInRole(GovernanceRoles.Administrator)) return Results.Forbid();

            // A support session cannot start another one: the claims saying whose session to hand
            // back to would be overwritten with the impersonated user's own identity, stranding the
            // real administrator in someone else's account.
            if (context.User.FindFirst(AdminImpersonationClaims.OriginalAdminId) is not null) return Results.Forbid();

            var target = await userManager.FindByIdAsync(userId);
            if (target is null) return Results.NotFound();

            var adminId = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "unknown";
            var adminName = context.User.Identity?.Name ?? "unknown";
            if (target.Id == adminId) return Results.LocalRedirect("~/");

            clientContext.CaptureOnce(context);
            await auditLog.LogAsync(adminName, AuditAction.ImpersonationStart, nameof(ApplicationUser), target.Id,
                $"Administrator support session started: acting as {target.Email}");

            await signInManager.SignInWithClaimsAsync(target, isPersistent: false,
            [
                new Claim(AdminImpersonationClaims.OriginalAdminId, adminId),
                new Claim(AdminImpersonationClaims.OriginalAdminName, adminName),
            ]);

            return Results.LocalRedirect("~/");
        });

        group.MapPost("/ImpersonateEnd", async (
            HttpContext context,
            [FromServices] UserManager<ApplicationUser> userManager,
            [FromServices] SignInManager<ApplicationUser> signInManager,
            [FromServices] IAuditLogger auditLog,
            [FromServices] ClientContext clientContext) =>
        {
            // Who to go back to is carried on the session itself, so there is nothing to post and
            // nothing a caller could name that would take them somewhere they had not come from.
            var originalAdminId = context.User.FindFirst(AdminImpersonationClaims.OriginalAdminId)?.Value;
            if (originalAdminId is null) return Results.LocalRedirect("~/");

            var admin = await userManager.FindByIdAsync(originalAdminId);
            if (admin is null) return Results.LocalRedirect("~/");

            var impersonatedName = context.User.Identity?.Name;

            clientContext.CaptureOnce(context);
            await auditLog.LogAsync(admin.UserName ?? "unknown", AuditAction.ImpersonationEnd, nameof(ApplicationUser), originalAdminId,
                $"Ended support session acting as {impersonatedName}");

            await signInManager.SignInAsync(admin, isPersistent: false);

            return Results.LocalRedirect("~/");
        });

        return group;
    }
}
