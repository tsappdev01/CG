/* ============================================================================
   Legacy CG → CGTOOL migration, step 1: entities, departments, job titles, users.

   Source: the old application's [InsiderTrading] database.
   Target: CGTOOL's [CGS] database.

   Run this FIRST. Every later step (declarations, RP transactions) references a
   member, and a declaration whose member has not been migrated has nowhere to
   land.

   PREREQUISITES
     - CGTOOL has been started once against [CGS], so DbBootstrapper has created
       the tables from the EF model. This script inserts rows; it does not create
       schema.
     - Both databases are reachable from the session running this. Where they are
       on different instances, either restore a copy of [InsiderTrading] beside
       [CGS] or set up a linked server and change @SourceDb below accordingly.
     - SQL Server 2017 or later (STRING_AGG ... WITHIN GROUP).

   RE-RUNNABLE. Every insert is guarded by NOT EXISTS on a natural key -- entity
   name, department name, job title name, member email -- so running it twice
   inserts nothing the second time. It does NOT update rows that already exist:
   once a member is in CGTOOL, CGTOOL is the system of record for them and a
   re-run must not quietly overwrite an edit made there.

   WHAT IS DELIBERATELY NOT MIGRATED
     - Passwords. [ApplicationUsers].[Password] is not carried across at all.
       CGTOOL authenticates through Entra ID SSO and ASP.NET Identity; importing
       the old credential store would create a second, weaker way in. Migrated
       members have no login until one is linked to them.
     - [IsAdmin]. A member and a login are different things in CGTOOL -- the
       administrator role lives on the Identity user, not on the member -- so
       this script REPORTS who was an admin (step 6) and leaves granting the role
       to a person.
     - [Impersonator]. CGTOOL records impersonation as approved pairs
       (dbo.MemberImpersonationApprovals: who may act for whom), which a single
       boolean cannot express. Step 6 reports these too.
     - [user_logs], [deleted_by]/[deleted_on], [ResetPassword], [UserManagement],
       [Report], [IsTest], [Manager]. Either superseded by CGTOOL's own audit
       trail and role model, or filters rather than data -- see @Include* below.
   ============================================================================ */

SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

/* ---------------------------------------------------------------------------
   Configuration. Change these, not the body of the script.
   --------------------------------------------------------------------------- */
/* These are read in step 4, which must therefore be in the same batch as them:
   GO ends a batch and takes its variables with it, so a configuration block
   separated from its use by a GO silently has no effect. Hence no GO between
   here and the COMMIT. */
DECLARE @IncludeTestUsers    bit = 0;  -- [IsTest] = 1 rows
DECLARE @IncludeDeletedUsers bit = 0;  -- rows with [deleted_on] set
DECLARE @IncludeInactiveUsers bit = 1; -- [IsActive] = 0 rows; migrated as Active = 0

/* A leaver is still migrated by default, and migrated as inactive. Their old
   declarations are evidence and have to hang off a member record; dropping the
   person would orphan every one of them. */

PRINT '--- Legacy CG -> CGTOOL, step 1: users and their reference data ---';

BEGIN TRANSACTION;

/* ===========================================================================
   Step 1: Entities  ([InsiderTrading].dbo.Companies -> [CGS].dbo.Companies)

   ShortCode is required and unique in CGTOOL and has no source column, so it is
   derived: the initials of the name's words ("Dubai Investments PJSC" -> "DIP"),
   uppercased, and suffixed with a number where two entities would collide.
   Derived rather than left blank because the column is NOT NULL and the code is
   what the reports print; an operator can rename any of them afterwards on the
   Company screen, which is cheaper than being unable to insert at all.

   EntityType is left NULL. It drives which declaration wording a member gets,
   and the legacy schema has nothing to derive it from -- guessing it would put a
   classification on an entity that nobody chose. Set it on the Company screen.

   [Section] is NOT carried into Sector. Its real values are "MD & CEO",
   "CEO - RE", "CEO - BM", "GM - Masharie", "Chairman" -- that is who signs for
   the entity, not what industry it is in, and it belongs with [ApproverId]
   rather than in a sector column. Both are handled in step 5, once the members
   those ids point at exist. The values are reported there so they can be
   checked rather than assumed.
   =========================================================================== */
PRINT 'Step 1: entities';

