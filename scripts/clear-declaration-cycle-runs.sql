/*
    ============================================================================================
    CLEAR DECLARATION CYCLE RUNS/HISTORY ONLY -- deletes the Send Now / Schedule Send history
    rows shown in Declarations Setup > History (DeclarationCycleRuns + their
    DeclarationCycleRunRecipients), for ALL five declaration/notification types (Insider
    Trading, Conflict of Interest, Related Party Register, Blackout Periods, Scheduled
    Maintenance).

    Narrower than scripts/clear-declarations.sql and scripts/reset-declaration-data.sql: this
    does NOT touch any actual submitted declaration data, and does NOT touch
    DeclarationCycleSetup (the per-type reminder count/frequency/email template config).

    SAFETY: RelatedPartyCoiDeclarations has an ON DELETE CASCADE relationship to
    DeclarationCycleRuns (unlike InsiderDeclarations, which is ON DELETE RESTRICT and would
    simply block the delete). Deleting a run that already has a real RP & COI submission
    against it would silently cascade-delete that submission too -- which this script's whole
    point is to avoid. So this only deletes runs that have ZERO submissions on record (neither
    Insider Trading nor Related Party & COI); any run with at least one submission is left
    exactly as-is, submission and all.

    Consequence: once a run is cleared, any member who had (or has) that declaration due will
    need a fresh Send Now / Schedule Send from Declarations Setup before they can submit again
    -- there is no "due" cycle left to submit against until one is sent.

    THIS IS IRREVERSIBLE. There is no undo short of restoring a database backup taken before
    running this. Take a backup first if there is any chance you'll want this data back:
        BACKUP DATABASE CGS TO DISK = 'CGS_before_clear_cycle_runs.bak';

    Run manually against the CGS database (e.g. via SSMS, Azure Data Studio, or
    `sqlcmd -S UATWEB01 -d CGS -i scripts/clear-declaration-cycle-runs.sql`). Not part of the
    app's normal deployment -- this is destructive and one-off, and is NOT gated by any
    confirmation prompt of its own once you run it.
    ============================================================================================
*/

SET NOCOUNT ON;
BEGIN TRANSACTION;

DECLARE @ClearableRunIds TABLE (Id int PRIMARY KEY);

INSERT INTO @ClearableRunIds (Id)
SELECT r.Id
FROM dbo.DeclarationCycleRuns r
WHERE NOT EXISTS (SELECT 1 FROM dbo.InsiderDeclarations d WHERE d.DeclarationCycleRunId = r.Id)
  AND NOT EXISTS (SELECT 1 FROM dbo.RelatedPartyCoiDeclarations d WHERE d.DeclarationCycleRunId = r.Id);

DECLARE @ClearableCount int = (SELECT COUNT(*) FROM @ClearableRunIds);
DECLARE @SkippedCount int = (SELECT COUNT(*) FROM dbo.DeclarationCycleRuns) - @ClearableCount;

DELETE rec
FROM dbo.DeclarationCycleRunRecipients rec
JOIN @ClearableRunIds c ON c.Id = rec.DeclarationCycleRunId;

DELETE r
FROM dbo.DeclarationCycleRuns r
JOIN @ClearableRunIds c ON c.Id = r.Id;

COMMIT TRANSACTION;

PRINT CONCAT('Cleared ', @ClearableCount, ' declaration cycle run(s) with no submissions. ',
    @SkippedCount, ' run(s) were skipped because they already have at least one Insider Trading or Related Party & COI submission on record -- those runs and their submissions were left untouched.');
