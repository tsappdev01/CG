namespace CGTOOL.Web.Data.Governance;

/// <summary>Which Member the signed-in user is currently acting on behalf of.
///
/// Scoped per circuit, and a circuit does not survive a browser refresh -- so the chosen profile is
/// also written to the tab's session storage and restored before anything reads it. Without that, a
/// refresh would quietly drop back to the user's own identity while they were part-way through
/// filing for somebody else, and the next thing they submitted would be filed under their own name.
/// Restoration is gated (see Restored) so no page can read this before it has happened.</summary>
public class ImpersonationContext
{
    public const string StorageKey = "cgtool.acting-as";

    public int? ActingMemberId { get; private set; }
    public string? ActingMemberName { get; private set; }

    /// <summary>False until session storage has been read back. The layout holds the page until then.</summary>
    public bool Restored { get; private set; }

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

    /// <summary>Called once per circuit with whatever session storage held, or nothing.</summary>
    public void MarkRestored(int? memberId, string? memberName)
    {
        if (memberId is { } id && !string.IsNullOrEmpty(memberName))
        {
            ActingMemberId = id;
            ActingMemberName = memberName;
        }

        Restored = true;
        Changed?.Invoke();
    }
}

/// <summary>What is kept in session storage between page loads.</summary>
public record ActingAsSnapshot(int MemberId, string MemberName);