WITH distinct_src AS (
    /* One row per name. NOT EXISTS below tests the table as it was before the
       statement, so two legacy rows sharing a name would both pass it and
       create two entities. */
    SELECT c.Id, LTRIM(RTRIM(c.Name)) AS Name,
           ROW_NUMBER() OVER (PARTITION BY LTRIM(RTRIM(c.Name)) ORDER BY c.Id) AS rn
    FROM [InsiderTrading].[dbo].[Companies] c
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
           STRING_AGG(UPPER(SUBSTRING(p.Name, p.n, 1)), '') WITHIN GROUP (ORDER BY p.n) AS Compact
    FROM positions p
    WHERE SUBSTRING(p.Name, p.n, 1) LIKE '[A-Za-z0-9]'
    GROUP BY p.Id, p.Name
),
numbered AS (
    /* Initials only where there are enough of them to read as a code. A
       one-word name gives a one-letter code -- "Finance" would become "F" --
       which is unique but tells a reader nothing, so those fall back to the
       start of the name itself. */
    SELECT i.Id, i.Name,
           CASE WHEN LEN(i.Initials) >= 3 THEN LEFT(i.Initials, 16) ELSE LEFT(i.Compact, 6) END AS Code,
           ROW_NUMBER() OVER (
               PARTITION BY CASE WHEN LEN(i.Initials) >= 3 THEN LEFT(i.Initials, 16) ELSE LEFT(i.Compact, 6) END
               ORDER BY i.Id) AS dup
    FROM initials i
)
INSERT INTO [CGS].[dbo].[Companies] (Name, ShortCode, Active, ApprovingAuthorityNotApplicable, DelegateAuthorityNotApplicable)
SELECT n.Name,
       CASE WHEN n.dup = 1 THEN n.Code ELSE LEFT(n.Code, 14) + CAST(n.dup AS varchar(4)) END,
       1, 0, 0
FROM numbered n
WHERE NOT EXISTS (SELECT 1 FROM [CGS].[dbo].[Companies] t WHERE t.Name = n.Name)
  AND NOT EXISTS (
        SELECT 1 FROM [CGS].[dbo].[Companies] t
        WHERE t.ShortCode = CASE WHEN n.dup = 1 THEN n.Code ELSE LEFT(n.Code, 14) + CAST(n.dup AS varchar(4)) END);

PRINT '  entities inserted: ' + CAST(@@ROWCOUNT AS varchar(10));

/* ===========================================================================
   Step 2: Departments. Code is required and unique, and derived the same way.
   =========================================================================== */
PRINT 'Step 2: departments';

WITH distinct_src AS (
    /* One row per name -- see the note in step 1. */
    SELECT d.Id, LTRIM(RTRIM(d.Name)) AS Name,
           ROW_NUMBER() OVER (PARTITION BY LTRIM(RTRIM(d.Name)) ORDER BY d.Id) AS rn
    FROM [InsiderTrading].[dbo].[Departments] d
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
           STRING_AGG(UPPER(SUBSTRING(p.Name, p.n, 1)), '') WITHIN GROUP (ORDER BY p.n) AS Compact
    FROM positions p
    WHERE SUBSTRING(p.Name, p.n, 1) LIKE '[A-Za-z0-9]'
    GROUP BY p.Id, p.Name
),
numbered AS (
    /* Initials only where there are enough of them to read as a code. A
       one-word name gives a one-letter code -- "Finance" would become "F" --
       which is unique but tells a reader nothing, so those fall back to the
       start of the name itself. */
    SELECT i.Id, i.Name,
           CASE WHEN LEN(i.Initials) >= 3 THEN LEFT(i.Initials, 16) ELSE LEFT(i.Compact, 6) END AS Code,
           ROW_NUMBER() OVER (
               PARTITION BY CASE WHEN LEN(i.Initials) >= 3 THEN LEFT(i.Initials, 16) ELSE LEFT(i.Compact, 6) END
               ORDER BY i.Id) AS dup
    FROM initials i
)
INSERT INTO [CGS].[dbo].[Departments] (Code, Name, Active)
SELECT CASE WHEN n.dup = 1 THEN n.Code ELSE LEFT(n.Code, 14) + CAST(n.dup AS varchar(4)) END,
       n.Name, 1
