namespace CGTOOL.Web.Data.Governance;

/// <summary>The bit of the originating request worth keeping, carried from the static render into
/// the circuit. Its own type so the key and the shape are declared in one place.</summary>
public record ClientContextSnapshot(string? IpAddress, string? UserAgent)
{
    public const string PersistenceKey = "cgtool.client-context";
}

/// <summary>
/// Where the person is acting from, for the audit log.
///
/// This is harder than it looks in a Blazor Web App, and getting it wrong is silent. A component's
/// later interactions arrive over the SignalR circuit, not a fresh HTTP request, so there is no
/// HttpContext to read at the moment somebody clicks Save -- and the cascading HttpContext is only
/// supplied to statically rendered components in the first place.
///
/// Worse, the static render and the circuit are separate DI scopes. Capturing into this service
/// during the static pass therefore fills an instance that nothing interactive will ever see: the
/// AuditLogger resolves the circuit's instance, which stays empty. That is what used to happen, and
/// the only symptom was every audit row reading "not recorded".
///
/// So the value is captured during the static render and handed across the boundary through
/// PersistentComponentState (see MainLayout and ClientContextRestore), then restored into the
/// circuit's instance before anything is written.
/// </summary>
public class ClientContext
{
    public string? IpAddress { get; private set; }
    public string? UserAgent { get; private set; }

    private bool _set;

    /// <summary>Reads the originating request. Only meaningful during a static render, where the
    /// cascading HttpContext is supplied.</summary>
    public void CaptureOnce(HttpContext? httpContext)
    {
        if (_set || httpContext is null) return;

        var ip = httpContext.Connection.RemoteIpAddress?.ToString();
        var agent = httpContext.Request.Headers.UserAgent.ToString();

        Set(ip, string.IsNullOrWhiteSpace(agent) ? null : agent);
    }

    /// <summary>Takes the values captured during the static render into this circuit's instance.</summary>
    public void Restore(string? ipAddress, string? userAgent) => Set(ipAddress, userAgent);

    public ClientContextSnapshot Snapshot() => new(IpAddress, UserAgent);

    private void Set(string? ipAddress, string? userAgent)
    {
        if (_set) return;

        // A proxy reports its own address as ::1 or 127.0.0.1 unless forwarded headers are on.
        // Recorded as-is rather than guessed at: a wrong address in an audit log is worse than an
        // obviously local one, and the deployment's proxy configuration is the thing to fix.
        IpAddress = string.IsNullOrWhiteSpace(ipAddress) ? null : ipAddress;
        UserAgent = string.IsNullOrWhiteSpace(userAgent) ? null : userAgent;
        _set = IpAddress is not null || UserAgent is not null;
    }
}
