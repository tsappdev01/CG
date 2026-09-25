/*
    Creates sign-in accounts and their Member records from a list of people.

    Why this exists: the normal path is Sync from Entra ID on the User Management screen, which
    reads the whole tenant and creates everything below automatically. This script is for the
    cases that path cannot cover -- a tenant not yet connected, or people who are not in it -- so
    the accounts can be put in place by hand.

    Fill in @People below and run the whole file. It is re-runnable: a person already present is
    left exactly as they are, so it is safe to add rows to the list and run it again.

    What it creates, in this order, only where missing:
      1. the Company  (matched on Name)
      2. the Department (matched on Name)
      3. the Job Title  (matched on Name)
      4. the AspNetUsers login
      5. the AspNetUserRoles grant
      6. the Members record, linked to the login
      7. the reporting manager link, in a second pass once everyone exists

    Accounts are created with NO PASSWORD. Sign-in goes through Entra single sign-on, which finds
    the account by email address and links the external login on the first sign-in -- so the email
    here has to be the one Entra will present. There is deliberately nothing to sign in with if
    single sign-on is not configured.

    Run it against the application database:
        sqlcmd -S UATWEB01 -d CGS -i scripts/create-users.sql
*/

SET NOCOUNT ON;
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

/* ------------------------------------------------------------------ the people to create --- */

DECLARE @People TABLE (
    DisplayName   nvarchar(120) NOT NULL,
    WindowsUserId nvarchar(80)  NULL,
    Department    nvarchar(120) NULL,
    Title         nvarchar(120) NULL,
    Email         nvarchar(160) NOT NULL,
    CompanyName   nvarchar(160) NOT NULL,
    RoleName      nvarchar(80)  NOT NULL,  -- 'Administrator' or 'Normal User'
    ReportingTo   nvarchar(160) NULL       -- the manager's Email, or NULL / 'None'
);

INSERT INTO @People (DisplayName, WindowsUserId, Department, Title, Email, CompanyName, RoleName, ReportingTo)
VALUES
    (N'Senthil Kumar', N'senthil',   N'TS Technology & Operations', N'Manager - Operations',            N'senthil@techsource.ae.local',   N'TechSource', N'Normal User',   NULL),
    (N'Shafeeque',     N'shafeeque', N'TS Technology & Operations', N'System Administrator',            N'Shafeeque@techsource.ae.local', N'TechSource', N'Normal User',   N'senthil@techsource.ae.local'),
    (N'Nayyar Jawaid', N'nayyar',    N'TS Business Systems',        N'Manager - Business Applications', N'nayyar@techsource.ae.local',    N'TechSource', N'Administrator', NULL),
    (N'Lata Jagwani',  N'lata',      N'TS Business Systems',        N'Application Support',             N'lata@techsource.ae.local',      N'TechSource', N'Normal User',   N'nayyar@techsource.ae.local');

UPDATE @People SET ReportingTo = NULL WHERE ReportingTo IN (N'', N'None', N'none', N'N/A');

/* --------------------------------------------------------------------------- sanity check --- */

/*
    A role name that does not exist would otherwise produce a user who can sign in and see
    nothing, with no indication why. The roles are created by the application at startup.
*/
IF EXISTS (SELECT 1 FROM @People p WHERE NOT EXISTS (SELECT 1 FROM dbo.AspNetRoles r WHERE r.Name = p.RoleName))
BEGIN
    DECLARE @missingRoles nvarchar(max) =
        STUFF((SELECT DISTINCT N', ' + p.RoleName FROM @People p
               WHERE NOT EXISTS (SELECT 1 FROM dbo.AspNetRoles r WHERE r.Name = p.RoleName)
               FOR XML PATH('')), 1, 2, '');
    RAISERROR(
        'Not creating anyone: these role names do not exist in AspNetRoles: %s. Start the application once against this database (it creates the roles), or correct the RoleName values above.',
        16, 1, @missingRoles) WITH NOWAIT;
    RETURN;
END