FROM numbered n
WHERE NOT EXISTS (SELECT 1 FROM [CGS].[dbo].[Departments] t WHERE t.Name = n.Name)
  AND NOT EXISTS (
        SELECT 1 FROM [CGS].[dbo].[Departments] t
        WHERE t.Code = CASE WHEN n.dup = 1 THEN n.Code ELSE LEFT(n.Code, 14) + CAST(n.dup AS varchar(4)) END);

PRINT '  departments inserted: ' + CAST(@@ROWCOUNT AS varchar(10));

/* ===========================================================================
   Step 3: Job titles.

   The legacy schema holds job titles two ways: [UsersJobTitle] / [JobTitle],
   which are flat per-person lists keyed by email, and [ApplicationUsers].
   [designation], a free-text field on the user. CGTOOL has both a JobTitles
   lookup (for the dropdown) and Member.JobTitle (the title actually recorded
   against a person), so both sources are used: the distinct values become the
   lookup here, and step 4 stamps each member's own title.
   =========================================================================== */
PRINT 'Step 3: job titles';

/* [dbo].[JobTitle] and [dbo].[UsersJobTitle] look like job-title lists but are
   not: their values are "CEO Office", "Corporate Affairs", "Legal", "Finance"
   -- offices and departments, the same vocabulary as [dbo].[Departments]. Seeding
   the CGTOOL job-title lookup from them would fill the Job Title dropdown with
   department names, so they are left alone.

   [ApplicationUsers].[designation] is free text and does hold titles, so it is
   the source here. [ApplicationUsers].[JobTitleID] points at
   [dbo].[JobTitleMaster], which is the real lookup -- that table's definition is
   not yet in hand, so ids are reported unresolved in step 6 rather than guessed
   at. */
INSERT INTO [CGS].[dbo].[JobTitles] (Name, Active)
SELECT DISTINCT LEFT(LTRIM(RTRIM(u.designation)), 120), 1
FROM [InsiderTrading].[dbo].[ApplicationUsers] u
WHERE u.designation IS NOT NULL
  AND LTRIM(RTRIM(u.designation)) <> ''
  AND NOT EXISTS (SELECT 1 FROM [CGS].[dbo].[JobTitles] x
                  WHERE x.Name = LEFT(LTRIM(RTRIM(u.designation)), 120));

PRINT '  job titles inserted: ' + CAST(@@ROWCOUNT AS varchar(10));

/* ===========================================================================
   Step 4: Users -> Members.

   Access flags map to CGTOOL's four by what the legacy Users Report called
   them:
       InsiderModule     -> InsiderTradingAccess          (Insider Declaration)
       RpModule          -> ConflictOfInterestAccess      (RP & COI Declaration)
       TransactionModule -> RelatedPartyTransactionAccess (RP Transaction)
   CGTOOL's fourth flag, RelatedPartyRegisterAccess, has no legacy counterpart
   and is set to 0 -- granting it because a related flag was set would hand out
   access nobody granted. Step 6 reports the count so someone can decide.

   DeclarationType stays NULL: it is the capacity a person files in (Employee,
   Board Member, ...) and the legacy schema does not record it. The model allows
   NULL exactly so that an unmigrated decision reads as "nobody has chosen"
   rather than as a wrong choice.

   A user with no email is still migrated -- the name is what the declarations
   hang off -- but they cannot be matched to a login or notified, so step 6
   lists them.
   =========================================================================== */
PRINT 'Step 4: users -> members';

INSERT INTO [CGS].[dbo].[Members] (
    CompanyId, FullName, JobTitle, DepartmentId, Email,
    InsiderTradingAccess, ConflictOfInterestAccess, RelatedPartyRegisterAccess, RelatedPartyTransactionAccess,
    IsExternalMember, IsBoardMember, IsExecutiveManagement, IsManualEntry,
    CanBeImpersonated, Active, RpTransactionRole, CreatedAtUtc, ModifiedAtUtc)
SELECT
    tc.Id,
    LEFT(LTRIM(RTRIM(u.Name)), 160),
    LEFT(NULLIF(LTRIM(RTRIM(u.designation)), ''), 120),
    td.Id,
    LEFT(NULLIF(LTRIM(RTRIM(u.Email)), ''), 200),
    COALESCE(u.InsiderModule, 0),
    u.RpModule,
    0,
    u.TransactionModule,
    COALESCE(u.IsExternal, 0),
    0,
    0,
    /* Everything here was entered by hand in the old application rather than
       synced from the directory, which is what IsManualEntry records. */
    1,
    COALESCE(u.Impersonate, 0),
    u.IsActive,
    0,
    COALESCE(u.CreatedOn, SYSUTCDATETIME()),
    COALESCE(u.ModifiedOn, u.CreatedOn, SYSUTCDATETIME())
