namespace CGTOOL.Web.Data.Governance;

/// <summary>Scoped per circuit: tracks which Member the current signed-in user is currently acting on behalf of.</summary>
public class ImpersonationContext
{
    public int? ActingMemberId { get; private set; }
    public string? ActingMemberName { get; private set; }

    public event Action? Changed;

    public void Start(int memberId, string memberName)
    {
        ActingMemberId = memberId;
        ActingMemberName = memberName;
        Changed?.Invoke();
    }

    public void End()
    {
        ActingMemberId = null;
        ActingMemberName = null;
        Changed?.Invoke();
    }
}
