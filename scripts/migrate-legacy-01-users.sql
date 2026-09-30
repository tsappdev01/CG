/*
    Legacy CG -> CGTOOL migration, step 1: users and everything a user needs
    before they can exist -- entities, departments, job titles -- plus the two
    things that point back at users once they do: each entity's approving
    authority, and the RP transaction positions.

    Run this FIRST. Every later step (declarations, RP transactions) hangs off a
    member, and a declaration whose member has not been migrated has nowhere to
    land.

    Source: the old application's [InsiderTrading] database, reached through
    synonyms in the [mig] schema -- see "Pointing at the source" below.
    Target:  CGTOOL's database, which is the one you run this file on.

    ------------------------------------------------------------------------
    HOW TO RUN

        -- 1. Deploy this file (creates the mig schema, synonyms, log and procs)
        -- 2. Look before you leap. @DryRun is the default:
        EXEC mig.usp_Migrate_Users;

        -- 3. Read what it says it would do, then:
        EXEC mig.usp_Migrate_Users @DryRun = 0;

        -- 4. Read the decisions it could not make for you:
        EXEC mig.usp_Migration_Report;

    @DryRun = 1 does the real work inside a transaction and rolls it back, so
    the counts are what would actually happen rather than an estimate of it.

    ------------------------------------------------------------------------
    POINTING AT THE SOURCE

    The procedures read synonyms (mig.src_ApplicationUsers and friends), not
    three-part names. Moving to a restored copy, a differently named database or
    a linked server is then a matter of recreating five synonyms rather than
    editing five hundred lines of SQL, and nothing silently keeps reading the
    old place. @SourceDatabase below sets what they are created against.

    ------------------------------------------------------------------------
    RE-RUNNABLE

    Every insert is guarded by NOT EXISTS on a natural key -- entity name,
    department name, job title name, member email. Running it twice inserts
    nothing the second time.

    It does NOT update rows that already exist. Once a member is in CGTOOL,
    CGTOOL is the system of record for them, and a re-run must not quietly
    overwrite an edit someone made there. The two UPDATE steps (approving
    authority, RP transaction role) only fill values that are still unset, for
    the same reason.

    ------------------------------------------------------------------------
    WHAT IS DELIBERATELY NOT MIGRATED

      - Passwords. [ApplicationUsers].[Password] is not read at all. CGTOOL
        authenticates through Entra ID SSO and ASP.NET Identity; importing the
        old credential store would create a second, weaker way in. A migrated
        member has no login until one is linked to them.
      - [IsAdmin]. A member and a login are different things here -- the
        administrator role lives on the Identity user, not on the member -- so
        mig.usp_Migration_Report lists who was an admin and a person grants it.
      - [Impersonator]. CGTOOL records impersonation as approved pairs (who may
        act for whom), which one boolean cannot express.
      - [Manager]. The legacy bit says a person IS a manager, not WHOSE. There is
        no reporting line in it to carry across.
      - [UserCompanies]. A second, many-to-many user-to-entity list alongside
        ApplicationUsers.CompanyId, which reads as a viewing scope rather than
        membership. CGTOOL gives a member one entity.
      - [user_logs], [ResetPassword], [UserManagement], [Report], [IsTest],
        [deleted_by]/[deleted_on]. Superseded by CGTOOL's own audit trail and
        role model, or filters rather than data.

    All of the above are reported by mig.usp_Migration_Report rather than
    guessed at.

    ------------------------------------------------------------------------
    ERROR NUMBERS (50100 block, to stay clear of stored-procedures.sql's 50001+)

      50101 - A source synonym does not resolve
      50102 - Target schema is not current
*/

SET NOCOUNT ON;
GO

/* ===========================================================================
   Guard: the right database, with a current schema.

   SQL Server resolves column names when a procedure is created, so deploying
   against a database whose schema is behind the application gives a run of
   "Invalid column name" errors while everything else deploys happily -- leaving
   a database that looks deployed and is missing the procedures that matter.
   Migrations first: start the application once against this database, or run
   `dotnet ef database update`, and then run this file.
   =========================================================================== */
IF OBJECT_ID('dbo.Members', 'U') IS NULL
    OR OBJECT_ID('dbo.Companies', 'U') IS NULL
    OR OBJECT_ID('dbo.Departments', 'U') IS NULL
    OR OBJECT_ID('dbo.JobTitles', 'U') IS NULL
    OR COL_LENGTH('dbo.Members', 'RpTransactionRole') IS NULL
    OR COL_LENGTH('dbo.Members', 'RelatedPartyTransactionAccess') IS NULL
    OR COL_LENGTH('dbo.Members', 'IsExecutiveManagement') IS NULL
    OR COL_LENGTH('dbo.Companies', 'ApprovingAuthorityMemberId') IS NULL
    OR COL_LENGTH('dbo.Companies', 'ApprovingAuthorityNotApplicable') IS NULL
