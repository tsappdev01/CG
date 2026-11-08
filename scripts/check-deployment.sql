-- Is the database in step with the deployed build?
--
-- Run this first whenever a page starts returning "An error occurred while processing your
-- request" straight after a deploy. Three things ship together and are applied separately:
--
--   1. the code          (the pipeline)
--   2. the migrations    (applied when the site is first loaded after a deploy)
--   3. the procedures    (scripts/stored-procedures.sql, run by hand)
--
-- A page that worked yesterday and 500s today is usually (3): the code calls a procedure with a
-- parameter the database's copy does not have, and SQL refuses the call.

SET NOCOUNT ON;

-- ---------------------------------------------------------------- 2. migrations

SELECT TOP 10 MigrationId, 'applied' AS State
FROM dbo.__EFMigrationsHistory
ORDER BY MigrationId DESC;

PRINT '--- the newest applied migration is at the top. Compare it with the newest file in';
PRINT '--- src/CGTOOL.Web/Data/Migrations. If the file is newer, load the site once and re-check.';
PRINT '';

-- ---------------------------------------------------------------- 3. procedures

-- Columns the current model expects, against what the table actually has.
SELECT 'MemberOnboardings column' AS Check_, name AS Found
FROM sys.columns
WHERE object_id = OBJECT_ID('dbo.MemberOnboardings') AND name LIKE '%BlobId%';

PRINT '--- expected: EmiratesIdBlobId and NinDocumentBlobId.';
PRINT '--- EmiratesIdFrontBlobId or EmiratesIdBackBlobId here means the migration has not run.';
PRINT '';

-- Parameters the procedures accept, against what the code passes.
SELECT
    OBJECT_NAME(p.object_id) AS Procedure_,
    p.name                   AS Parameter
FROM sys.parameters p
WHERE p.object_id IN (
        OBJECT_ID('dbo.usp_MemberOnboarding_Insert'),
        OBJECT_ID('dbo.usp_MemberOnboarding_Update'))
  AND p.name LIKE '%Blob%'
ORDER BY Procedure_, Parameter;

PRINT '--- expected: @EmiratesIdBlobId and @NinDocumentBlobId on both.';
PRINT '--- @EmiratesIdFrontBlobId or @EmiratesIdBackBlobId here, or nothing at all, means';
PRINT '--- scripts/stored-procedures.sql has not been re-run against this database.';
PRINT '';

-- Procedures the current build calls that the database does not have at all.
SELECT needed.Name AS MissingProcedure
FROM (VALUES
        ('usp_MemberOnboarding_Insert'),
        ('usp_MemberOnboarding_Update'),
        ('usp_MemberIdentityReference_Upsert'),
        ('usp_MemberIdentityReference_SetEmiratesIdDocument'),
        ('usp_DocumentBlob_Insert'),
        ('usp_DocumentBlob_Delete')
     ) AS needed(Name)
WHERE OBJECT_ID('dbo.' + needed.Name) IS NULL;

PRINT '--- anything listed above is missing entirely. Run scripts/stored-procedures.sql.';