FROM (
    /* One row per email, newest first: the legacy table has no unique index on
       it, and NOT EXISTS cannot see rows this same statement is inserting. */
    SELECT *, ROW_NUMBER() OVER (
                PARTITION BY CASE WHEN NULLIF(LTRIM(RTRIM(Email)), '') IS NULL
                                  THEN CAST(Id AS nvarchar(20)) ELSE LTRIM(RTRIM(Email)) END
                ORDER BY IsActive DESC, Id DESC) AS rn
    FROM [InsiderTrading].[dbo].[ApplicationUsers]
) u
INNER JOIN [InsiderTrading].[dbo].[Companies] sc ON sc.Id = u.CompanyId
INNER JOIN [CGS].[dbo].[Companies] tc ON tc.Name = LTRIM(RTRIM(sc.Name))
LEFT JOIN [InsiderTrading].[dbo].[Departments] sd ON sd.Id = u.DepartmentId
LEFT JOIN [CGS].[dbo].[Departments] td ON td.Name = LTRIM(RTRIM(sd.Name))
WHERE u.rn = 1
  AND u.Name IS NOT NULL AND LTRIM(RTRIM(u.Name)) <> ''
  AND (@IncludeTestUsers = 1 OR u.IsTest = 0)
  AND (@IncludeDeletedUsers = 1 OR u.deleted_on IS NULL)
  AND (@IncludeInactiveUsers = 1 OR u.IsActive = 1)
  AND NOT EXISTS (
        /* Email is the natural key where there is one; where there is not, the
           name within the same entity, which is what the old application itself
           relied on. */
        SELECT 1 FROM [CGS].[dbo].[Members] m
        WHERE (u.Email IS NOT NULL AND LTRIM(RTRIM(u.Email)) <> '' AND m.Email = LTRIM(RTRIM(u.Email)))
           OR (m.FullName = LTRIM(RTRIM(u.Name)) AND m.CompanyId = tc.Id));

PRINT '  members inserted: ' + CAST(@@ROWCOUNT AS varchar(10));

/* ===========================================================================
   Step 5: Reporting managers.

   Done after the members exist, because it points members at each other: the
   legacy [Manager] bit says a person IS a manager, not WHOSE manager they are,
   so there is no reporting line to carry across. ReportingManagerId is left
   NULL and the managers are reported in step 6 -- inventing a line here would
   be worse than an empty one.
   =========================================================================== */
PRINT 'Step 5: approving authorities and RP transaction roles';

/* Done after the members exist, because both point at them.

   [Companies].[ApproverId] is a user id -- who signs for that entity -- which is
   exactly CGTOOL's Company.ApprovingAuthorityMemberId. [Companies].[Section]
   ("MD & CEO", "CEO - RE", "GM - Masharie") is that approver's office, and has
   no column of its own in CGTOOL; it is reported in step 6 so it can be checked
   against the approver this sets.

   Only entities that do not already have an approving authority are touched: a
   choice made in CGTOOL outranks the legacy one. */
UPDATE tc
SET    ApprovingAuthorityMemberId = m.Id,
       ApprovingAuthorityNotApplicable = 0
FROM   [CGS].[dbo].[Companies] tc
JOIN   [InsiderTrading].[dbo].[Companies] sc ON LTRIM(RTRIM(sc.Name)) = tc.Name
JOIN   [InsiderTrading].[dbo].[ApplicationUsers] au ON au.Id = sc.ApproverId
JOIN   [CGS].[dbo].[Members] m
         ON (au.Email IS NOT NULL AND LTRIM(RTRIM(au.Email)) <> '' AND m.Email = LTRIM(RTRIM(au.Email)))
         OR m.FullName = LTRIM(RTRIM(au.Name))
WHERE  tc.ApprovingAuthorityMemberId IS NULL;

PRINT '  approving authorities set: ' + CAST(@@ROWCOUNT AS varchar(10));