/*
    Company.ShortCode and Department.Code are unique. The derivation below is good enough for
    names like "TechSource", but a collision with an unrelated existing row would surface as a
    constraint violation with nothing to act on -- so it is named here instead.
*/
IF EXISTS (
    SELECT 1 FROM @People p
    JOIN dbo.Companies c ON c.ShortCode = LEFT(UPPER(REPLACE(p.CompanyName, N' ', N'')), 20)
    WHERE c.Name <> p.CompanyName)
BEGIN
    RAISERROR('Not creating anyone: the short code derived from a CompanyName is already used by a different company. Add that company on the Company screen first, then re-run -- this script will match it by name.', 16, 1) WITH NOWAIT;
    RETURN;
END

IF EXISTS (
    SELECT 1 FROM @People p
    JOIN dbo.Departments d ON d.Code = LEFT(UPPER(REPLACE(p.Department, N' ', N'')), 20)
    WHERE d.Name <> p.Department)
BEGIN
    RAISERROR('Not creating anyone: the code derived from a Department name is already used by a different department. Add that department on the Departments screen first, then re-run -- this script will match it by name.', 16, 1) WITH NOWAIT;
    RETURN;
END

/* ------------------------------------------------------------------- reference data first --- */

INSERT INTO dbo.Companies (Name, ShortCode, Active)
SELECT DISTINCT p.CompanyName,
       -- A short code is required and must be unique; derive one and fall back to a suffix.
       LEFT(UPPER(REPLACE(p.CompanyName, N' ', N'')), 20),
       1
FROM @People p
WHERE NOT EXISTS (SELECT 1 FROM dbo.Companies c WHERE c.Name = p.CompanyName);

INSERT INTO dbo.Departments (Code, Name, Active)
SELECT DISTINCT LEFT(UPPER(REPLACE(p.Department, N' ', N'')), 20), p.Department, 1
FROM @People p
WHERE p.Department IS NOT NULL
  AND NOT EXISTS (SELECT 1 FROM dbo.Departments d WHERE d.Name = p.Department);

INSERT INTO dbo.JobTitles (Name, Active)
SELECT DISTINCT p.Title, 1
FROM @People p
WHERE p.Title IS NOT NULL
  AND NOT EXISTS (SELECT 1 FROM dbo.JobTitles j WHERE j.Name = p.Title);

/* ----------------------------------------------------------------------- the login account --- */

/*
    Identity looks accounts up by the NORMALIZED columns, which its default normalizer fills with
    the upper-cased value. Getting those wrong produces an account that exists but can never be
    found -- so they are written explicitly rather than left to match by luck.
*/
INSERT INTO dbo.AspNetUsers (
    Id, UserName, NormalizedUserName, Email, NormalizedEmail, EmailConfirmed,
    PasswordHash, SecurityStamp, ConcurrencyStamp, PhoneNumber, PhoneNumberConfirmed,
    TwoFactorEnabled, LockoutEnd, LockoutEnabled, AccessFailedCount,
    IsDefaultAdmin, ProfilePicturePath, LastSignInUtc)
SELECT
    LOWER(CONVERT(nvarchar(36), NEWID())),
    p.Email, UPPER(p.Email),
    p.Email, UPPER(p.Email),
    1,
    NULL,                                              -- single sign-on only; no password
    CONVERT(nvarchar(36), NEWID()),
    CONVERT(nvarchar(36), NEWID()),
    NULL, 0,
    0, NULL, 1, 0,
    0, NULL, NULL
FROM @People p
WHERE NOT EXISTS (SELECT 1 FROM dbo.AspNetUsers u WHERE u.NormalizedEmail = UPPER(p.Email));

/* ------------------------------------------------------------------------------ the roles --- */

INSERT INTO dbo.AspNetUserRoles (UserId, RoleId)
SELECT u.Id, r.Id
FROM @People p
JOIN dbo.AspNetUsers u ON u.NormalizedEmail = UPPER(p.Email)
JOIN dbo.AspNetRoles  r ON r.Name = p.RoleName
WHERE NOT EXISTS (SELECT 1 FROM dbo.AspNetUserRoles ur WHERE ur.UserId = u.Id AND ur.RoleId = r.Id);

/* ---------------------------------------------------------------------- the member record --- */

