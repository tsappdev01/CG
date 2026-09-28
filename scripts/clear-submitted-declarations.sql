/*
    ============================================================================================
    CLEAR SUBMITTED DECLARATIONS ONLY -- so the same members can submit against the SAME open
    cycle again.

    Deletes every submitted and draft declaration:
      - InsiderDeclarationRelatives, InsiderDeclarationNinHolders, InsiderDeclarations
      - CoiRelatives, CoiCompanyEntries, CoiTradeLicenseDocuments, CoiConflictEntries,
        RelatedPartyCoiDeclarations

    Deliberately KEEPS:
      - DeclarationCycleRuns and DeclarationCycleRunRecipients -- the "Send Now" that made the
        declaration due. This is the difference between this script and
        scripts/clear-declarations.sql: delete the runs and there is no open cycle left to
        submit against, so nobody can resubmit until an administrator sends a new notification.
      - DeclarationCycleSetups -- the reminder schedule and email template.
      - My Workspace (FamilyMembers, OwnedCompanies, MemberDocuments) -- the member's own
        register, which is not part of any one submission.
      - AuditLogEntries -- the trail records that these declarations existed and were removed;
        erasing it would defeat the point of having one. The rows deleted here are visible in
        it, which is what makes this recoverable in the sense that matters.

    Does NOT touch the files on disk. A declaration's uploaded trade licences and the signature
    drawn on it live under wwwroot/uploads, and SQL cannot reach them:

        wwwroot/uploads/coi-trade-licenses
        wwwroot/uploads/insider-declarations
        wwwroot/uploads/declarations/coi          (signatures)

    After this runs those files are orphaned -- referenced by nothing. Leaving them is harmless
    and costs disk; clearing them is safe once this script has committed, since nothing points at
    them any more. Do not clear wwwroot/uploads/my-workspace: that is the member's own register,
    which this script keeps.

    Reminder counters on the kept runs are reset too (RemindersSent/LastReminderSentUtc), so the
    reminder sequence starts over rather than resuming mid-way through a cycle whose answers
    have just been removed.

    THIS IS IRREVERSIBLE short of restoring a backup. Run it against the CGS database, e.g.

        sqlcmd -S UATWEB01 -d CGS -i scripts/clear-submitted-declarations.sql

    or open it in SSMS with the right database selected.
    ============================================================================================
*/

SET NOCOUNT ON;
SET XACT_ABORT ON;

BEGIN TRANSACTION;

DECLARE @insider int = (SELECT COUNT(*) FROM dbo.InsiderDeclarations);
DECLARE @coi int = (SELECT COUNT(*) FROM dbo.RelatedPartyCoiDeclarations);

-- Children first: these are ON DELETE RESTRICT, so the parent delete fails while they exist.
DELETE FROM dbo.InsiderDeclarationRelatives;
DELETE FROM dbo.InsiderDeclarationNinHolders;
DELETE FROM dbo.InsiderDeclarations;

/*
    Order matters here, and it is not the obvious one. A company row in "Companies a relative owns
    >= 30%" carries CoiRelativeId, pointing at the relative who owns it, and that foreign key has no
    cascade -- so deleting CoiRelatives first fails the moment any such row exists, and XACT_ABORT
    takes the whole script down with it. The company rows go first, then the relatives they point at -- the order
    reset-all-data.sql and reset-declaration-data.sql already use.

    CoiTradeLicenseDocuments cascade from CoiCompanyEntries, so they are already gone by the time
    that line runs. It stays because a table listed here is one nobody has to remember.
*/
DELETE FROM dbo.CoiTradeLicenseDocuments;
DELETE FROM dbo.CoiCompanyEntries;
DELETE FROM dbo.CoiConflictEntries;
DELETE FROM dbo.CoiRelatives;
DELETE FROM dbo.RelatedPartyCoiDeclarations;

/*
    The runs stay, but their reminder counters go back to the start: a member who is about to
    declare again should get reminder 1 of 3, not reminder 3 of 3 for answers that no longer
    exist.
*/
UPDATE dbo.DeclarationCycleRuns
SET RemindersSent = 0,
    LastReminderSentUtc = NULL;

COMMIT TRANSACTION;

PRINT CONCAT('Deleted ', @insider, ' Insider Trading and ', @coi,
             ' Related Party & COI declaration(s). The open cycle(s) and their notifications were kept, ',
             'so the same members can submit again now.');
