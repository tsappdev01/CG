namespace CGTOOL.Web.Data.Governance;

/// <summary>
/// Scoped per circuit: captures the originating request's IP/user agent once (Blazor Server's
/// IHttpContextAccessor isn't reliable for interactions after the initial page render, since later
/// component events travel over the persistent SignalR connection, not a fresh HTTP request), so
/// AuditLogger can attribute every entry in this session to a real client IP.
/// </summary>
public class ClientContext
{
    public string? IpAddress { get; private set; }
    public string? UserAgent { get; private set; }
    private bool _captured;

    public void CaptureOnce(HttpContext? httpContext)
    {
        if (_captured || httpContext is null) return;
        IpAddress = httpContext.Connection.RemoteIpAddress?.ToString();
        UserAgent = httpContext.Request.Headers.UserAgent.ToString();
        _captured = true;
    }
}
