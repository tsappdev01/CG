/*
    ============================================================================================
    FULL DATA RESET -- deletes EVERY row in EVERY application and login table, including master
    data (Companies, Departments, Job Titles), all declarations/transactions/audit history, and
    ALL ASP.NET Identity login accounts (AspNetUsers/AspNetRoles/etc).

    Every DELETE/CHECKIDENT below is guarded with IF OBJECT_ID(...) IS NOT NULL, so this runs
    cleanly against a database whose migrations aren't fully applied yet (e.g. tables added by
    recent EF Core migrations, like InsiderDeclarationRelatives/InsiderDeclarationNinHolders,
    only exist once the app has actually started up and run `db.Database.MigrateAsync()` against
    this database at least once) -- a missing table is silently skipped, not an error. Any OTHER
    error (e.g. an FK conflict) aborts the whole transaction via SET XACT_ABORT ON below, so this
    either fully succeeds or changes nothing -- never a partial reset.

    THIS IS IRREVERSIBLE. There is no undo short of restoring a database backup taken before
    running this. Take a backup first if there is any chance you'll want this data back:
        BACKUP DATABASE CGS TO DISK = 'CGS_before_reset.bak';

    CONSEQUENCE OF WIPING LOGINS: after this runs, nobody can sign in -- there is no admin
    account left, Azure AD SSO logins are gone (they're stored as ASP.NET Identity external
    logins, deleted below), and local sign-in is gone too. To get back in:
      1. Restart the app once (or let it start normally) -- this applies any pending migrations,
         so any table skipped above (if it didn't exist yet) will exist for next time.
      2. Sign in however this deployment normally authenticates (Windows/AD, Azure AD SSO, or
         the local dev fallback/Account/Register) -- this creates/links a fresh AspNetUsers row
         for you with no roles yet.
      3. Go to /admin/setup and click "Claim administrator access". That page only allows this
         while zero users hold the Administrator role (see AdminSetup.razor) -- exactly the state
         this script leaves the database in -- so it grants your account Administrator with no
         manual SQL required.
      4. From User Management, create/link a Member record for yourself if the app's admin
         screens require one to fully use the account, and re-add any other users' accounts.

    NOTE ON DEMO DATA: GovernanceSeeder.SeedAsync runs on every app startup and re-inserts its
    sample Companies/Departments/Members/Transactions automatically whenever the Members table is
    empty (see Program.cs). After this script runs, the NEXT app start will repopulate that
    sample data unless you delete it again -- it is not an empty database that stays empty.

    Run manually against the CGS database (e.g. via SSMS, Azure Data Studio, or
    `sqlcmd -S UATWEB01 -d CGS -i scripts/reset-all-data.sql`). Not part of the app's normal
    deployment -- this is destructive and one-off, and is NOT gated by any confirmation prompt
    of its own once you run it.
    ============================================================================================
*/

SET NOCOUNT ON;
-- Without this, a mid-script error (like the Companies<->Members FK cycle this script used to hit)
-- only aborts the one offending statement -- everything else still runs and COMMIT still succeeds,
-- silently leaving a partial reset with no indication anything went wrong. XACT_ABORT makes any
-- error roll back the whole transaction instead.
SET XACT_ABORT ON;
BEGIN TRANSACTION;

-- ---------- Insider Trading declarations (children first) ----------
IF OBJECT_ID(N'dbo.InsiderDeclarationRelatives', N'U') IS NOT NULL DELETE FROM dbo.InsiderDeclarationRelatives;
IF OBJECT_ID(N'dbo.InsiderDeclarationNinHolders', N'U') IS NOT NULL DELETE FROM dbo.InsiderDeclarationNinHolders;
IF OBJECT_ID(N'dbo.InsiderDeclarations', N'U') IS NOT NULL DELETE FROM dbo.InsiderDeclarations;
IF OBJECT_ID(N'dbo.DeclarationCycleRunRecipients', N'U') IS NOT NULL DELETE FROM dbo.DeclarationCycleRunRecipients;
IF OBJECT_ID(N'dbo.DeclarationCycleRuns', N'U') IS NOT NULL DELETE FROM dbo.DeclarationCycleRuns;
IF OBJECT_ID(N'dbo.DeclarationCycleSetups', N'U') IS NOT NULL DELETE FROM dbo.DeclarationCycleSetups;

