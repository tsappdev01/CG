using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Microsoft.JSInterop;
using CGTOOL.Web.Data;
using CGTOOL.Web.Data.Governance;

namespace CGTOOL.Web.Components.Pages;

public partial class MyDeclarations
{
    private Member? _effectiveMember;
    private bool _isImpersonating;
    private DeclarationCycleRun? _insiderRun;
    private DeclarationCycleRun? _coiRun;
    private List<InsiderDeclaration>? _insiderHistory;
    private List<RelatedPartyCoiDeclaration>? _coiHistory;
    private InsiderDeclaration? _printDeclaration;
    private InsiderDeclaration? _confirmEditDeclaration;
    private RelatedPartyCoiDeclaration? _confirmEditCoiDeclaration;

    private async Task PrintAsync() => await JS.InvokeVoidAsync("print");

    private void ConfirmEdit()
    {
        if (_confirmEditDeclaration is null) return;
        Nav.NavigateTo($"/declarations/insider-trading/{_confirmEditDeclaration.Id}");
    }

    private void ConfirmEditCoi()
    {
        if (_confirmEditCoiDeclaration is null) return;
        Nav.NavigateTo($"/declarations/related-party-coi/{_confirmEditCoiDeclaration.Id}");
    }

    private static string RelationshipLabel(RelativeRelationship relationship) => relationship switch
    {
        RelativeRelationship.InLaws => "In-Laws",
        RelativeRelationship.FatherInLaw => "Father-in-Law",
        RelativeRelationship.MotherInLaw => "Mother-in-Law",
        _ => relationship.ToString(),
    };

    private bool Option1Enabled => _effectiveMember is { InsiderTradingAccess: true } && _insiderRun is not null;

    private bool Option2Available => _effectiveMember is not null
        && (_effectiveMember.ConflictOfInterestAccess || _effectiveMember.RelatedPartyRegisterAccess)
        && _coiRun is not null;

    private bool Option2Enabled => Option2Available && !Option1Enabled;

    protected override async Task OnInitializedAsync() => await LoadAsync();

    private async Task LoadAsync()
    {
        // Its own short-lived context rather than the circuit-scoped ApplicationDbContext:
        // sharing that one lets this race, or outlive, whatever else in the circuit is using
        // it -- which kills the circuit and takes the page with it.
        await using var db = await DbFactory.CreateDbContextAsync();

        _isImpersonating = Impersonation.ActingMemberId is not null;

        if (Impersonation.ActingMemberId is { } actingId)
        {
            _effectiveMember = await db.Members.FirstOrDefaultAsync(m => m.Id == actingId);
        }
        else
        {
            var state = await AuthState.GetAuthenticationStateAsync();
            var userId = state.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            _effectiveMember = userId is null ? null : await db.Members.FirstOrDefaultAsync(m => m.ApplicationUserId == userId);
        }

        if (_effectiveMember is null) return;

        if (_effectiveMember.InsiderTradingAccess)
        {
            // Drafts are still rows in InsiderDeclarations, so they must not count as "already
            // submitted" here -- otherwise a saved-but-not-submitted draft would make its run look
            // done and the "1. Submit Insider Declaration" option would wrongly disappear.
            var submittedInsiderRunIds = await db.InsiderDeclarations
                .AsNoTracking()
                .Where(d => d.MemberId == _effectiveMember.Id && !d.IsDraft)
                .Select(d => d.DeclarationCycleRunId)
                .ToListAsync();

            // ThenByDescending(Id): SentAtUtc alone isn't a unique/stable sort key (repeated Send Now/
            // reminders can tie), so without a deterministic tiebreaker this could disagree with the
            // identical query in InsiderDeclarationBanner.razor and show a different due date there
            // than here.
            _insiderRun = await db.DeclarationCycleRuns
                .AsNoTracking()
                .Where(r => r.Type == DeclarationCycleType.InsiderTrading
                    && (r.CompanyId == null || r.CompanyId == _effectiveMember.CompanyId)
                    && !r.Recalled
                    && !submittedInsiderRunIds.Contains(r.Id))
                .OrderByDescending(r => r.SentAtUtc)
                .ThenByDescending(r => r.Id)
                .FirstOrDefaultAsync();
        }

        if (_effectiveMember.ConflictOfInterestAccess || _effectiveMember.RelatedPartyRegisterAccess)
        {
            var submittedCoiRunIds = await db.RelatedPartyCoiDeclarations
                .AsNoTracking()
                .Where(d => d.MemberId == _effectiveMember.Id && !d.IsDraft)
                .Select(d => d.DeclarationCycleRunId)
                .ToListAsync();

            _coiRun = await db.DeclarationCycleRuns
                .AsNoTracking()
                .Where(r => r.Type == DeclarationCycleType.ConflictOfInterest
                    && (r.CompanyId == null || r.CompanyId == _effectiveMember.CompanyId)
                    && !r.Recalled
                    && !submittedCoiRunIds.Contains(r.Id))
                .OrderByDescending(r => r.SentAtUtc)
                .ThenByDescending(r => r.Id)
                .FirstOrDefaultAsync();
        }

        // AsNoTracking: without it, EF's identity map returns the pre-edit entity here after editing
        // a declaration on a different page within the same circuit-scoped DbContext (edits go
        // through a raw stored procedure, not EF's tracker), which is why print preview kept showing
        // the old values.
        _insiderHistory = await db.InsiderDeclarations
            .AsNoTracking()
            .Include(d => d.DeclarationCycleRun)
            .Include(d => d.Relatives)
            .Include(d => d.NinHolders)
            .Where(d => d.MemberId == _effectiveMember.Id)
            .OrderByDescending(d => d.SubmittedAtUtc)
            .ToListAsync();

        _coiHistory = await db.RelatedPartyCoiDeclarations
            .AsNoTracking()
            .Include(d => d.DeclarationCycleRun)
            .Where(d => d.MemberId == _effectiveMember.Id)
            .OrderByDescending(d => d.SubmittedAtUtc)
            .ToListAsync();
    }
}
