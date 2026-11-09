namespace CGTOOL.Web.Data.Governance;

/// <summary>What a brand-new member declares before anybody has said otherwise.
///
/// One definition, applied by every path that creates a member -- the user-list upload, the Entra
/// sync, and adding one by hand on User Management -- because the three ways of creating a person
/// had disagreed: the imports left every flag false, so an imported member had nothing to declare,
/// no onboarding to do, an empty My Workspace, and no row in any declaration cycle until an
/// administrator opened their record and ticked the boxes themselves.
///
/// Only creation. An existing row's flags are the governance process's to set, and an import that
/// re-applied these would undo every deliberate exception on every run.
/// </summary>
public static class MemberDefaults
{
    /// <summary>Ticks what everyone declares. Related Party Transactions is deliberately not among
    /// them: it is a place in an approval workflow -- raising a transaction for approval -- rather
    /// than something every member files, and granting it to all would put everyone in the chain.</summary>
    public static T ApplyToNew<T>(T member) where T : Member
    {
        ArgumentNullException.ThrowIfNull(member);

        member.InsiderTradingAccess = true;
        member.ConflictOfInterestAccess = true;
        member.RelatedPartyRegisterAccess = true;

        return member;
    }
}
