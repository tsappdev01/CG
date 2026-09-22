/*
    ============================================================================================
    DECLARATION DATA RESET -- deletes all Insider Trading and Related Party & COI declaration
    data (submissions, grid rows, uploaded-document records, and the send/schedule history that
    produced them), but PRESERVES:
      - Master data: Companies, Departments, Members, JobTitles
      - Login accounts: all ASP.NET Identity users/roles
      - Email config: DeclarationCycleSetup (reminder count/frequency, email subject/body per
        declaration type) and the legacy DeclarationSetup table (same role for the old,
        no-longer-administered Conflict of Interest flow)
      - Audit log: AuditLogEntries is left untouched, so the record of who did what survives
        the reset (if you also want the audit log cleared, do it separately/explicitly)
      - Everything unrelated to declarations: Transactions, ScheduledActivities/Dates,
        MemberNotifications, MemberImpersonationApprovals

    Cleared (children before parents, so this is FK-safe in any order the tables happen to
    exist in on the target database):
      - InsiderDeclarationNinHolders, InsiderDeclarationRelatives, InsiderDeclarations
      - CoiTradeLicenseDocuments, CoiCompanyEntries, CoiConflictEntries, CoiRelatives,
        RelatedPartyCoiDeclarations
      - DeclarationCycleRunRecipients, DeclarationCycleRuns (the "who was notified, which
        quarter, due date" history for BOTH Insider Trading and Conflict of Interest --
        DeclarationCycleSetup itself, the reusable per-type config those runs came from, is
        NOT touched)
      - DeclarationSubmissions (legacy Related Party & COI acknowledgment records from before
        this app's RP/COI flow was rebuilt on DeclarationCycleRun)

    Every DELETE/CHECKIDENT below is guarded with IF OBJECT_ID(...) IS NOT NULL, so this runs
    cleanly even if some of these tables don't exist yet on the target database (pending
    migrations) -- it just skips whatever isn't there.

    Consequence: clearing DeclarationCycleRuns means every member who currently has (or had) a
    declaration due will need a fresh Send Now / Schedule Send from Declarations Setup before
    they can submit again -- there is no "due" cycle left to submit against until one is sent.

    THIS IS IRREVERSIBLE. There is no undo short of restoring a database backup taken before
    running this. Take a backup first if there is any chance you'll want this data back:
        BACKUP DATABASE CGS TO DISK = 'CGS_before_declaration_reset.bak';

    Run manually against the CGS database (e.g. via SSMS, Azure Data Studio, or
    `sqlcmd -S UATWEB01 -d CGS -i scripts/reset-declaration-data.sql`). Not part of the app's
    normal deployment -- this is destructive and one-off, and is NOT gated by any confirmation
    prompt of its own once you run it.
    ============================================================================================
*/

SET NOCOUNT ON;
BEGIN TRANSACTION;

-- ---------- Insider Trading declarations (children first) ----------
IF OBJECT_ID(N'dbo.InsiderDeclarationRelatives', N'U') IS NOT NULL DELETE FROM dbo.InsiderDeclarationRelatives;
IF OBJECT_ID(N'dbo.InsiderDeclarationNinHolders', N'U') IS NOT NULL DELETE FROM dbo.InsiderDeclarationNinHolders;
IF OBJECT_ID(N'dbo.InsiderDeclarations', N'U') IS NOT NULL DELETE FROM dbo.InsiderDeclarations;

-- ---------- Related Party & COI declarations (current + legacy) ----------
IF OBJECT_ID(N'dbo.CoiTradeLicenseDocuments', N'U') IS NOT NULL DELETE FROM dbo.CoiTradeLicenseDocuments;
IF OBJECT_ID(N'dbo.CoiCompanyEntries', N'U') IS NOT NULL DELETE FROM dbo.CoiCompanyEntries;
IF OBJECT_ID(N'dbo.CoiConflictEntries', N'U') IS NOT NULL DELETE FROM dbo.CoiConflictEntries;
IF OBJECT_ID(N'dbo.CoiRelatives', N'U') IS NOT NULL DELETE FROM dbo.CoiRelatives;
IF OBJECT_ID(N'dbo.RelatedPartyCoiDeclarations', N'U') IS NOT NULL DELETE FROM dbo.RelatedPartyCoiDeclarations;
IF OBJECT_ID(N'dbo.DeclarationSubmissions', N'U') IS NOT NULL DELETE FROM dbo.DeclarationSubmissions;

-- ---------- Send/schedule history for both declaration types (config itself is kept) ----------
IF OBJECT_ID(N'dbo.DeclarationCycleRunRecipients', N'U') IS NOT NULL DELETE FROM dbo.DeclarationCycleRunRecipients;
IF OBJECT_ID(N'dbo.DeclarationCycleRuns', N'U') IS NOT NULL DELETE FROM dbo.DeclarationCycleRuns;

-- Reseed identity columns (int PKs) back to 1 for the next rows created after this reset.
IF OBJECT_ID(N'dbo.InsiderDeclarationRelatives', N'U') IS NOT NULL DBCC CHECKIDENT ('dbo.InsiderDeclarationRelatives', RESEED, 0);
IF OBJECT_ID(N'dbo.InsiderDeclarationNinHolders', N'U') IS NOT NULL DBCC CHECKIDENT ('dbo.InsiderDeclarationNinHolders', RESEED, 0);
IF OBJECT_ID(N'dbo.InsiderDeclarations', N'U') IS NOT NULL DBCC CHECKIDENT ('dbo.InsiderDeclarations', RESEED, 0);
IF OBJECT_ID(N'dbo.CoiTradeLicenseDocuments', N'U') IS NOT NULL DBCC CHECKIDENT ('dbo.CoiTradeLicenseDocuments', RESEED, 0);
IF OBJECT_ID(N'dbo.CoiCompanyEntries', N'U') IS NOT NULL DBCC CHECKIDENT ('dbo.CoiCompanyEntries', RESEED, 0);
IF OBJECT_ID(N'dbo.CoiConflictEntries', N'U') IS NOT NULL DBCC CHECKIDENT ('dbo.CoiConflictEntries', RESEED, 0);
IF OBJECT_ID(N'dbo.CoiRelatives', N'U') IS NOT NULL DBCC CHECKIDENT ('dbo.CoiRelatives', RESEED, 0);
IF OBJECT_ID(N'dbo.RelatedPartyCoiDeclarations', N'U') IS NOT NULL DBCC CHECKIDENT ('dbo.RelatedPartyCoiDeclarations', RESEED, 0);
IF OBJECT_ID(N'dbo.DeclarationSubmissions', N'U') IS NOT NULL DBCC CHECKIDENT ('dbo.DeclarationSubmissions', RESEED, 0);
IF OBJECT_ID(N'dbo.DeclarationCycleRunRecipients', N'U') IS NOT NULL DBCC CHECKIDENT ('dbo.DeclarationCycleRunRecipients', RESEED, 0);
IF OBJECT_ID(N'dbo.DeclarationCycleRuns', N'U') IS NOT NULL DBCC CHECKIDENT ('dbo.DeclarationCycleRuns', RESEED, 0);

COMMIT TRANSACTION;

PRINT 'Declaration data reset complete: Insider Trading and Related Party & COI declarations, their grid rows/documents, and send/schedule history have been deleted. Master data, login accounts, email config (DeclarationCycleSetup/DeclarationSetup), and the audit log were left untouched.';
