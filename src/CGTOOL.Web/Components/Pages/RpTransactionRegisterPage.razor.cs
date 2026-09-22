using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.JSInterop;
using CGTOOL.Web.Data;
using CGTOOL.Web.Data.Governance;

namespace CGTOOL.Web.Components.Pages;

public partial class RpTransactionRegisterPage
{
    private string _activeTab = "register";

    // Register tab -- Released to RP Register only (FRD §3.4.1), filterable by request/CCAO-approval/
    // release date.
    private List<RelatedPartyTransaction> _released = [];
    private string _dateFilterBasis = "request";
    private DateTime? _fromDate;
    private DateTime? _toDate;

    // Pending Approval tab -- Escalated only (FRD §3.4.2).
    private List<RelatedPartyTransaction> _escalated = [];

    protected override async Task OnInitializedAsync() => await LoadAsync();

    private async Task LoadAsync()
    {
        var query = Db.RelatedPartyTransactions
            .AsNoTracking()
            .Include(t => t.Member).ThenInclude(m => m!.Company)
            .Include(t => t.Company)
            .Include(t => t.ApproverMember)
            .Where(t => t.Status == RpTransactionStatus.ReleasedToRegister);

        if (_fromDate is not null || _toDate is not null)
        {
            query = _dateFilterBasis switch
            {
                "ccaoApproval" => query.Where(t =>
                    (_fromDate == null || t.CcaoActionAtUtc >= _fromDate) &&
                    (_toDate == null || t.CcaoActionAtUtc <= _toDate.Value.AddDays(1))),
                "release" => query.Where(t =>
                    (_fromDate == null || t.ReleasedAtUtc >= _fromDate) &&
                    (_toDate == null || t.ReleasedAtUtc <= _toDate.Value.AddDays(1))),
                _ => query.Where(t =>
                    (_fromDate == null || t.DateOfRequest >= _fromDate) &&
                    (_toDate == null || t.DateOfRequest <= _toDate.Value.AddDays(1))),
            };
        }

        _released = await query.OrderByDescending(t => t.ReleasedAtUtc).ToListAsync();

        _escalated = await Db.RelatedPartyTransactions
            .AsNoTracking()
            .Include(t => t.Member).ThenInclude(m => m!.Company)
            .Include(t => t.Company)
            .Include(t => t.ApproverMember)
            .Where(t => t.Status == RpTransactionStatus.Escalated)
            .OrderBy(t => t.DateOfRequest)
            .ToListAsync();
    }

    private void SwitchTab(string tab) => _activeTab = tab;

    private async Task ApplyFilterAsync() => await LoadAsync();

    private async Task ExportRegisterAsync()
    {
        var sb = new StringBuilder();
        sb.AppendLine("Reference,Entity,Requestor,Counter-party,Transaction Value,Description,Date of request,Approver Action,Approver Remarks,Approver Date,CCAO Action,CCAO Remarks,CCAO Date,Released Date");

        foreach (var t in _released)
        {
            sb.AppendLine(string.Join(',',
                RelatedPartyTransaction.DisplayReference(t.Id),
                Csv(t.Company?.Name),
                Csv(t.Member?.FullName),
                Csv(t.CounterPartyName),
                t.TransactionValue.ToString("F2"),
                Csv(t.Description),
                t.DateOfRequest.ToString("dd/MM/yyyy"),
                Csv(t.ApproverAction?.ToString()),
                Csv(t.ApproverRemarks),
                t.ApproverActionAtUtc?.ToString("dd/MM/yyyy") ?? string.Empty,
                Csv(t.CcaoAction?.ToString()),
                Csv(t.CcaoRemarks),
                t.CcaoActionAtUtc?.ToString("dd/MM/yyyy") ?? string.Empty,
                t.ReleasedAtUtc?.ToString("dd/MM/yyyy") ?? string.Empty));
        }

        var bytes = Encoding.UTF8.GetBytes(sb.ToString());
        await JS.InvokeVoidAsync("downloadFileFromBase64", $"rp-transaction-register-{DateTime.UtcNow:yyyyMMdd}.csv", "text/csv", Convert.ToBase64String(bytes));
    }

    private static string Csv(string? value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        return value.Contains(',') || value.Contains('"') || value.Contains('\n')
            ? $"\"{value.Replace("\"", "\"\"")}\""
            : value;
    }
}
