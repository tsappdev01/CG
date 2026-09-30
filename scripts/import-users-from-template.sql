/*
    Add users to CGTOOL from the filled-in Excel template.

    Template: scripts/templates/cgtool-user-import-template.xlsx
    Target:   CGTOOL's database, which is the one you run this on.

    Nothing here overlaps migrate-legacy-01-users.sql. That one moves the old
    application's users across in bulk; this one is for the rows a person types:
    joiners, a new entity's staff, the handful the migration could not match.
    Both write through the same guards, so running one after the other is safe
    in either order.

    ------------------------------------------------------------------------
    HOW TO RUN

      1. Deploy this file. It creates mig.UserImport and two procedures.

      2. Get the spreadsheet into mig.UserImport. Any of:

         a) SSMS: right-click the database > Tasks > Import Data, source
            Excel, destination mig.UserImport. Map by name; every column is
            text, so nothing is rejected at load.

         b) Save the Users sheet as CSV (UTF-8) and:

              BULK INSERT mig.UserImport
              FROM 'C:\path\to\users.csv'
              WITH (FORMAT = 'CSV', FIRSTROW = 6, CODEPAGE = '65001',
                    FIELDTERMINATOR = ',', ROWTERMINATOR = '0x0d0a',
                    TABLOCK);

            FIRSTROW = 6 skips the title block, the headings and the example
            row. Check that the example row did not come through before you
            commit anything.

         c) Send the file back and the INSERT statements get generated for you.

      3. Look before you leap. @DryRun is the default:

              EXEC mig.usp_Import_Users;

         It validates every row, tells you what it would insert and what it
         would reject and why, and rolls back.

      4. Fix the rejects in the spreadsheet, reload, and when it reads clean:

              EXEC mig.usp_Import_Users @DryRun = 0;

    ------------------------------------------------------------------------
    HOW IT DECIDES

    Everything is matched by NAME, not by id -- Entity and Department against
    the real tables -- so nobody filling the template has to look an id up. A
    name that does not match is a rejected row with the reason on it, never a
    silent NULL: a member in the wrong entity is worse than a member who was
    not created.

    Email is the key. A row whose email already belongs to a member is skipped
    rather than updated, for the same reason the migration does not update:
    once a person is in CGTOOL, CGTOOL is the system of record for them.

    Reporting managers are resolved in a second pass, after every row in the
    batch has been inserted -- so a manager who is in the same spreadsheet as
    the people reporting to them works, whichever order the rows are in.

    ------------------------------------------------------------------------
    NOT IN THE TEMPLATE, DELIBERATELY

      - Password. CGTOOL authenticates through Entra ID SSO; there is nowhere
        to put one and nowhere for it to go.
      - Administrator. That is a role on the Identity login, not on the person.
        Grant it in CGTOOL after the import.

    ERROR NUMBERS (50120 block)
      50121 - mig.UserImport is empty
*/

SET NOCOUNT ON;
GO

IF OBJECT_ID('dbo.Members', 'U') IS NULL
    OR COL_LENGTH('dbo.Members', 'RpTransactionRole') IS NULL
    OR COL_LENGTH('dbo.Members', 'IsExecutiveManagement') IS NULL
    OR COL_LENGTH('dbo.Members', 'CanBeImpersonated') IS NULL
BEGIN
    RAISERROR('This is not a current CGTOOL database. Run the EF migrations first, then run this file again. Nothing has been applied.', 16, 1);
    SET NOEXEC ON;
END
GO

IF SCHEMA_ID('mig') IS NULL EXEC('CREATE SCHEMA mig');
GO

/* ===========================================================================
   The staging table. Every column is nvarchar, matching the template's
   headings exactly: a spreadsheet arrives with "Yes", "yes", "Y", a stray
   space or an empty cell, and none of that should fail at load. It is
   interpreted in the procedure, where a bad value can be reported against the
   row it came from rather than killing the whole insert.
   =========================================================================== */
