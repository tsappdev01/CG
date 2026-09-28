using Microsoft.AspNetCore.Components;
using Microsoft.EntityFrameworkCore;
using Microsoft.JSInterop;
using CGTOOL.Web.Data;
using CGTOOL.Web.Data.Governance;

namespace CGTOOL.Web.Components.Pages;

/// <summary>One transaction, its whole life on one page: the facts, the documents, and a history
/// anyone can read -- including someone who was not part of it, which is what an audit is.
///
/// Requestor, approver, CCAO and administrator all land here; only the action link differs, because
/// the decision itself is taken on the queue screen that owns it.</summary>
public partial class RpTransactionDetailPage
{
    [Parameter] public string Reference { get; set; } = string.Empty;

    private RelatedPartyTransaction? _transaction;
    private bool _loading = true;
    private bool _canAct;
    private string ActionLink => _transaction?.Status switch
    {
        RpTransactionStatus.AwaitingApproval => "/rp-transactions/approvals",
        RpTransactionStatus.Returned => "/rp-transactions/mine",
        _ => "/rp-transactions/ccao-review",
    };
    private string ActionLabel => _transaction?.Status switch
    {
        RpTransactionStatus.Returned => "Amend and resubmit",
        _ => "Take the decision",
    };

    private async Task PrintAsync() => await JS.InvokeVoidAsync("print");

    protected override async Task OnParametersSetAsync()
    {
        _loading = true;
        _transaction = null;

        // "RPT-00042" is how the reference is written everywhere; the id behind it is what the
        // query needs. A reference that isn't one is simply not found rather than an error page.
        if (!TryReadId(Reference, out var id))
        {
            _loading = false;
            return;
        }

        await using var db = await DbFactory.CreateDbContextAsync();
        var transaction = await db.RelatedPartyTransactions
            .AsNoTracking()
            .Include(t => t.Member).ThenInclude(m => m!.Company)
            .Include(t => t.Company)
            .Include(t => t.ApproverMember)
            .Include(t => t.Documents)
            .FirstOrDefaultAsync(t => t.Id == id);

        if (transaction is not null && await MayReadAsync(db, transaction))
        {
            _transaction = transaction;
            _canAct = await MayActAsync(db, transaction);
        }

        _loading = false;
    }

    /// <summary>RPT-00042 → 42. Anything else is not a reference.</summary>
    private static bool TryReadId(string reference, out int id)
    {
        id = 0;
        var digits = reference.AsSpan();
        var dash = digits.LastIndexOf('-');
        if (dash >= 0) digits = digits[(dash + 1)..];
        return int.TryParse(digits, out id) && id > 0;
    }

    /// <summary>Who may open a transaction: the person who raised it, its approver, anyone holding
    /// the CCAO position, and administrators. Anyone else gets the same answer as a reference that
    /// doesn't exist -- a page that says "you may not see this" still says the transaction exists.</summary>
    private async Task<bool> MayReadAsync(ApplicationDbContext db, RelatedPartyTransaction t)
    {
        var state = await AuthState.GetAuthenticationStateAsync();
        if (state.User.IsInRole(GovernanceRoles.Administrator)) return true;

        var member = await CurrentMemberAsync(db, state.User.Identity?.Name);
        if (member is null) return false;

        return member.Id == t.MemberId
               || member.Id == t.ApproverMemberId
               || member.RpTransactionRole == RpTransactionRole.Ccao;
    }

    /// <summary>Whether this viewer is the one the transaction is currently waiting on.</summary>
    private async Task<bool> MayActAsync(ApplicationDbContext db, RelatedPartyTransaction t)
    {
        var state = await AuthState.GetAuthenticationStateAsync();
        var member = await CurrentMemberAsync(db, state.User.Identity?.Name);
        if (member is null) return false;

        return t.Status switch
        {
            RpTransactionStatus.AwaitingApproval => member.Id == t.ApproverMemberId,
            RpTransactionStatus.Returned => member.Id == t.MemberId,
            RpTransactionStatus.Approved or RpTransactionStatus.Escalated => member.RpTransactionRole == RpTransactionRole.Ccao,
            _ => false,
        };
    }

    private async Task<Member?> CurrentMemberAsync(ApplicationDbContext db, string? userName)
    {
        // An administrator filing on someone's behalf reads the transaction as that person, the same
        // way every other screen in the app treats impersonation.
        if (Impersonation.ActingMemberId is { } acting)
        {
            return await db.Members.AsNoTracking().FirstOrDefaultAsync(m => m.Id == acting);
        }

        if (string.IsNullOrWhiteSpace(userName)) return null;

        return await db.Members
            .AsNoTracking()
            .FirstOrDefaultAsync(m => m.ApplicationUser!.UserName == userName);
    }
}
