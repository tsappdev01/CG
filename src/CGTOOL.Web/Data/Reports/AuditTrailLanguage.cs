using CGTOOL.Web.Data.Governance;

namespace CGTOOL.Web.Data.Reports;

/// <summary>Turns the audit log into words an ordinary reader understands.
///
/// The log stores what the code calls things: the action is an enum member, and the record type is
/// a C# class name -- "FamilyMember", "RelatedPartyCoiDeclaration", "NavMenuItemOrder". A compliance
/// reviewer signing off a quarter should not have to learn any of that, and a reviewer who cannot
/// read the log cannot review it, which makes the control a formality.
///
/// So this is the whole vocabulary in one place: what each action is called, what each record is
/// called, and which part of the system it belongs to. Its own class rather than private helpers on
/// the report, because the words are a decision about the product and are worth finding and
/// arguing with.</summary>
public static class AuditTrailLanguage
{
    /// <summary>The change, as a person would say it. Past tense, because the reader is looking at
    /// something that already happened.</summary>
    public static string Change(AuditAction action) => action switch
    {
        AuditAction.Create => "Added",
        AuditAction.Update => "Changed",
        AuditAction.Delete => "Removed",
        AuditAction.Deactivate => "Made inactive",
        AuditAction.Reactivate => "Made active again",
        AuditAction.RoleChange => "Changed access",
        AuditAction.ImpersonationStart => "Started acting for someone",
        AuditAction.ImpersonationEnd => "Stopped acting for someone",
        AuditAction.Notify => "Sent a notification",
        AuditAction.Recall => "Recalled a notification",
        AuditAction.AccessDenied => "Was refused entry",
        _ => action.ToString(),
    };

    /// <summary>What the record is, in the words the screens use. An unknown type is split on its
    /// capitals rather than printed raw, so a class added later reads as "Share Trading Upload"
    /// instead of "ShareTradingUpload" -- wrong, but readable, and obviously a gap to fill here.</summary>
    public static string Record(string? entityType) => entityType switch
    {
        null or "" => "—",

        "Member" => "User",
        "ApplicationUser" => "Login account",
        "Role" => "Access rights",
        "Company" => "Entity",
        "Department" => "Department",
        "JobTitle" => "Job title",

        "FamilyMember" => "Related party",
        "OwnedCompany" => "Related company",
        "MemberDocument" => "Document",
        "MemberImpersonationApproval" => "Permission to act for someone",

        "InsiderDeclaration" => "Insider declaration",
        "RelatedPartyCoiDeclaration" => "RP & COI declaration",
        "DeclarationCycleSetup" => "Declaration schedule",
        "DeclarationCycleRun" => "Declaration notification",
        "DeclarationReminderLog" => "Declaration reminder",

        "RelatedPartyTransaction" => "RP transaction",
        "DelegationOfAuthority" => "Approval limits",

        "ShareholderRegisterUpload" => "Share register upload",
        "ShareTradingUpload" => "Share dealing upload",
        "PolicyDocumentVersion" => "Policy document",

        "NavMenuItemOrder" or "NavMenuItemLabel" or "NavMenuItemVisibility" => "Menu setting",
        "AuditLog" or "AuditLogReview" => "Audit log review",
        "Dashboard" => "Dashboard",

        _ => Spaced(entityType),
    };

    /// <summary>Which part of the system a record belongs to, so the report can be filtered the way
    /// a reviewer thinks about it -- "show me everything about declarations" -- rather than by class
    /// name.</summary>
    public static string Area(string? entityType) => entityType switch
    {
        "Member" or "ApplicationUser" or "Role" or "MemberImpersonationApproval" => "People and access",
        "Company" or "Department" or "JobTitle" or "PolicyDocumentVersion" => "Reference data",
        "FamilyMember" or "OwnedCompany" or "MemberDocument" => "Related parties",
        "InsiderDeclaration" or "RelatedPartyCoiDeclaration" or "DeclarationCycleSetup"
            or "DeclarationCycleRun" or "DeclarationReminderLog" => "Declarations",
        "RelatedPartyTransaction" or "DelegationOfAuthority" => "RP transactions",
        "ShareholderRegisterUpload" or "ShareTradingUpload" => "Investor relations",
        "NavMenuItemOrder" or "NavMenuItemLabel" or "NavMenuItemVisibility"
            or "AuditLog" or "AuditLogReview" or "Dashboard" => "System settings",
        _ => "Other",
    };

    /// <summary>The areas a reader can filter by, in the order the menu presents them.</summary>
    public static readonly string[] Areas =
    [
        "People and access",
        "Related parties",
        "Declarations",
        "RP transactions",
        "Investor relations",
        "Reference data",
        "System settings",
        "Other",
    ];

    /// <summary>Who did it, naming the person they were acting for where that applies. A reviewer
    /// has to be able to see that someone filed on another person's behalf; it is one of the few
    /// things in the log that is interesting on its own.</summary>
    public static string Who(AuditLogEntry e) =>
        e.ActingOnBehalfOf is { Length: > 0 } behalf
            ? $"{e.ActorDisplayName} (for {behalf})"
            : e.ActorDisplayName;

    /// <summary>What happened, in a sentence. The log's own Details is already written for a reader
    /// -- "Added related party Musfirah Nayyar" -- so it is used as it stands where there is one,
    /// and a sentence is built from the action and the record where there is not.</summary>
    public static string What(AuditLogEntry e) =>
        e.Details is { Length: > 0 } details
            ? details.Trim()
            : $"{Change(e.Action)} {Record(e.EntityType).ToLowerInvariant()}" + (e.EntityId is { Length: > 0 } id ? $" #{id}" : "") + ".";

    /// <summary>"ShareTradingUpload" -> "Share Trading Upload".</summary>
    private static string Spaced(string pascal)
    {
        var chars = new List<char>(pascal.Length + 6);
        for (var i = 0; i < pascal.Length; i++)
        {
            if (i > 0 && char.IsUpper(pascal[i]) && !char.IsUpper(pascal[i - 1])) chars.Add(' ');
            chars.Add(pascal[i]);
        }
        return new string([.. chars]);
    }
}