IF OBJECT_ID('mig.UserImport', 'U') IS NULL
CREATE TABLE mig.UserImport (
    RowId                           int IDENTITY(1,1) NOT NULL CONSTRAINT PK_mig_UserImport PRIMARY KEY,
    [Full Name]                     nvarchar(400) NULL,
    [Email]                         nvarchar(400) NULL,
    [Entity]                        nvarchar(400) NULL,
    [Department]                    nvarchar(400) NULL,
    [Job Title]                     nvarchar(400) NULL,
    [Declaration Type]              nvarchar(100) NULL,
    [Board Member]                  nvarchar(20)  NULL,
    [Executive Management]          nvarchar(20)  NULL,
    [External Member]               nvarchar(20)  NULL,
    [Active]                        nvarchar(20)  NULL,
    [RP & COI Declaration Access]   nvarchar(20)  NULL,
    [Insider Declaration Access]    nvarchar(20)  NULL,
    [Related Party Register Access] nvarchar(20)  NULL,
    [RP Transaction Access]         nvarchar(20)  NULL,
    [RP Transaction Role]           nvarchar(40)  NULL,
    [Reporting Manager Email]       nvarchar(400) NULL,
    [Can Be Impersonated]           nvarchar(20)  NULL,
    LoadedAt                        datetime2(3) NOT NULL CONSTRAINT DF_mig_UserImport_LoadedAt DEFAULT SYSUTCDATETIME()
);
GO

/* ---------------------------------------------------------------------------
   Yes/No, as a spreadsheet actually spells it. Blank falls back to the
   column's own default, which differs: Active defaults to Yes, every access
   flag defaults to No. A blank access cell must never read as "grant it".
   --------------------------------------------------------------------------- */
CREATE OR ALTER FUNCTION mig.fn_YesNo (@value nvarchar(20), @default bit)
RETURNS bit
AS
BEGIN
    DECLARE @v nvarchar(20) = UPPER(LTRIM(RTRIM(COALESCE(@value, ''))));
    RETURN CASE
        WHEN @v IN ('Y', 'YES', 'TRUE', '1')  THEN CAST(1 AS bit)
        WHEN @v IN ('N', 'NO', 'FALSE', '0')  THEN CAST(0 AS bit)
        ELSE @default
    END;
END
GO

/* ---------------------------------------------------------------------------
   A Yes/No cell that is neither, and not blank, is a typo worth reporting --
   "Ys" must not quietly become No.
   --------------------------------------------------------------------------- */
CREATE OR ALTER FUNCTION mig.fn_IsYesNoOrBlank (@value nvarchar(20))
RETURNS bit
AS
BEGIN
    DECLARE @v nvarchar(20) = UPPER(LTRIM(RTRIM(COALESCE(@value, ''))));
    RETURN CASE WHEN @v = '' OR @v IN ('Y','YES','TRUE','1','N','NO','FALSE','0')
                THEN CAST(1 AS bit) ELSE CAST(0 AS bit) END;
END
GO

/* ===========================================================================
   Validation, as a view, so the import and the report cannot disagree about
   what a good row is. Both read this; neither re-states the rules.

   Declaration Type accepts a hyphen where CGTOOL's label has an en dash. The
   labels really are "Employee – DI PJSC" with U+2013, and nobody types that:
   Excel's autocorrect gives it to you sometimes and not others, and a row
   rejected over an invisible character is the worst kind of rejection.
   =========================================================================== */