/* [dbo].[Approvers] holds the workflow positions: Role is "CCAO", "CFO" or
   "FD". The first two are CGTOOL's Ccao and Cfo. "FD" (Finance Director) has no
   member of the RpTransactionRole enum -- which is None, Ccao, Cfo, Coo, MdCeo
   -- so it is left as None and reported, rather than filed under whichever of
   Coo or MdCeo looks closest. Putting a person in the wrong position in an
   approval chain is not a rounding error. */
UPDATE m
SET    RpTransactionRole = CASE UPPER(LTRIM(RTRIM(a.Role)))
                               WHEN 'CCAO' THEN 1
                               WHEN 'CFO'  THEN 2
                               WHEN 'COO'  THEN 3
                               ELSE 0
                           END
FROM   [CGS].[dbo].[Members] m
JOIN   [InsiderTrading].[dbo].[ApplicationUsers] au
         ON (au.Email IS NOT NULL AND LTRIM(RTRIM(au.Email)) <> '' AND m.Email = LTRIM(RTRIM(au.Email)))
         OR m.FullName = LTRIM(RTRIM(au.Name))
JOIN   [InsiderTrading].[dbo].[Approvers] a ON a.UserId = au.Id
WHERE  m.RpTransactionRole = 0
  AND  UPPER(LTRIM(RTRIM(a.Role))) IN ('CCAO', 'CFO', 'COO');

PRINT '  RP transaction roles set: ' + CAST(@@ROWCOUNT AS varchar(10));

/* The legacy [Manager] bit says a person IS a manager, not WHOSE manager they
   are, so there is no reporting line to carry across. ReportingManagerId stays
   NULL and the managers are listed in step 6 -- inventing a line would be worse
   than an empty one. */

COMMIT TRANSACTION;
GO

/* ===========================================================================
   Step 6: What landed, and what a person still has to decide.
   =========================================================================== */
PRINT '';
PRINT '--- Counts ---';

SELECT 'Legacy users (all)'            AS Measure, COUNT(*) AS Value FROM [InsiderTrading].[dbo].[ApplicationUsers]
UNION ALL SELECT 'Legacy users (test)',           COUNT(*) FROM [InsiderTrading].[dbo].[ApplicationUsers] WHERE IsTest = 1
UNION ALL SELECT 'Legacy users (soft-deleted)',   COUNT(*) FROM [InsiderTrading].[dbo].[ApplicationUsers] WHERE deleted_on IS NOT NULL
UNION ALL SELECT 'Legacy entities',               COUNT(*) FROM [InsiderTrading].[dbo].[Companies]
UNION ALL SELECT 'Legacy departments',            COUNT(*) FROM [InsiderTrading].[dbo].[Departments]
UNION ALL SELECT 'CGTOOL members',                COUNT(*) FROM [CGS].[dbo].[Members]
UNION ALL SELECT 'CGTOOL members (active)',       COUNT(*) FROM [CGS].[dbo].[Members] WHERE Active = 1
UNION ALL SELECT 'CGTOOL entities',               COUNT(*) FROM [CGS].[dbo].[Companies]
UNION ALL SELECT 'CGTOOL departments',            COUNT(*) FROM [CGS].[dbo].[Departments]
UNION ALL SELECT 'CGTOOL job titles',             COUNT(*) FROM [CGS].[dbo].[JobTitles];

PRINT '';
PRINT '--- Legacy users that did NOT migrate, and why ---';

SELECT u.Id, u.Name, u.Email, sc.Name AS Company,
       CASE
           WHEN u.Name IS NULL OR LTRIM(RTRIM(u.Name)) = '' THEN 'No name'
           WHEN sc.Id IS NULL THEN 'Entity missing from legacy Companies'
           WHEN tc.Id IS NULL THEN 'Entity not migrated -- check step 1'
           WHEN u.IsTest = 1 THEN 'Test user (excluded by @IncludeTestUsers)'
           WHEN u.deleted_on IS NOT NULL THEN 'Soft-deleted (excluded by @IncludeDeletedUsers)'
           ELSE 'Already present in CGTOOL'
       END AS Reason