/*
    The Member is the governance-side record -- declarations, relatives and reports all hang off
    it. A login without one can sign in and has nothing to do, so the two are created together.
*/
INSERT INTO dbo.Members (
    CompanyId, FullName, JobTitle, DepartmentId, Email, ApplicationUserId,
    IsBoardMember, IsManualEntry, IsExternalMember, IsExecutiveManagement, Active,
    InsiderTradingAccess, ConflictOfInterestAccess, RelatedPartyRegisterAccess,
    RelatedPartyTransactionAccess, CanBeImpersonated, RpTransactionRole,
    CreatedAtUtc, ModifiedAtUtc)
SELECT
    c.Id, p.DisplayName, p.Title, d.Id, p.Email, u.Id,
    0,
    1,          -- entered by hand rather than loaded from the directory
    0, 0, 1,
    0, 0, 0, 0, 0,
    0,          -- RpTransactionRole.None
    SYSUTCDATETIME(), SYSUTCDATETIME()
FROM @People p
JOIN dbo.AspNetUsers u ON u.NormalizedEmail = UPPER(p.Email)
JOIN dbo.Companies   c ON c.Name = p.CompanyName
LEFT JOIN dbo.Departments d ON d.Name = p.Department
WHERE NOT EXISTS (SELECT 1 FROM dbo.Members m WHERE m.ApplicationUserId = u.Id);

/* ------------------------------------------------------------------- the reporting manager --- */

/*
    A separate pass, because a manager has to exist as a Member before anyone can point at them --
    and in a list like this the manager may well be two rows further down. Matching is on the
    manager's email, which is the only stable handle the sheet carries.

    A manager who is not in the list and not already in the database is reported rather than
    silently dropped, and the person is left with no reporting manager.
*/
UPDATE m
SET    m.ReportingManagerId = mgr.Id,
       m.ModifiedAtUtc      = SYSUTCDATETIME()
FROM   dbo.Members m
JOIN   @People     p   ON p.Email = m.Email
JOIN   dbo.Members mgr ON mgr.Email = p.ReportingTo
WHERE  p.ReportingTo IS NOT NULL
  AND  mgr.Id <> m.Id                      -- nobody reports to themselves
  AND  (m.ReportingManagerId IS NULL OR m.ReportingManagerId <> mgr.Id);

IF EXISTS (SELECT 1 FROM @People p WHERE p.ReportingTo IS NOT NULL
           AND NOT EXISTS (SELECT 1 FROM dbo.Members mgr WHERE mgr.Email = p.ReportingTo))
BEGIN
    DECLARE @missingManagers nvarchar(max) =
        STUFF((SELECT DISTINCT N', ' + p.ReportingTo FROM @People p
               WHERE p.ReportingTo IS NOT NULL
                 AND NOT EXISTS (SELECT 1 FROM dbo.Members mgr WHERE mgr.Email = p.ReportingTo)
               FOR XML PATH('')), 1, 2, '');
    RAISERROR('Everyone was created, but these reporting managers were not found and have been left unset: %s. Add them to the list and run this again.',
              10, 1, @missingManagers) WITH NOWAIT;
END

/* ---------------------------------------------------------------------------- what we did --- */

SELECT p.DisplayName,
       p.Email,
       p.RoleName,
       CASE WHEN u.Id IS NULL THEN 'NOT CREATED' ELSE 'login ok' END      AS LoginAccount,
       CASE WHEN m.Id IS NULL THEN 'NOT CREATED' ELSE 'member ok' END     AS MemberRecord,
       c.Name    AS Company,
       d.Name    AS Department,
       ISNULL(mgr.FullName, '-') AS ReportsTo
FROM @People p
LEFT JOIN dbo.AspNetUsers u ON u.NormalizedEmail = UPPER(p.Email)
LEFT JOIN dbo.Members     m ON m.ApplicationUserId = u.Id
LEFT JOIN dbo.Companies   c ON c.Id = m.CompanyId
LEFT JOIN dbo.Departments d ON d.Id = m.DepartmentId
LEFT JOIN dbo.Members   mgr ON mgr.Id = m.ReportingManagerId
ORDER BY p.DisplayName;
GO