CREATE OR ALTER VIEW mig.vw_UserImportValidation
AS
SELECT
    i.RowId,
    LTRIM(RTRIM(i.[Full Name]))               AS FullName,
    LOWER(LTRIM(RTRIM(i.[Email])))            AS Email,
    LTRIM(RTRIM(i.[Entity]))                  AS EntityName,
    NULLIF(LTRIM(RTRIM(i.[Department])), '')  AS DepartmentName,
    NULLIF(LTRIM(RTRIM(i.[Job Title])), '')   AS JobTitle,
    LOWER(NULLIF(LTRIM(RTRIM(i.[Reporting Manager Email])), '')) AS ManagerEmail,
    c.Id  AS CompanyId,
    d.Id  AS DepartmentId,

    CASE REPLACE(NULLIF(LTRIM(RTRIM(i.[Declaration Type])), ''), N'-', N'–')
        WHEN N'Board Member'        THEN 0
        WHEN N'Employee – DI PJSC'  THEN 1
        WHEN N'Employee – Other'    THEN 2
        WHEN N'Corporate'           THEN 3
    END AS DeclarationType,

    CASE UPPER(REPLACE(LTRIM(RTRIM(COALESCE(i.[RP Transaction Role], ''))), ' ', ''))
        WHEN ''        THEN 0
        WHEN 'NONE'    THEN 0
        WHEN 'CCAO'    THEN 1
        WHEN 'CFO'     THEN 2
        WHEN 'COO'     THEN 3
        WHEN 'MD&CEO'  THEN 4
        WHEN 'MDCEO'   THEN 4
    END AS RpTransactionRole,

    mig.fn_YesNo(i.[Board Member], 0)                  AS IsBoardMember,
    mig.fn_YesNo(i.[Executive Management], 0)          AS IsExecutiveManagement,
    mig.fn_YesNo(i.[External Member], 0)               AS IsExternalMember,
    mig.fn_YesNo(i.[Active], 1)                        AS Active,
    mig.fn_YesNo(i.[RP & COI Declaration Access], 0)   AS ConflictOfInterestAccess,
    mig.fn_YesNo(i.[Insider Declaration Access], 0)    AS InsiderTradingAccess,
    mig.fn_YesNo(i.[Related Party Register Access], 0) AS RelatedPartyRegisterAccess,
    mig.fn_YesNo(i.[RP Transaction Access], 0)         AS RelatedPartyTransactionAccess,
    mig.fn_YesNo(i.[Can Be Impersonated], 0)           AS CanBeImpersonated,

    /* One reason per row, the most fundamental first: a row with no name and a
       bad entity should say "no name", because that is what you fix first. */
    CASE
        WHEN NULLIF(LTRIM(RTRIM(i.[Full Name])), '') IS NULL
            THEN 'Full Name is empty'
        WHEN NULLIF(LTRIM(RTRIM(i.[Email])), '') IS NULL
            THEN 'Email is empty'
        WHEN LTRIM(RTRIM(i.[Email])) NOT LIKE '%_@_%.__%' OR LTRIM(RTRIM(i.[Email])) LIKE '% %'
            THEN 'Email does not look like an address: ' + LTRIM(RTRIM(i.[Email]))
        WHEN NULLIF(LTRIM(RTRIM(i.[Entity])), '') IS NULL
            THEN 'Entity is empty'
        WHEN c.Id IS NULL
            THEN 'No entity in CGTOOL is called "' + LTRIM(RTRIM(i.[Entity])) + '" -- check the Lookups sheet'
        WHEN NULLIF(LTRIM(RTRIM(i.[Department])), '') IS NOT NULL AND d.Id IS NULL
            THEN 'No department in CGTOOL is called "' + LTRIM(RTRIM(i.[Department])) + '"'
        WHEN NULLIF(LTRIM(RTRIM(i.[Declaration Type])), '') IS NOT NULL
             AND REPLACE(LTRIM(RTRIM(i.[Declaration Type])), N'-', N'–')
                 NOT IN (N'Board Member', N'Employee – DI PJSC', N'Employee – Other', N'Corporate')
            THEN 'Declaration Type is not one of the four: ' + LTRIM(RTRIM(i.[Declaration Type]))
        WHEN UPPER(REPLACE(LTRIM(RTRIM(COALESCE(i.[RP Transaction Role], ''))), ' ', ''))
             NOT IN ('', 'NONE', 'CCAO', 'CFO', 'COO', 'MD&CEO', 'MDCEO')
            THEN 'RP Transaction Role is not one of the five: ' + LTRIM(RTRIM(i.[RP Transaction Role]))
        WHEN mig.fn_IsYesNoOrBlank(i.[Board Member]) = 0                  THEN 'Board Member must be Yes or No'
        WHEN mig.fn_IsYesNoOrBlank(i.[Executive Management]) = 0          THEN 'Executive Management must be Yes or No'
        WHEN mig.fn_IsYesNoOrBlank(i.[External Member]) = 0               THEN 'External Member must be Yes or No'
        WHEN mig.fn_IsYesNoOrBlank(i.[Active]) = 0                        THEN 'Active must be Yes or No'
        WHEN mig.fn_IsYesNoOrBlank(i.[RP & COI Declaration Access]) = 0   THEN 'RP & COI Declaration Access must be Yes or No'
        WHEN mig.fn_IsYesNoOrBlank(i.[Insider Declaration Access]) = 0    THEN 'Insider Declaration Access must be Yes or No'
        WHEN mig.fn_IsYesNoOrBlank(i.[Related Party Register Access]) = 0 THEN 'Related Party Register Access must be Yes or No'
        WHEN mig.fn_IsYesNoOrBlank(i.[RP Transaction Access]) = 0         THEN 'RP Transaction Access must be Yes or No'
        WHEN mig.fn_IsYesNoOrBlank(i.[Can Be Impersonated]) = 0           THEN 'Can Be Impersonated must be Yes or No'
        WHEN EXISTS (SELECT 1 FROM dbo.Members m WHERE m.Email = LOWER(LTRIM(RTRIM(i.[Email]))))
            THEN 'Already a member in CGTOOL -- skipped rather than updated'
        WHEN EXISTS (SELECT 1 FROM mig.UserImport j
                     WHERE LOWER(LTRIM(RTRIM(j.[Email]))) = LOWER(LTRIM(RTRIM(i.[Email])))
                       AND j.RowId < i.RowId)
            THEN 'Duplicate of an earlier row with the same email'
        ELSE NULL
    END AS Problem
