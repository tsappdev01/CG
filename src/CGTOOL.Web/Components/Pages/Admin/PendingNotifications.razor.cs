using Microsoft.EntityFrameworkCore;
using Microsoft.JSInterop;
using CGTOOL.Web.Data;
using CGTOOL.Web.Data.Governance;

namespace CGTOOL.Web.Components.Pages.Admin;

public partial class PendingNotifications
{
    private List<MemberNotification>? _pending;
    private int? _sendingId;
    private string _sortColumn = "Queued";
    private bool _sortAscending = true;
    private HashSet<int> _selectedIds = [];
    private bool _bulkSending;
    private bool _showPrintPreview;
    private MemberNotification? _printSingleRecord;

    private async Task PrintAsync() => await JS.InvokeVoidAsync("print");

    private List<MemberNotification> PrintRows() => _printSingleRecord is not null ? [_printSingleRecord] : SortedPending();

    protected override async Task OnInitializedAsync() => await LoadAsync();

    private async Task LoadAsync()
    {
        _pending = await Db.MemberNotifications
            .Include(n => n.Member)
            .Where(n => n.Status == MemberNotificationStatus.Pending)
            .OrderBy(n => n.CreatedAtUtc)
            .ToListAsync();
    }

    private List<MemberNotification> SortedPending()
    {
        if (_pending is null) return [];
        IOrderedEnumerable<MemberNotification> sorted = _sortColumn switch
        {
            "User" => _pending.OrderBy(n => n.Member?.FullName),
            "Recipient" => _pending.OrderBy(n => n.Recipient),
            _ => _pending.OrderBy(n => n.CreatedAtUtc),
        };
        return (_sortAscending ? sorted : sorted.Reverse()).ToList();
    }

    private void Sort(string column)
    {
        if (_sortColumn == column) _sortAscending = !_sortAscending;
        else { _sortColumn = column; _sortAscending = true; }
    }

    private bool AllSelected => _pending is { Count: > 0 } && _pending.All(n => _selectedIds.Contains(n.Id));

    private void ToggleSelectAll(bool select)
    {
        if (select) _selectedIds = _pending!.Select(n => n.Id).ToHashSet();
        else _selectedIds.Clear();
    }

    private void ToggleSelect(int id, bool select)
    {
        if (select) _selectedIds.Add(id);
        else _selectedIds.Remove(id);
    }

    private void DeselectAll() => _selectedIds.Clear();

    private async Task SendSelectedAsync()
    {
        if (_pending is null) return;

        _bulkSending = true;
        var targets = _pending.Where(n => _selectedIds.Contains(n.Id)).ToList();
        var sentCount = 0;
        foreach (var notification in targets)
        {
            await SendNowAsync(notification);
            sentCount++;
        }

        _bulkSending = false;
        _selectedIds.Clear();
        Toasts.ShowSuccess($"Processed {sentCount} notification(s).");
    }

    private async Task SendNowAsync(MemberNotification notification)
    {
        if (notification.Member is null) return;

        _sendingId = notification.Id;
        bool sent;
        string? sendError = null;
        try
        {
            sent = await WelcomeEmailSender.SendWelcomeEmailAsync(notification.Member);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to send welcome email to {Recipient}", notification.Recipient);
            sent = false;
            sendError = ex.Message;
        }

        await NotificationWriter.SetStatusAsync(notification.Id, sent ? MemberNotificationStatus.Sent : MemberNotificationStatus.Failed, sent ? DateTime.UtcNow : null);

        var state = await AuthState.GetAuthenticationStateAsync();
        await AuditLog.LogAsync(
            state.User.Identity?.Name ?? "unknown",
            AuditAction.Notify,
            nameof(Member),
            notification.MemberId.ToString(),
            sent ? $"Notification email sent to {notification.Recipient}"
                 : $"Notification email to {notification.Recipient} could not be sent{(sendError is null ? "" : $": {sendError}")}");

        if (sent)
        {
            Toasts.ShowSuccess("Notification sent.");
        }
        else
        {
            Toasts.ShowError(sendError is null
                ? "Could not send — check SMTP configuration and try again."
                : $"Could not send: {sendError}");
        }
        _sendingId = null;
        await LoadAsync();
    }
}