FROM [InsiderTrading].[dbo].[ApplicationUsers] u
LEFT JOIN [InsiderTrading].[dbo].[Companies] sc ON sc.Id = u.CompanyId
LEFT JOIN [CGS].[dbo].[Companies] tc ON tc.Name = LTRIM(RTRIM(sc.Name))
WHERE NOT EXISTS (
    SELECT 1 FROM [CGS].[dbo].[Members] m
    WHERE (u.Email IS NOT NULL AND LTRIM(RTRIM(u.Email)) <> '' AND m.Email = LTRIM(RTRIM(u.Email)))
       OR (m.FullName = LTRIM(RTRIM(u.Name)) AND tc.Id IS NOT NULL AND m.CompanyId = tc.Id))
ORDER BY Reason, u.Name;

PRINT '';
PRINT '--- Decisions left to a person ---';

PRINT 'Who was an administrator in the old application (grant the CGTOOL role on the Identity login, not here):';
SELECT u.Id, u.Name, u.Email FROM [InsiderTrading].[dbo].[ApplicationUsers] u
WHERE u.IsAdmin = 1 AND u.deleted_on IS NULL ORDER BY u.Name;

PRINT 'Who could impersonate others (record as approved pairs on the Member screen):';
SELECT u.Id, u.Name, u.Email FROM [InsiderTrading].[dbo].[ApplicationUsers] u
WHERE u.Impersonator = 1 AND u.deleted_on IS NULL ORDER BY u.Name;

PRINT 'Who was flagged as a manager (CGTOOL records a reporting line, not a flag -- set it per member):';
SELECT u.Id, u.Name, u.Email FROM [InsiderTrading].[dbo].[ApplicationUsers] u
WHERE u.Manager = 1 AND u.deleted_on IS NULL ORDER BY u.Name;

PRINT 'Migrated members with no email -- cannot be matched to a login or notified:';
SELECT m.Id, m.FullName, c.Name AS Company FROM [CGS].[dbo].[Members] m
LEFT JOIN [CGS].[dbo].[Companies] c ON c.Id = m.CompanyId
WHERE m.Email IS NULL OR m.Email = '' ORDER BY m.FullName;

PRINT 'Entities with no EntityType -- set it before running a declaration cycle, it picks the wording:';
SELECT c.Id, c.Name, c.ShortCode FROM [CGS].[dbo].[Companies] c WHERE c.EntityType IS NULL ORDER BY c.Name;

PRINT 'Members with no DeclarationType -- set it before running a declaration cycle, it picks the form:';
SELECT COUNT(*) AS MembersWithoutDeclarationType FROM [CGS].[dbo].[Members] WHERE DeclarationType IS NULL;

PRINT 'Nobody has RelatedPartyRegisterAccess -- the legacy schema had no such flag:';
SELECT COUNT(*) AS MembersWithRegisterAccess FROM [CGS].[dbo].[Members] WHERE RelatedPartyRegisterAccess = 1;

PRINT 'Approvers whose Role has no CGTOOL equivalent -- left as None, decide the position:';
SELECT a.Id, a.Level, a.Role, u.Name, u.Email
FROM [InsiderTrading].[dbo].[Approvers] a
JOIN [InsiderTrading].[dbo].[ApplicationUsers] u ON u.Id = a.UserId
WHERE UPPER(LTRIM(RTRIM(a.Role))) NOT IN ('CCAO', 'CFO', 'COO')
ORDER BY a.Level;

PRINT 'Entity approving authority as migrated, beside the legacy Section it should agree with:';
SELECT tc.Name AS Entity, tc.ShortCode, sc.Section AS LegacySection,
       am.FullName AS ApprovingAuthority,
       CASE WHEN sc.ApproverId IS NOT NULL AND tc.ApprovingAuthorityMemberId IS NULL
            THEN 'Legacy approver did not match a member' ELSE '' END AS Note
FROM [CGS].[dbo].[Companies] tc
LEFT JOIN [InsiderTrading].[dbo].[Companies] sc ON LTRIM(RTRIM(sc.Name)) = tc.Name
LEFT JOIN [CGS].[dbo].[Members] am ON am.Id = tc.ApprovingAuthorityMemberId
ORDER BY tc.Name;

PRINT 'Users with a JobTitleID -- resolve against dbo.JobTitleMaster once its definition is in hand:';
SELECT COUNT(*) AS UsersWithUnresolvedJobTitleId
FROM [InsiderTrading].[dbo].[ApplicationUsers] u
WHERE u.JobTitleID IS NOT NULL AND u.deleted_on IS NULL;
GO