BEGIN
    RAISERROR('This is not a current CGTOOL database. Run the EF migrations first (start the app once, or `dotnet ef database update`), then run this file again. Nothing has been applied.', 16, 1);
    SET NOEXEC ON;
END
GO

/* ===========================================================================
   The mig schema: everything this migration owns lives here, so dropping it
   drops the whole apparatus and leaves the application's own schema untouched.
   =========================================================================== */
IF SCHEMA_ID('mig') IS NULL EXEC('CREATE SCHEMA mig');
GO

/* ---------------------------------------------------------------------------
   Synonyms onto the source. Change @SourceDatabase and re-run this file to
   point somewhere else -- a restored copy, or a linked server, in which case
   use the four-part name.
   --------------------------------------------------------------------------- */
DECLARE @SourceDatabase sysname = N'InsiderTrading';

DECLARE @synonyms TABLE (LocalName sysname, SourceObject sysname);
INSERT INTO @synonyms (LocalName, SourceObject) VALUES
    (N'src_ApplicationUsers', N'ApplicationUsers'),
    (N'src_Companies',        N'Companies'),
    (N'src_Departments',      N'Departments'),
    (N'src_Approvers',        N'Approvers'),
    (N'src_UserCompanies',    N'UserCompanies');

DECLARE @local sysname, @source sysname, @sql nvarchar(max);
DECLARE synonym_cursor CURSOR LOCAL FAST_FORWARD FOR SELECT LocalName, SourceObject FROM @synonyms;
OPEN synonym_cursor;
FETCH NEXT FROM synonym_cursor INTO @local, @source;
WHILE @@FETCH_STATUS = 0
BEGIN
    SET @sql = N'IF OBJECT_ID(''mig.' + @local + N''', ''SN'') IS NOT NULL DROP SYNONYM mig.' + QUOTENAME(@local) + N';';
    EXEC sp_executesql @sql;

    SET @sql = N'CREATE SYNONYM mig.' + QUOTENAME(@local)
             + N' FOR ' + QUOTENAME(@SourceDatabase) + N'.[dbo].' + QUOTENAME(@source) + N';';
    EXEC sp_executesql @sql;

    FETCH NEXT FROM synonym_cursor INTO @local, @source;
END
CLOSE synonym_cursor;
DEALLOCATE synonym_cursor;
GO

/* ===========================================================================
   Run log. A migration that cannot say afterwards what it did is not something
   anyone should run against production data.
   =========================================================================== */
IF OBJECT_ID('mig.MigrationRun', 'U') IS NULL
CREATE TABLE mig.MigrationRun (
    RunId       int IDENTITY(1,1) NOT NULL CONSTRAINT PK_mig_MigrationRun PRIMARY KEY,
    StartedAt   datetime2(3) NOT NULL CONSTRAINT DF_mig_MigrationRun_StartedAt DEFAULT SYSUTCDATETIME(),
    FinishedAt  datetime2(3) NULL,
    RunBy       sysname NOT NULL CONSTRAINT DF_mig_MigrationRun_RunBy DEFAULT SUSER_SNAME(),
    Step        nvarchar(60) NOT NULL,
    DryRun      bit NOT NULL,
    Succeeded   bit NULL,
    ErrorMessage nvarchar(4000) NULL
);
GO

IF OBJECT_ID('mig.MigrationLog', 'U') IS NULL
CREATE TABLE mig.MigrationLog (
    LogId        int IDENTITY(1,1) NOT NULL CONSTRAINT PK_mig_MigrationLog PRIMARY KEY,
    RunId        int NOT NULL CONSTRAINT FK_mig_MigrationLog_Run REFERENCES mig.MigrationRun (RunId),
    Step         nvarchar(60) NOT NULL,
    RowsAffected int NOT NULL,
    Detail       nvarchar(400) NULL,
    LoggedAt     datetime2(3) NOT NULL CONSTRAINT DF_mig_MigrationLog_LoggedAt DEFAULT SYSUTCDATETIME()
);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_mig_MigrationLog_RunId' AND object_id = OBJECT_ID('mig.MigrationLog'))
CREATE INDEX IX_mig_MigrationLog_RunId ON mig.MigrationLog (RunId);
GO

/* ===========================================================================
   Step 1: Entities.  mig.src_Companies -> dbo.Companies

   ShortCode is required and unique in CGTOOL and has no source column, so it is
   derived: the initials of the name's words ("Dubai Investments PJSC" -> "DIP"),
   with a number appended where two entities would collide. Derived rather than
   left blank because the column is NOT NULL and the code is what the reports
   print; an operator can rename any of them afterwards on the Company screen,
   which is cheaper than being unable to insert at all.

   EntityType is left NULL. It decides which declaration wording a member gets,
   and the legacy schema has nothing to derive it from -- guessing would put a
   classification on an entity that nobody chose.

   [Section] is NOT carried into Sector. Its values are "MD & CEO", "CEO - RE",
   "GM - Masharie", "Chairman" -- who signs for the entity, not what industry it
   is in. It belongs with [ApproverId] and is handled in step 5.
   =========================================================================== */
CREATE OR ALTER PROCEDURE mig.usp_Migrate_Entities
    @RowsInserted int OUTPUT
AS
BEGIN
    SET NOCOUNT ON;

    ;WITH distinct_src AS (
        /* One row per name. The NOT EXISTS below tests the table as it was
           before the statement, so two legacy rows sharing a name would both
           pass it and create two entities. */
        SELECT c.Id,
               LTRIM(RTRIM(c.Name)) AS Name,
               ROW_NUMBER() OVER (PARTITION BY LTRIM(RTRIM(c.Name)) ORDER BY c.Id) AS rn
        FROM mig.src_Companies c
        WHERE c.Name IS NOT NULL AND LTRIM(RTRIM(c.Name)) <> ''
    ),
    src AS (SELECT Id, Name FROM distinct_src WHERE rn = 1),
    positions AS (
        SELECT s.Id, s.Name, n.n
        FROM src s
        CROSS APPLY (
            SELECT TOP (LEN(s.Name)) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) AS n
            FROM sys.all_objects
        ) n
    ),
    initials AS (
        SELECT p.Id, p.Name,
               STRING_AGG(CASE WHEN p.n = 1 OR SUBSTRING(p.Name, p.n - 1, 1) = ' '
                               THEN UPPER(SUBSTRING(p.Name, p.n, 1)) END, '')
                   WITHIN GROUP (ORDER BY p.n) AS Initials,
               STRING_AGG(UPPER(SUBSTRING(p.Name, p.n, 1)), '')
                   WITHIN GROUP (ORDER BY p.n) AS Compact
        FROM positions p
        WHERE SUBSTRING(p.Name, p.n, 1) LIKE '[A-Za-z0-9]'
        GROUP BY p.Id, p.Name
    ),
    coded AS (
        /* Initials only where there are enough of them to read as a code. A
           one-word name gives a one-letter code -- "Finance" would become "F" --
           unique but useless to a reader, so those fall back to the start of
           the name itself. */
        SELECT i.Id, i.Name,
               CASE WHEN LEN(i.Initials) >= 3 THEN LEFT(i.Initials, 16) ELSE LEFT(i.Compact, 6) END AS Code
        FROM initials i
    ),
    numbered AS (
        SELECT c.Id, c.Name, c.Code,
               ROW_NUMBER() OVER (PARTITION BY c.Code ORDER BY c.Id) AS dup
        FROM coded c
    ),
    final AS (
        SELECT LEFT(n.Name, 160) AS Name,
               CASE WHEN n.dup = 1 THEN n.Code ELSE LEFT(n.Code, 14) + CAST(n.dup AS varchar(4)) END AS ShortCode
        FROM numbered n
    )
    INSERT INTO dbo.Companies (Name, ShortCode, Active, ApprovingAuthorityNotApplicable, DelegateAuthorityNotApplicable)
    SELECT f.Name, f.ShortCode, 1, 0, 0
    FROM final f
    WHERE NOT EXISTS (SELECT 1 FROM dbo.Companies t WHERE t.Name = f.Name)
      AND NOT EXISTS (SELECT 1 FROM dbo.Companies t WHERE t.ShortCode = f.ShortCode);

    SET @RowsInserted = @@ROWCOUNT;
END
GO

/* ===========================================================================
   Step 2: Departments. Code is required and unique, derived the same way.
   =========================================================================== */
CREATE OR ALTER PROCEDURE mig.usp_Migrate_Departments
    @RowsInserted int OUTPUT
AS
BEGIN
    SET NOCOUNT ON;

    ;WITH distinct_src AS (
        SELECT d.Id,
               LTRIM(RTRIM(d.Name)) AS Name,
               ROW_NUMBER() OVER (PARTITION BY LTRIM(RTRIM(d.Name)) ORDER BY d.Id) AS rn
        FROM mig.src_Departments d
        WHERE d.Name IS NOT NULL AND LTRIM(RTRIM(d.Name)) <> ''
    ),
    src AS (SELECT Id, Name FROM distinct_src WHERE rn = 1),
    positions AS (
        SELECT s.Id, s.Name, n.n
        FROM src s
        CROSS APPLY (
            SELECT TOP (LEN(s.Name)) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) AS n
            FROM sys.all_objects
        ) n
    ),
    initials AS (
        SELECT p.Id, p.Name,
               STRING_AGG(CASE WHEN p.n = 1 OR SUBSTRING(p.Name, p.n - 1, 1) = ' '
                               THEN UPPER(SUBSTRING(p.Name, p.n, 1)) END, '')
                   WITHIN GROUP (ORDER BY p.n) AS Initials,
               STRING_AGG(UPPER(SUBSTRING(p.Name, p.n, 1)), '')
                   WITHIN GROUP (ORDER BY p.n) AS Compact
        FROM positions p
        WHERE SUBSTRING(p.Name, p.n, 1) LIKE '[A-Za-z0-9]'
        GROUP BY p.Id, p.Name
    ),
    coded AS (
        SELECT i.Id, i.Name,
               CASE WHEN LEN(i.Initials) >= 3 THEN LEFT(i.Initials, 16) ELSE LEFT(i.Compact, 6) END AS Code
        FROM initials i
    ),
    numbered AS (
        SELECT c.Id, c.Name, c.Code,
               ROW_NUMBER() OVER (PARTITION BY c.Code ORDER BY c.Id) AS dup
        FROM coded c
    ),
    final AS (
        SELECT LEFT(n.Name, 120) AS Name,
               CASE WHEN n.dup = 1 THEN n.Code ELSE LEFT(n.Code, 14) + CAST(n.dup AS varchar(4)) END AS Code
        FROM numbered n
    )
    INSERT INTO dbo.Departments (Code, Name, Active)
    SELECT f.Code, f.Name, 1
    FROM final f
    WHERE NOT EXISTS (SELECT 1 FROM dbo.Departments t WHERE t.Name = f.Name)
      AND NOT EXISTS (SELECT 1 FROM dbo.Departments t WHERE t.Code = f.Code);

    SET @RowsInserted = @@ROWCOUNT;
END
GO

/* ===========================================================================
   Step 3: Job titles.

   [dbo].[JobTitle] and [dbo].[UsersJobTitle] look like job-title lists and are
   not: their values are "CEO Office", "Corporate Affairs", "Legal", "Finance"
   -- offices and departments, the same vocabulary as [dbo].[Departments].
   Seeding the CGTOOL lookup from them would fill the Job Title dropdown with
   department names, so they are left alone and no synonym is made for them.

   [ApplicationUsers].[designation] is free text and does hold titles, so it is
   the source. [ApplicationUsers].[JobTitleID] points at [dbo].[JobTitleMaster],
   which is the real lookup -- that table's definition is not yet in hand, so
   the ids are reported unresolved rather than guessed at.
   =========================================================================== */
CREATE OR ALTER PROCEDURE mig.usp_Migrate_JobTitles
    @RowsInserted int OUTPUT
AS
BEGIN
    SET NOCOUNT ON;

    INSERT INTO dbo.JobTitles (Name, Active)
    SELECT DISTINCT LEFT(LTRIM(RTRIM(u.designation)), 120), 1
    FROM mig.src_ApplicationUsers u
    WHERE u.designation IS NOT NULL
      AND LTRIM(RTRIM(u.designation)) <> ''
      AND NOT EXISTS (SELECT 1 FROM dbo.JobTitles x
                      WHERE x.Name = LEFT(LTRIM(RTRIM(u.designation)), 120));

    SET @RowsInserted = @@ROWCOUNT;
END
GO

/* ===========================================================================
   Step 4: Users -> Members.

   Access flags map to CGTOOL's four by what the legacy Users Report called
   them:
       InsiderModule     -> InsiderTradingAccess          (Insider Declaration)
       RpModule          -> ConflictOfInterestAccess      (RP & COI Declaration)
       TransactionModule -> RelatedPartyTransactionAccess (RP Transaction)
   CGTOOL's fourth, RelatedPartyRegisterAccess, has no legacy counterpart and is
   set to 0. Granting it because a related flag was set would hand out access
   nobody granted.

   DeclarationType stays NULL: it is the capacity a person files in (Employee,
   Board Member, ...) and the legacy schema does not record it. The model allows
   NULL exactly so an unmigrated decision reads as "nobody has chosen" rather
   than as a wrong choice.

   A user with no email is still migrated -- their declarations have to hang off
   a member record -- but they cannot be matched to a login or notified, so the
   report lists them.
   =========================================================================== */
CREATE OR ALTER PROCEDURE mig.usp_Migrate_Members
    @IncludeTestUsers     bit = 0,
    @IncludeDeletedUsers  bit = 0,
    @IncludeInactiveUsers bit = 1,
    @RowsInserted int OUTPUT
AS
BEGIN
    SET NOCOUNT ON;

    INSERT INTO dbo.Members (
        CompanyId, FullName, JobTitle, DepartmentId, Email,
        InsiderTradingAccess, ConflictOfInterestAccess, RelatedPartyRegisterAccess, RelatedPartyTransactionAccess,
        IsExternalMember, IsBoardMember, IsExecutiveManagement, IsManualEntry,
        CanBeImpersonated, Active, RpTransactionRole, CreatedAtUtc, ModifiedAtUtc)
    SELECT
        tc.Id,
        LEFT(LTRIM(RTRIM(u.Name)), 120),
        LEFT(NULLIF(LTRIM(RTRIM(u.designation)), ''), 120),
        td.Id,
        LEFT(NULLIF(LTRIM(RTRIM(u.Email)), ''), 160),
        COALESCE(u.InsiderModule, 0),
        u.RpModule,
        0,
        u.TransactionModule,
        COALESCE(u.IsExternal, 0),
        0,
        0,
        /* Everything in the old application was entered by hand rather than
           synced from the directory, which is what IsManualEntry records. */
        1,
        COALESCE(u.Impersonate, 0),
        u.IsActive,
        0,
        COALESCE(u.CreatedOn, SYSUTCDATETIME()),
        COALESCE(u.ModifiedOn, u.CreatedOn, SYSUTCDATETIME())
    FROM (
        /* One row per email, the active and newest first: the legacy table has
           no unique index on it, and NOT EXISTS cannot see rows this same
           statement is inserting. A user with no email is keyed on their id so
           they are not collapsed together with every other blank. */
        SELECT *,
               ROW_NUMBER() OVER (
                   PARTITION BY CASE WHEN NULLIF(LTRIM(RTRIM(Email)), '') IS NULL
                                     THEN CAST(Id AS nvarchar(20)) ELSE LTRIM(RTRIM(Email)) END
                   ORDER BY IsActive DESC, Id DESC) AS rn
        FROM mig.src_ApplicationUsers
    ) u
    INNER JOIN mig.src_Companies sc ON sc.Id = u.CompanyId
    INNER JOIN dbo.Companies tc ON tc.Name = LEFT(LTRIM(RTRIM(sc.Name)), 160)
    LEFT JOIN mig.src_Departments sd ON sd.Id = u.DepartmentId
    LEFT JOIN dbo.Departments td ON td.Name = LEFT(LTRIM(RTRIM(sd.Name)), 120)
    WHERE u.rn = 1
      AND u.Name IS NOT NULL AND LTRIM(RTRIM(u.Name)) <> ''
      AND (@IncludeTestUsers = 1 OR u.IsTest = 0)
      AND (@IncludeDeletedUsers = 1 OR u.deleted_on IS NULL)
      AND (@IncludeInactiveUsers = 1 OR u.IsActive = 1)
      AND NOT EXISTS (
            /* Email is the natural key where there is one; where there is not,
               the name within the same entity, which is what the old
               application itself relied on. */
            SELECT 1 FROM dbo.Members m
            WHERE (u.Email IS NOT NULL AND LTRIM(RTRIM(u.Email)) <> '' AND m.Email = LTRIM(RTRIM(u.Email)))
               OR (m.FullName = LTRIM(RTRIM(u.Name)) AND m.CompanyId = tc.Id));

    SET @RowsInserted = @@ROWCOUNT;
END
GO

/* ===========================================================================
   Step 5: Approving authorities and RP transaction positions.

   Runs after the members exist, because both point at them.

   [Companies].[ApproverId] is a user id -- who signs for that entity -- which is
   exactly CGTOOL's Company.ApprovingAuthorityMemberId. [Companies].[Section] is
   that approver's office and has no column of its own here; the report shows it
   beside the approver this sets, so the two can be checked against each other.

   [dbo].[Approvers].[Role] holds the workflow positions: "CCAO", "CFO", "FD".
   The first two are CGTOOL's Ccao and Cfo. "FD" (Finance Director) has no
   member of the RpTransactionRole enum -- None, Ccao, Cfo, Coo, MdCeo -- so it
   is left as None and reported, rather than filed under whichever of Coo or
   MdCeo looks closest. The wrong position in an approval chain is not a
   rounding error.

   Both updates only fill values that are still unset: a choice made in CGTOOL
   outranks the legacy one.
   =========================================================================== */
CREATE OR ALTER PROCEDURE mig.usp_Migrate_ApproverRoles
    @AuthoritiesSet int OUTPUT,
    @RolesSet int OUTPUT
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE tc
    SET    ApprovingAuthorityMemberId = m.Id,
           ApprovingAuthorityNotApplicable = 0
    FROM   dbo.Companies tc
    JOIN   mig.src_Companies sc ON LEFT(LTRIM(RTRIM(sc.Name)), 160) = tc.Name
    JOIN   mig.src_ApplicationUsers au ON au.Id = sc.ApproverId
    JOIN   dbo.Members m
             ON (au.Email IS NOT NULL AND LTRIM(RTRIM(au.Email)) <> '' AND m.Email = LTRIM(RTRIM(au.Email)))
             OR m.FullName = LTRIM(RTRIM(au.Name))
    WHERE  tc.ApprovingAuthorityMemberId IS NULL;

    SET @AuthoritiesSet = @@ROWCOUNT;

    UPDATE m
    SET    RpTransactionRole = CASE UPPER(LTRIM(RTRIM(a.Role)))
                                   WHEN 'CCAO' THEN 1
                                   WHEN 'CFO'  THEN 2
                                   WHEN 'COO'  THEN 3
                               END
    FROM   dbo.Members m
    JOIN   mig.src_ApplicationUsers au
             ON (au.Email IS NOT NULL AND LTRIM(RTRIM(au.Email)) <> '' AND m.Email = LTRIM(RTRIM(au.Email)))
             OR m.FullName = LTRIM(RTRIM(au.Name))
    JOIN   mig.src_Approvers a ON a.UserId = au.Id
    WHERE  m.RpTransactionRole = 0
      AND  UPPER(LTRIM(RTRIM(a.Role))) IN ('CCAO', 'CFO', 'COO');

    SET @RolesSet = @@ROWCOUNT;
END
GO

/* ===========================================================================
   The entry point. Runs the five steps as one transaction, logs what each did,
   and finishes by showing the report -- because the counts alone do not tell
   anyone what they still have to decide.

   @DryRun = 1 (the default) does all of it and then rolls back. The counts are
   therefore what would really happen, not an estimate: the same statements, the
   same guards, the same joins. Row counts are collected in a table variable,
   which a rollback does not touch, so the log survives to be read afterwards.
   =========================================================================== */
CREATE OR ALTER PROCEDURE mig.usp_Migrate_Users
    @DryRun               bit = 1,
    @IncludeTestUsers     bit = 0,
    @IncludeDeletedUsers  bit = 0,
    @IncludeInactiveUsers bit = 1,
    @ShowReport           bit = 1,
    @RunId                int = NULL OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    /* Fail before touching anything if the source is not reachable. A synonym
       whose target is gone resolves to nothing until it is used, and the error
       then arrives halfway through a transaction. */
    IF OBJECT_ID('mig.src_ApplicationUsers', 'SN') IS NULL
        OR OBJECT_ID('mig.src_Companies', 'SN') IS NULL
        OR OBJECT_ID('mig.src_Departments', 'SN') IS NULL
        OR OBJECT_ID('mig.src_Approvers', 'SN') IS NULL
        OR OBJECT_ID('mig.src_UserCompanies', 'SN') IS NULL
        THROW 50101, 'The mig.src_* synonyms are missing. Re-run migrate-legacy-01-users.sql, setting @SourceDatabase to the legacy database.', 1;

    BEGIN TRY
        DECLARE @probe int;
        SELECT TOP 1 @probe = Id FROM mig.src_ApplicationUsers;
    END TRY
    BEGIN CATCH
        THROW 50101, 'The mig.src_* synonyms exist but do not resolve -- the legacy database is not reachable from this connection. Check the database name, or the linked server.', 1;
    END CATCH

    INSERT INTO mig.MigrationRun (Step, DryRun) VALUES (N'Users and reference data', @DryRun);
    SET @RunId = SCOPE_IDENTITY();

    /* A table variable, so the counts outlive the rollback a dry run ends with. */
    DECLARE @counts TABLE (Ordinal int, Step nvarchar(60), RowsAffected int, Detail nvarchar(400));

    DECLARE @entities int = 0, @departments int = 0, @jobTitles int = 0,
            @members int = 0, @authorities int = 0, @roles int = 0;

    BEGIN TRY
        BEGIN TRANSACTION;

        EXEC mig.usp_Migrate_Entities    @RowsInserted = @entities    OUTPUT;
        EXEC mig.usp_Migrate_Departments @RowsInserted = @departments OUTPUT;
        EXEC mig.usp_Migrate_JobTitles   @RowsInserted = @jobTitles   OUTPUT;

        EXEC mig.usp_Migrate_Members
             @IncludeTestUsers     = @IncludeTestUsers,
             @IncludeDeletedUsers  = @IncludeDeletedUsers,
             @IncludeInactiveUsers = @IncludeInactiveUsers,
             @RowsInserted         = @members OUTPUT;

        EXEC mig.usp_Migrate_ApproverRoles
             @AuthoritiesSet = @authorities OUTPUT,
             @RolesSet       = @roles       OUTPUT;

        INSERT INTO @counts (Ordinal, Step, RowsAffected, Detail) VALUES
            (1, N'Entities',               @entities,    N'dbo.Companies inserted'),
            (2, N'Departments',            @departments, N'dbo.Departments inserted'),
            (3, N'Job titles',             @jobTitles,   N'dbo.JobTitles inserted from [designation]'),
            (4, N'Members',                @members,     N'dbo.Members inserted'),
            (5, N'Approving authorities',  @authorities, N'dbo.Companies.ApprovingAuthorityMemberId set'),
            (6, N'RP transaction roles',   @roles,       N'dbo.Members.RpTransactionRole set');

        IF @DryRun = 1
        BEGIN
            ROLLBACK TRANSACTION;
            PRINT 'DRY RUN -- everything above was rolled back. Re-run with @DryRun = 0 to keep it.';
        END
        ELSE
        BEGIN
            COMMIT TRANSACTION;
            PRINT 'Committed.';
        END
    END TRY
    BEGIN CATCH
        IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;

        UPDATE mig.MigrationRun
        SET FinishedAt = SYSUTCDATETIME(), Succeeded = 0, ErrorMessage = ERROR_MESSAGE()
        WHERE RunId = @RunId;

        THROW;
    END CATCH

    INSERT INTO mig.MigrationLog (RunId, Step, RowsAffected, Detail)
    SELECT @RunId, Step, RowsAffected, Detail FROM @counts ORDER BY Ordinal;

    UPDATE mig.MigrationRun
    SET FinishedAt = SYSUTCDATETIME(), Succeeded = 1
    WHERE RunId = @RunId;

    SELECT Step, RowsAffected, Detail
    FROM @counts
    ORDER BY Ordinal;

    IF @ShowReport = 1 EXEC mig.usp_Migration_Report @RunId = @RunId;
END
GO

/* ===========================================================================
   The report. What landed, what did not, and what a person still has to decide.

   Run automatically at the end of mig.usp_Migrate_Users, and on its own
   afterwards -- it reads current state, so it is as true a week later as it was
   on the day.

   In a dry run the counts are of the rolled-back state, so the "did not
   migrate" list will name everybody. That is correct, and it is exactly what a
   dry run is for: it is the list you would be left with if you stopped now.
   =========================================================================== */
CREATE OR ALTER PROCEDURE mig.usp_Migration_Report
    @RunId int = NULL
AS
BEGIN
    SET NOCOUNT ON;

    SELECT N'1. What each side holds' AS Section;

    SELECT 'Legacy users (all)'          AS Measure, COUNT(*) AS Value FROM mig.src_ApplicationUsers
    UNION ALL SELECT 'Legacy users (test)',          COUNT(*) FROM mig.src_ApplicationUsers WHERE IsTest = 1
    UNION ALL SELECT 'Legacy users (soft-deleted)',  COUNT(*) FROM mig.src_ApplicationUsers WHERE deleted_on IS NOT NULL
    UNION ALL SELECT 'Legacy entities',              COUNT(*) FROM mig.src_Companies
    UNION ALL SELECT 'Legacy departments',           COUNT(*) FROM mig.src_Departments
    UNION ALL SELECT 'CGTOOL members',               COUNT(*) FROM dbo.Members
    UNION ALL SELECT 'CGTOOL members (active)',      COUNT(*) FROM dbo.Members WHERE Active = 1
    UNION ALL SELECT 'CGTOOL entities',              COUNT(*) FROM dbo.Companies
    UNION ALL SELECT 'CGTOOL departments',           COUNT(*) FROM dbo.Departments
    UNION ALL SELECT 'CGTOOL job titles',            COUNT(*) FROM dbo.JobTitles;

    SELECT N'2. Legacy users that did not migrate, and why' AS Section;

    SELECT u.Id, u.Name, u.Email, sc.Name AS Company,
           CASE
               WHEN u.Name IS NULL OR LTRIM(RTRIM(u.Name)) = '' THEN 'No name'
               WHEN sc.Id IS NULL  THEN 'Entity missing from the legacy Companies table'
               WHEN tc.Id IS NULL  THEN 'Entity not migrated -- check step 1'
               WHEN u.IsTest = 1   THEN 'Test user (excluded unless @IncludeTestUsers = 1)'
               WHEN u.deleted_on IS NOT NULL THEN 'Soft-deleted (excluded unless @IncludeDeletedUsers = 1)'
               WHEN u.IsActive = 0 THEN 'Inactive (excluded when @IncludeInactiveUsers = 0)'
               ELSE 'Duplicate of another row with the same email'
           END AS Reason
    FROM mig.src_ApplicationUsers u
    LEFT JOIN mig.src_Companies sc ON sc.Id = u.CompanyId
    LEFT JOIN dbo.Companies tc ON tc.Name = LEFT(LTRIM(RTRIM(sc.Name)), 160)
    WHERE NOT EXISTS (
        SELECT 1 FROM dbo.Members m
        WHERE (u.Email IS NOT NULL AND LTRIM(RTRIM(u.Email)) <> '' AND m.Email = LTRIM(RTRIM(u.Email)))
           OR (m.FullName = LTRIM(RTRIM(u.Name)) AND tc.Id IS NOT NULL AND m.CompanyId = tc.Id))
    ORDER BY Reason, u.Name;

    SELECT N'3. Administrators -- grant the CGTOOL role on the Identity login, not here' AS Section;

    SELECT u.Id, u.Name, u.Email
    FROM mig.src_ApplicationUsers u
    WHERE u.IsAdmin = 1 AND u.deleted_on IS NULL
    ORDER BY u.Name;

    SELECT N'4. Impersonators -- record as approved pairs on the Member screen' AS Section;

    SELECT u.Id, u.Name, u.Email
    FROM mig.src_ApplicationUsers u
    WHERE u.Impersonator = 1 AND u.deleted_on IS NULL
    ORDER BY u.Name;

    SELECT N'5. Managers -- CGTOOL records a reporting line, not a flag; set it per member' AS Section;

    SELECT u.Id, u.Name, u.Email
    FROM mig.src_ApplicationUsers u
    WHERE u.Manager = 1 AND u.deleted_on IS NULL
    ORDER BY u.Name;

    SELECT N'6. Approving authority as migrated, beside the legacy Section it should agree with' AS Section;

    SELECT tc.Name AS Entity, tc.ShortCode, sc.Section AS LegacySection,
           am.FullName AS ApprovingAuthority,
           CASE WHEN sc.ApproverId IS NOT NULL AND tc.ApprovingAuthorityMemberId IS NULL
                THEN 'Legacy approver did not match a member' ELSE '' END AS Note
    FROM dbo.Companies tc
    LEFT JOIN mig.src_Companies sc ON LEFT(LTRIM(RTRIM(sc.Name)), 160) = tc.Name
    LEFT JOIN dbo.Members am ON am.Id = tc.ApprovingAuthorityMemberId
    ORDER BY tc.Name;

    SELECT N'7. Approver roles with no CGTOOL equivalent -- left as None, decide the position' AS Section;

    SELECT a.Id, a.Level, a.Role, u.Name, u.Email
    FROM mig.src_Approvers a
    JOIN mig.src_ApplicationUsers u ON u.Id = a.UserId
    WHERE UPPER(LTRIM(RTRIM(a.Role))) NOT IN ('CCAO', 'CFO', 'COO')
    ORDER BY a.Level;

    SELECT N'8. Users linked to more than one entity -- CGTOOL gives a member one' AS Section;

    /* [ApplicationUsers].[CompanyId] is the entity a person belongs to, and that
       is what migrated. [UserCompanies] is a second, many-to-many list which
       reads as a viewing scope rather than membership. Nothing is invented for
       it; these are the people for whom that decision matters. */
    SELECT u.Id, u.Name, u.Email,
           home.Name AS HomeEntity,
           COUNT(*) AS ExtraEntityLinks,
           STRING_AGG(oc.Name, ', ') WITHIN GROUP (ORDER BY oc.Name) AS LinkedEntities
    FROM mig.src_UserCompanies uc
    JOIN mig.src_ApplicationUsers u ON u.Id = uc.UserId
    JOIN mig.src_Companies oc ON oc.Id = uc.CompanyId
    LEFT JOIN mig.src_Companies home ON home.Id = u.CompanyId
    WHERE u.deleted_on IS NULL AND uc.CompanyId <> u.CompanyId
    GROUP BY u.Id, u.Name, u.Email, home.Name
    ORDER BY COUNT(*) DESC, u.Name;

    SELECT N'9. Set before running a declaration cycle -- these pick the form and its wording' AS Section;

    SELECT 'Entities with no EntityType' AS Measure, COUNT(*) AS Value FROM dbo.Companies WHERE EntityType IS NULL
    UNION ALL SELECT 'Members with no DeclarationType',  COUNT(*) FROM dbo.Members WHERE DeclarationType IS NULL
    UNION ALL SELECT 'Members with no email',            COUNT(*) FROM dbo.Members WHERE Email IS NULL OR Email = ''
    UNION ALL SELECT 'Members with RelatedPartyRegisterAccess (legacy had no such flag)',
                                                         COUNT(*) FROM dbo.Members WHERE RelatedPartyRegisterAccess = 1
    UNION ALL SELECT 'Users with an unresolved JobTitleID (needs dbo.JobTitleMaster)',
                                                         COUNT(*) FROM mig.src_ApplicationUsers WHERE JobTitleID IS NOT NULL AND deleted_on IS NULL;

    SELECT N'10. This run' AS Section;

    SELECT r.RunId, r.StartedAt, r.FinishedAt, r.RunBy, r.DryRun, r.Succeeded,
           l.Step, l.RowsAffected, l.Detail
    FROM mig.MigrationRun r
    LEFT JOIN mig.MigrationLog l ON l.RunId = r.RunId
    WHERE r.RunId = COALESCE(@RunId, (SELECT MAX(RunId) FROM mig.MigrationRun))
    ORDER BY l.LogId;
END
GO

/*
    Clears the guard at the top. Reached normally on a successful deploy; on a
    guarded one this is the only batch after the guard that does anything.
*/
SET NOEXEC OFF;
GO