-- ---------- Investor Relations (Share Register / Shares Trading uploads) ----------
IF OBJECT_ID(N'dbo.ShareholderRecords', N'U') IS NOT NULL DELETE FROM dbo.ShareholderRecords;
IF OBJECT_ID(N'dbo.ShareholderRegisterUploads', N'U') IS NOT NULL DELETE FROM dbo.ShareholderRegisterUploads;
IF OBJECT_ID(N'dbo.ShareTradingRecords', N'U') IS NOT NULL DELETE FROM dbo.ShareTradingRecords;
IF OBJECT_ID(N'dbo.ShareTradingUploads', N'U') IS NOT NULL DELETE FROM dbo.ShareTradingUploads;

-- ---------- Related Party Transactions ----------
IF OBJECT_ID(N'dbo.RelatedPartyTransactionDocuments', N'U') IS NOT NULL DELETE FROM dbo.RelatedPartyTransactionDocuments;
IF OBJECT_ID(N'dbo.RelatedPartyTransactions', N'U') IS NOT NULL DELETE FROM dbo.RelatedPartyTransactions;

-- ---------- Related Party / COI declarations (current + legacy) + scheduled activities ----------
IF OBJECT_ID(N'dbo.CoiTradeLicenseDocuments', N'U') IS NOT NULL DELETE FROM dbo.CoiTradeLicenseDocuments;
IF OBJECT_ID(N'dbo.CoiCompanyEntries', N'U') IS NOT NULL DELETE FROM dbo.CoiCompanyEntries;
IF OBJECT_ID(N'dbo.CoiConflictEntries', N'U') IS NOT NULL DELETE FROM dbo.CoiConflictEntries;
IF OBJECT_ID(N'dbo.CoiRelatives', N'U') IS NOT NULL DELETE FROM dbo.CoiRelatives;
IF OBJECT_ID(N'dbo.RelatedPartyCoiDeclarations', N'U') IS NOT NULL DELETE FROM dbo.RelatedPartyCoiDeclarations;
IF OBJECT_ID(N'dbo.DeclarationSubmissions', N'U') IS NOT NULL DELETE FROM dbo.DeclarationSubmissions;
IF OBJECT_ID(N'dbo.DeclarationSetups', N'U') IS NOT NULL DELETE FROM dbo.DeclarationSetups;
IF OBJECT_ID(N'dbo.ScheduledActivityDates', N'U') IS NOT NULL DELETE FROM dbo.ScheduledActivityDates;
IF OBJECT_ID(N'dbo.ScheduledActivities', N'U') IS NOT NULL DELETE FROM dbo.ScheduledActivities;
IF OBJECT_ID(N'dbo.MemberNotifications', N'U') IS NOT NULL DELETE FROM dbo.MemberNotifications;

-- ---------- Transaction monitoring + member-level data ----------
IF OBJECT_ID(N'dbo.Transactions', N'U') IS NOT NULL DELETE FROM dbo.Transactions;
IF OBJECT_ID(N'dbo.MemberImpersonationApprovals', N'U') IS NOT NULL DELETE FROM dbo.MemberImpersonationApprovals;
IF OBJECT_ID(N'dbo.AuditLogEntries', N'U') IS NOT NULL DELETE FROM dbo.AuditLogEntries;

-- ---------- Standalone/independent tables (no FKs pointing at them) ----------
IF OBJECT_ID(N'dbo.PolicyDocumentVersions', N'U') IS NOT NULL DELETE FROM dbo.PolicyDocumentVersions;
IF OBJECT_ID(N'dbo.NavMenuItemOrders', N'U') IS NOT NULL DELETE FROM dbo.NavMenuItemOrders;
IF OBJECT_ID(N'dbo.NavMenuItemLabels', N'U') IS NOT NULL DELETE FROM dbo.NavMenuItemLabels;

-- Companies.ApprovingAuthorityMemberId/DelegateAuthorityMemberId point at Members, while
-- Members.CompanyId points back at Companies -- a genuine cycle, so neither table can be deleted
-- first. Break it by nulling those two columns before deleting Members.
IF OBJECT_ID(N'dbo.Companies', N'U') IS NOT NULL
    UPDATE dbo.Companies SET ApprovingAuthorityMemberId = NULL, DelegateAuthorityMemberId = NULL;

