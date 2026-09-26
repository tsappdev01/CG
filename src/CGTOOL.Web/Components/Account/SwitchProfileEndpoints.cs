using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using CGTOOL.Web.Data;
using CGTOOL.Web.Data.Governance;

namespace CGTOOL.Web.Components.Account;

/// <summary>Switching to a profile you are authorised to act for, and switching back.
///
/// Endpoints rather than circuit event handlers because the chosen profile is kept in a cookie so it
/// survives a refresh, and a cookie is a response header -- which cannot be written once a circuit's
/// response has been sent. The switch then lands as a fresh page load, which is also the honest thing
/// for a change of identity: nothing stale is left on screen under the new name.</summary>
internal static class SwitchProfileEndpoints
{
    public static IEndpointConventionBuilder MapSwitchProfileEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var group = endpoints.MapGroup("/Account").RequireAuthorization();

        group.MapPost("/SwitchProfile", async (
            HttpContext context,
            [FromForm] int memberId,
            [FromServices] IDbContextFactory<ApplicationDbContext> dbFactory,
            [FromServices] ActingAsCookie actingAs,
            [FromServices] IAuditLogger auditLog,
            [FromServices] ClientContext clientContext) =>
        {
            var userId = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (userId is null) return Results.Forbid();

            await using var db = await dbFactory.CreateDbContextAsync();

            var me = await db.Members.AsNoTracking().FirstOrDefaultAsync(m => m.ApplicationUserId == userId);
            if (me is null) return Results.Forbid();

            // The grant is checked here, not only where the list is drawn: a form post can name any
            // member id without ever opening the page that offers the choice.
            var granted = await db.MemberImpersonationApprovals
                .AnyAsync(a => a.ImpersonatorId == me.Id && a.MemberId == memberId);
            if (!granted) return Results.Forbid();

            var target = await db.Members.AsNoTracking().FirstOrDefaultAsync(m => m.Id == memberId && m.Active);
            if (target is null) return Results.Forbid();

            actingAs.Write(context, target.Id, target.FullName);

            clientContext.CaptureOnce(context);
            await auditLog.LogAsync(context.User.Identity?.Name ?? "unknown", AuditAction.ImpersonationStart,
                nameof(Member), target.Id.ToString(),
                $"Started acting on behalf of {target.FullName}", actingOnBehalfOf: target.FullName);

            return Results.LocalRedirect("~/my-declarations");
        });

        group.MapPost("/SwitchProfileEnd", async (
            HttpContext context,
            [FromServices] ActingAsCookie actingAs,
            [FromServices] IAuditLogger auditLog,
            [FromServices] ClientContext clientContext) =>
        {
            // Who is being acted as comes from the cookie, so there is nothing to post and nothing a
            // caller could name that would end a session other than their own.
            var current = actingAs.Read(context);
            actingAs.Clear(context);

            if (current is not null)
            {
                clientContext.CaptureOnce(context);
                await auditLog.LogAsync(context.User.Identity?.Name ?? "unknown", AuditAction.ImpersonationEnd,
                    nameof(Member), current.MemberId.ToString(),
                    $"Stopped acting on behalf of {current.MemberName}", actingOnBehalfOf: current.MemberName);
            }

            return Results.LocalRedirect("~/");
        });

        return group;
    }
}
