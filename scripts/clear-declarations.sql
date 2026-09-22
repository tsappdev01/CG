/*
    One-time cleanup script: clears all Declarations Setup / Insider Trading test data.

    Deletes, in FK-safe order:
      - InsiderDeclarationRelatives, InsiderDeclarations   (submitted declaration answers)
      - DeclarationCycleRunRecipients, DeclarationCycleRuns (Send Now / Schedule Send history)
      - DeclarationCycleSetups                              (per-type settings: reminders, email
                                                               template -- deleting lets each type's
                                                               page recreate a blank row on next visit
                                                               via usp_DeclarationCycleSetup_EnsureExists)

    Does NOT touch the older DeclarationSetup/DeclarationSubmission tables (the separate
    acknowledgement-style Conflict of Interest flow) -- only the Declarations Setup /
    DeclarationCycle* schema covered by this cleanup.

    Run this manually against the CGS database (e.g. via SSMS, Azure Data Studio, or
    `sqlcmd -S UATWEB01 -d CGS -i scripts/clear-declarations.sql`). Not part of the app's normal
    deployment -- this is destructive and one-off.
*/

SET NOCOUNT ON;
BEGIN TRANSACTION;

DELETE FROM dbo.InsiderDeclarationRelatives;
DELETE FROM dbo.InsiderDeclarations;
DELETE FROM dbo.DeclarationCycleRunRecipients;
DELETE FROM dbo.DeclarationCycleRuns;
DELETE FROM dbo.DeclarationCycleSetups;

-- Optional: reseed identities so new test rows start back at 1. Comment out to keep existing counters.
DBCC CHECKIDENT ('dbo.InsiderDeclarationRelatives', RESEED, 0);
DBCC CHECKIDENT ('dbo.InsiderDeclarations', RESEED, 0);
DBCC CHECKIDENT ('dbo.DeclarationCycleRunRecipients', RESEED, 0);
DBCC CHECKIDENT ('dbo.DeclarationCycleRuns', RESEED, 0);
DBCC CHECKIDENT ('dbo.DeclarationCycleSetups', RESEED, 0);

COMMIT TRANSACTION;

PRINT 'Cleared all Insider Trading declarations and Declarations Setup notification history/settings.';