FROM mig.UserImport i
LEFT JOIN dbo.Companies   c ON c.Name = LTRIM(RTRIM(i.[Entity]))
LEFT JOIN dbo.Departments d ON d.Name = LTRIM(RTRIM(i.[Department]))
/* A wholly blank row is what the bottom of a spreadsheet is made of, and is not
   an error worth reporting. */
WHERE NULLIF(LTRIM(RTRIM(COALESCE(i.[Full Name], ''))), '') IS NOT NULL
   OR NULLIF(LTRIM(RTRIM(COALESCE(i.[Email], ''))), '') IS NOT NULL;
GO

/* ===========================================================================
   The import.

   @DryRun = 1 (the default) runs the real inserts and rolls them back, so the
   counts are what would actually happen. The row-level results are collected
   in a table variable, which a rollback does not touch.
   =========================================================================== */
CREATE OR ALTER PROCEDURE mig.usp_Import_Users
    @DryRun bit = 1
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF NOT EXISTS (SELECT 1 FROM mig.UserImport)
        THROW 50121, 'mig.UserImport is empty. Load the filled-in template into it first -- see the notes at the top of import-users-from-template.sql.', 1;

    DECLARE @results TABLE (
        RowId int, FullName nvarchar(400), Email nvarchar(400),
        Outcome nvarchar(20), Detail nvarchar(600));

    DECLARE @inserted int = 0, @managersLinked int = 0;

    BEGIN TRY
        BEGIN TRANSACTION;

        INSERT INTO dbo.Members (
            CompanyId, FullName, JobTitle, DepartmentId, Email, DeclarationType,
            IsBoardMember, IsExecutiveManagement, IsExternalMember, IsManualEntry,
            Active, CanBeImpersonated, RpTransactionRole,
            InsiderTradingAccess, ConflictOfInterestAccess,
            RelatedPartyRegisterAccess, RelatedPartyTransactionAccess,
            CreatedAtUtc, ModifiedAtUtc)
        SELECT
            v.CompanyId,
            LEFT(v.FullName, 120),
            LEFT(v.JobTitle, 120),
            v.DepartmentId,
            LEFT(v.Email, 160),
            v.DeclarationType,
            v.IsBoardMember, v.IsExecutiveManagement, v.IsExternalMember,
            /* Typed in by a person rather than synced from the directory, which
               is exactly what IsManualEntry records. */
            1,
            v.Active, v.CanBeImpersonated, v.RpTransactionRole,
            v.InsiderTradingAccess, v.ConflictOfInterestAccess,
            v.RelatedPartyRegisterAccess, v.RelatedPartyTransactionAccess,
            SYSUTCDATETIME(), SYSUTCDATETIME()
        FROM mig.vw_UserImportValidation v
        WHERE v.Problem IS NULL;

        SET @inserted = @@ROWCOUNT;

        /* Reporting managers, once every row in the batch exists -- so a
           manager listed in the same spreadsheet as the people reporting to
           them works, whichever order the rows are in. Only members this batch
           created are touched, and only where the line is still unset. */
        UPDATE m
        SET    ReportingManagerId = mgr.Id
        FROM   dbo.Members m
        JOIN   mig.vw_UserImportValidation v ON v.Email = m.Email AND v.Problem IS NULL
        JOIN   dbo.Members mgr ON mgr.Email = v.ManagerEmail
        WHERE  v.ManagerEmail IS NOT NULL
          AND  m.ReportingManagerId IS NULL
          AND  mgr.Id <> m.Id;

        SET @managersLinked = @@ROWCOUNT;

        INSERT INTO @results (RowId, FullName, Email, Outcome, Detail)
        SELECT v.RowId, v.FullName, v.Email,
               CASE WHEN v.Problem IS NULL THEN 'Imported' ELSE 'Rejected' END,
               COALESCE(v.Problem,
                        CASE WHEN v.ManagerEmail IS NOT NULL
                                  AND NOT EXISTS (SELECT 1 FROM dbo.Members x WHERE x.Email = v.ManagerEmail)
                             THEN 'Imported, but no member has the manager email ' + v.ManagerEmail + ' -- reporting line left unset'
                             ELSE '' END)
        FROM mig.vw_UserImportValidation v;

        IF @DryRun = 1
        BEGIN
            ROLLBACK TRANSACTION;
            PRINT 'DRY RUN -- nothing was kept. Re-run with @DryRun = 0 once the rejects below are clear.';
        END
        ELSE
        BEGIN
            COMMIT TRANSACTION;
            PRINT 'Committed.';
        END
    END TRY
    BEGIN CATCH
        IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;
        THROW;
    END CATCH

    SELECT 'Rows in the sheet' AS Measure, COUNT(*) AS Value FROM mig.vw_UserImportValidation
    UNION ALL SELECT 'Would import' , @inserted
    UNION ALL SELECT 'Rejected'     , (SELECT COUNT(*) FROM mig.vw_UserImportValidation WHERE Problem IS NOT NULL)
    UNION ALL SELECT 'Reporting lines set', @managersLinked;

    SELECT RowId AS [Sheet row], FullName AS [Full Name], Email, Outcome, Detail
    FROM @results
    ORDER BY CASE Outcome WHEN 'Rejected' THEN 0 ELSE 1 END, RowId;
