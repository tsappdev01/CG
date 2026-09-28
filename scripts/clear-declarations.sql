/*
    ============================================================================================
    CLEAR DECLARATIONS AND THEIR NOTIFICATIONS -- every submitted and draft declaration, and the
    Send Now / Schedule Send history that made them due. The per-type configuration is KEPT.

    Deletes:
      - InsiderDeclarationRelatives, InsiderDeclarationNinHolders, InsiderDeclarations
      - CoiTradeLicenseDocuments, CoiCompanyEntries, CoiConflictEntries, CoiRelatives,
        RelatedPartyCoiDeclarations
      - DeclarationCycleRunRecipients, DeclarationCycleRuns -- the notification history behind
        Declaration Setup > History, for all five types

    Deliberately KEEPS:
      - DeclarationCycleSetups -- the reminder count, the day and time they go out on, and the
        email subject and body for each type. This is the configuration somebody wrote; it is not
        history, and re-typing five templates is not part of clearing test data.
      - My Workspace / My Register (FamilyMembers, FamilyMemberHoldings, OwnedCompanies,
        MemberDocuments) -- the member's own register, which is not part of any one submission.
      - Members, Companies, Departments, Job Titles and everything else about who people are.
      - AuditLogEntries -- the trail records that these declarations existed and were removed.
        Erasing it would defeat the point of having one.

    Consequence: with the runs gone there is no open cycle left, so nobody can submit anything
    until an administrator sends a fresh notification from Declaration Setup. If the point is to
    let the same people submit again right now, against the cycle that is already open, use
    scripts/clear-submitted-declarations.sql instead -- that one keeps the runs.

    Does NOT touch the files on disk. Uploaded trade licences and the signature drawn on a
    declaration live under wwwroot/uploads, and SQL cannot reach them:

        wwwroot/uploads/coi-trade-licenses
        wwwroot/uploads/insider-declarations
        wwwroot/uploads/declarations/coi          (signatures)

    After this runs those files are orphaned and safe to clear. Do not clear
    wwwroot/uploads/my-workspace: that is the member's own register, which this script keeps.

    THIS IS IRREVERSIBLE short of restoring a backup. Take one first if there is any chance you
    will want this back:

        BACKUP DATABASE CGS TO DISK = 'CGS_before_clear_declarations.bak';

    Run it against the CGS database, e.g.

        sqlcmd -S UATWEB01 -d CGS -i scripts/clear-declarations.sql

    or open it in SSMS with the right database selected.
    ============================================================================================
*/

SET NOCOUNT ON;
SET XACT_ABORT ON;

BEGIN TRANSACTION;

DECLARE @insider int = (SELECT COUNT(*) FROM dbo.InsiderDeclarations);
DECLARE @coi int = (SELECT COUNT(*) FROM dbo.RelatedPartyCoiDeclarations);
DECLARE @runs int = (SELECT COUNT(*) FROM dbo.DeclarationCycleRuns);

-- Children first: these are ON DELETE RESTRICT, so the parent delete fails while they exist.
DELETE FROM dbo.InsiderDeclarationRelatives;
DELETE FROM dbo.InsiderDeclarationNinHolders;
DELETE FROM dbo.InsiderDeclarations;

/*
    Order matters here, and it is not the obvious one. A company row in "Companies a relative owns
    >= 30%" carries CoiRelativeId, pointing at the relative who owns it, and that foreign key has no
    cascade -- so deleting CoiRelatives first fails the moment any such row exists, and XACT_ABORT
    takes the whole script down with it. The company rows go first, then the relatives they point at.

    CoiTradeLicenseDocuments cascade from CoiCompanyEntries, so they are already gone by the time
    that line runs. It stays because a table listed here is one nobody has to remember.
*/
DELETE FROM dbo.CoiTradeLicenseDocuments;
DELETE FROM dbo.CoiCompanyEntries;
DELETE FROM dbo.CoiConflictEntries;
DELETE FROM dbo.CoiRelatives;
DELETE FROM dbo.RelatedPartyCoiDeclarations;

/*
    The declarations are gone before the runs on purpose. RelatedPartyCoiDeclarations cascades
    from DeclarationCycleRuns, so deleting runs while submissions are still there would take those
    submissions with them silently -- the same result here, but arrived at without anyone seeing
    it. InsiderDeclarations would simply have blocked. Deleting the answers first means both are
    removed deliberately, and the counts printed below are honest.
*/
DELETE FROM dbo.DeclarationCycleRunRecipients;
DELETE FROM dbo.DeclarationCycleRuns;

COMMIT TRANSACTION;

PRINT CONCAT('Deleted ', @insider, ' Insider Trading declaration(s), ', @coi,
             ' Related Party & COI declaration(s) and ', @runs,
             ' notification run(s). The per-type configuration and email templates were kept, ',
             'so send a fresh notification from Declaration Setup when people should declare again.');