-- Members reference Companies/Departments/JobTitle(string)/ReportingManager(self)/ApplicationUser
-- (Identity) -- clear Members before the master tables it points at, and before Identity users.
IF OBJECT_ID(N'dbo.Members', N'U') IS NOT NULL DELETE FROM dbo.Members;

-- ---------- Master data ----------
IF OBJECT_ID(N'dbo.JobTitles', N'U') IS NOT NULL DELETE FROM dbo.JobTitles;
IF OBJECT_ID(N'dbo.Departments', N'U') IS NOT NULL DELETE FROM dbo.Departments;
IF OBJECT_ID(N'dbo.Companies', N'U') IS NOT NULL DELETE FROM dbo.Companies;

-- ---------- ASP.NET Core Identity (login accounts) ----------
IF OBJECT_ID(N'dbo.AspNetUserTokens', N'U') IS NOT NULL DELETE FROM dbo.AspNetUserTokens;
IF OBJECT_ID(N'dbo.AspNetUserLogins', N'U') IS NOT NULL DELETE FROM dbo.AspNetUserLogins;
IF OBJECT_ID(N'dbo.AspNetUserClaims', N'U') IS NOT NULL DELETE FROM dbo.AspNetUserClaims;
IF OBJECT_ID(N'dbo.AspNetUserRoles', N'U') IS NOT NULL DELETE FROM dbo.AspNetUserRoles;
IF OBJECT_ID(N'dbo.AspNetRoleClaims', N'U') IS NOT NULL DELETE FROM dbo.AspNetRoleClaims;
IF OBJECT_ID(N'dbo.AspNetUsers', N'U') IS NOT NULL DELETE FROM dbo.AspNetUsers;
IF OBJECT_ID(N'dbo.AspNetRoles', N'U') IS NOT NULL DELETE FROM dbo.AspNetRoles;