END
GO

/* ===========================================================================
   Check the sheet without touching anything. Safe on production, and the thing
   to hand back to whoever filled the template in.
   =========================================================================== */
CREATE OR ALTER PROCEDURE mig.usp_Import_Users_Check
AS
BEGIN
    SET NOCOUNT ON;

    SELECT N'1. What the sheet holds' AS Section;

    SELECT 'Rows to consider' AS Measure, COUNT(*) AS Value FROM mig.vw_UserImportValidation
    UNION ALL SELECT 'Good', COUNT(*) FROM mig.vw_UserImportValidation WHERE Problem IS NULL
    UNION ALL SELECT 'Rejected', COUNT(*) FROM mig.vw_UserImportValidation WHERE Problem IS NOT NULL;

    SELECT N'2. Rows that would be rejected, and why' AS Section;

    SELECT v.RowId AS [Sheet row], v.FullName AS [Full Name], v.Email, v.EntityName AS Entity, v.Problem
    FROM mig.vw_UserImportValidation v
    WHERE v.Problem IS NOT NULL
    ORDER BY v.RowId;

    SELECT N'3. Reporting managers that will not resolve' AS Section;

    SELECT v.RowId AS [Sheet row], v.FullName AS [Full Name], v.ManagerEmail AS [Manager email]
    FROM mig.vw_UserImportValidation v
    WHERE v.Problem IS NULL
      AND v.ManagerEmail IS NOT NULL
      AND NOT EXISTS (SELECT 1 FROM dbo.Members m WHERE m.Email = v.ManagerEmail)
      AND NOT EXISTS (SELECT 1 FROM mig.vw_UserImportValidation p WHERE p.Email = v.ManagerEmail AND p.Problem IS NULL)
    ORDER BY v.RowId;

    SELECT N'4. RP transaction positions this sheet would fill' AS Section;

    /* Each of these is normally held by one person. Two rows claiming CCAO is
       not rejected -- it is legitimate, and the workflow resolves the position
       to whoever holds it -- but it is nearly always a mistake worth seeing. */
    SELECT CASE v.RpTransactionRole WHEN 1 THEN 'CCAO' WHEN 2 THEN 'CFO'
                                    WHEN 3 THEN 'COO'  WHEN 4 THEN 'MD & CEO' END AS Position,
           COUNT(*) AS RowsInSheet,
           STRING_AGG(v.FullName, ', ') WITHIN GROUP (ORDER BY v.FullName) AS People,
           (SELECT COUNT(*) FROM dbo.Members m
            WHERE m.Active = 1 AND m.RpTransactionRole = v.RpTransactionRole) AS AlreadyHoldingItInCgtool
    FROM mig.vw_UserImportValidation v
    WHERE v.Problem IS NULL AND v.RpTransactionRole > 0
    GROUP BY v.RpTransactionRole;
END
GO

/* ===========================================================================
   Generate the Lookups sheet from the real database, so the template goes out
   with this system's entities and departments rather than the examples it
   ships with. Paste each column into the Lookups sheet.
   =========================================================================== */
CREATE OR ALTER PROCEDURE mig.usp_Import_Users_Lookups
AS
BEGIN
    SET NOCOUNT ON;

    SELECT Name AS [Entity] FROM dbo.Companies WHERE Active = 1 ORDER BY Name;
    SELECT Name AS [Department] FROM dbo.Departments WHERE Active = 1 ORDER BY Name;
    SELECT * FROM (VALUES (N'Board Member'), (N'Employee – DI PJSC'), (N'Employee – Other'), (N'Corporate')) AS t([Declaration Type]);
    SELECT * FROM (VALUES (N'None'), (N'CCAO'), (N'CFO'), (N'COO'), (N'MD & CEO')) AS t([RP Transaction Role]);
END
GO

/*
    Clears the guard at the top.
*/
SET NOEXEC OFF;
GO