-- Reseed identity columns (int PKs) back to 1 for the next rows created after this reset.
-- AspNet* tables use string (GUID) primary keys, not identity columns, so they're excluded here.
IF OBJECT_ID(N'dbo.InsiderDeclarationRelatives', N'U') IS NOT NULL DBCC CHECKIDENT ('dbo.InsiderDeclarationRelatives', RESEED, 0);
IF OBJECT_ID(N'dbo.InsiderDeclarationNinHolders', N'U') IS NOT NULL DBCC CHECKIDENT ('dbo.InsiderDeclarationNinHolders', RESEED, 0);
IF OBJECT_ID(N'dbo.InsiderDeclarations', N'U') IS NOT NULL DBCC CHECKIDENT ('dbo.InsiderDeclarations', RESEED, 0);
IF OBJECT_ID(N'dbo.DeclarationCycleRunRecipients', N'U') IS NOT NULL DBCC CHECKIDENT ('dbo.DeclarationCycleRunRecipients', RESEED, 0);
IF OBJECT_ID(N'dbo.DeclarationCycleRuns', N'U') IS NOT NULL DBCC CHECKIDENT ('dbo.DeclarationCycleRuns', RESEED, 0);
IF OBJECT_ID(N'dbo.DeclarationCycleSetups', N'U') IS NOT NULL DBCC CHECKIDENT ('dbo.DeclarationCycleSetups', RESEED, 0);
IF OBJECT_ID(N'dbo.ShareholderRecords', N'U') IS NOT NULL DBCC CHECKIDENT ('dbo.ShareholderRecords', RESEED, 0);
IF OBJECT_ID(N'dbo.ShareholderRegisterUploads', N'U') IS NOT NULL DBCC CHECKIDENT ('dbo.ShareholderRegisterUploads', RESEED, 0);
IF OBJECT_ID(N'dbo.ShareTradingRecords', N'U') IS NOT NULL DBCC CHECKIDENT ('dbo.ShareTradingRecords', RESEED, 0);
IF OBJECT_ID(N'dbo.ShareTradingUploads', N'U') IS NOT NULL DBCC CHECKIDENT ('dbo.ShareTradingUploads', RESEED, 0);
IF OBJECT_ID(N'dbo.PolicyDocumentVersions', N'U') IS NOT NULL DBCC CHECKIDENT ('dbo.PolicyDocumentVersions', RESEED, 0);
IF OBJECT_ID(N'dbo.NavMenuItemOrders', N'U') IS NOT NULL DBCC CHECKIDENT ('dbo.NavMenuItemOrders', RESEED, 0);
IF OBJECT_ID(N'dbo.NavMenuItemLabels', N'U') IS NOT NULL DBCC CHECKIDENT ('dbo.NavMenuItemLabels', RESEED, 0);
IF OBJECT_ID(N'dbo.RelatedPartyTransactions', N'U') IS NOT NULL DBCC CHECKIDENT ('dbo.RelatedPartyTransactions', RESEED, 0);
IF OBJECT_ID(N'dbo.RelatedPartyTransactionDocuments', N'U') IS NOT NULL DBCC CHECKIDENT ('dbo.RelatedPartyTransactionDocuments', RESEED, 0);
IF OBJECT_ID(N'dbo.CoiTradeLicenseDocuments', N'U') IS NOT NULL DBCC CHECKIDENT ('dbo.CoiTradeLicenseDocuments', RESEED, 0);
IF OBJECT_ID(N'dbo.CoiCompanyEntries', N'U') IS NOT NULL DBCC CHECKIDENT ('dbo.CoiCompanyEntries', RESEED, 0);
IF OBJECT_ID(N'dbo.CoiConflictEntries', N'U') IS NOT NULL DBCC CHECKIDENT ('dbo.CoiConflictEntries', RESEED, 0);
IF OBJECT_ID(N'dbo.CoiRelatives', N'U') IS NOT NULL DBCC CHECKIDENT ('dbo.CoiRelatives', RESEED, 0);
IF OBJECT_ID(N'dbo.RelatedPartyCoiDeclarations', N'U') IS NOT NULL DBCC CHECKIDENT ('dbo.RelatedPartyCoiDeclarations', RESEED, 0);
IF OBJECT_ID(N'dbo.DeclarationSubmissions', N'U') IS NOT NULL DBCC CHECKIDENT ('dbo.DeclarationSubmissions', RESEED, 0);
IF OBJECT_ID(N'dbo.DeclarationSetups', N'U') IS NOT NULL DBCC CHECKIDENT ('dbo.DeclarationSetups', RESEED, 0);
IF OBJECT_ID(N'dbo.ScheduledActivityDates', N'U') IS NOT NULL DBCC CHECKIDENT ('dbo.ScheduledActivityDates', RESEED, 0);
IF OBJECT_ID(N'dbo.ScheduledActivities', N'U') IS NOT NULL DBCC CHECKIDENT ('dbo.ScheduledActivities', RESEED, 0);
IF OBJECT_ID(N'dbo.MemberNotifications', N'U') IS NOT NULL DBCC CHECKIDENT ('dbo.MemberNotifications', RESEED, 0);
IF OBJECT_ID(N'dbo.Transactions', N'U') IS NOT NULL DBCC CHECKIDENT ('dbo.Transactions', RESEED, 0);
IF OBJECT_ID(N'dbo.MemberImpersonationApprovals', N'U') IS NOT NULL DBCC CHECKIDENT ('dbo.MemberImpersonationApprovals', RESEED, 0);
IF OBJECT_ID(N'dbo.AuditLogEntries', N'U') IS NOT NULL DBCC CHECKIDENT ('dbo.AuditLogEntries', RESEED, 0);
IF OBJECT_ID(N'dbo.Members', N'U') IS NOT NULL DBCC CHECKIDENT ('dbo.Members', RESEED, 0);
IF OBJECT_ID(N'dbo.JobTitles', N'U') IS NOT NULL DBCC CHECKIDENT ('dbo.JobTitles', RESEED, 0);
IF OBJECT_ID(N'dbo.Departments', N'U') IS NOT NULL DBCC CHECKIDENT ('dbo.Departments', RESEED, 0);
IF OBJECT_ID(N'dbo.Companies', N'U') IS NOT NULL DBCC CHECKIDENT ('dbo.Companies', RESEED, 0);

COMMIT TRANSACTION;

PRINT 'Full reset complete: all business data, master data, and login accounts have been deleted (skipping any table not yet created by migrations -- see this script''s header comment).';
PRINT 'See the header comment in this script for how to regain admin access.';
