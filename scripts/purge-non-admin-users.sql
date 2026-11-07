/*
    ============================================================================================
    PURGE NON-ADMIN USERS -- deletes every sign-in account and Member that is not an
    Administrator, together with the rows that belong to those people.

    KEPT, deliberately:
      * Every account in the Administrator role, its Member record, and everything belonging to
        it -- declarations, transactions, documents, notifications.
      * All master/reference data: Companies, Departments, JobTitles, DeclarationSetups,
        DeclarationCycleSetups, NavMenu ordering/labels/visibility, DelegationOfAuthority bands,
        PolicyDocumentVersions, ScheduledActivities.
      * AspNetRoles themselves. The roles survive; only the people in them go.
      * AuditLogEntries -- see NOTE ON THE AUDIT LOG below. This is not an oversight.

    "Administrator" is the role name in GovernanceRoles.Administrator. A Member with no
    ApplicationUserId is not a sign-in account and so is NOT an administrator: it is deleted.
    If you have directory-only Members you want to keep, set @KeepUnlinkedMembers = 1.

    DRY RUN BY DEFAULT. @DryRun = 1 reports what would go and rolls everything back. Read the
    report, then set @DryRun = 0 to commit. SET XACT_ABORT ON means any error aborts the whole
    transaction, so this either fully succeeds or changes nothing -- never a partial purge.

    Every statement is guarded with IF OBJECT_ID(...) IS NOT NULL, so it runs cleanly against a
    database whose migrations are not fully applied: a missing table is skipped, not an error.

    THIS IS IRREVERSIBLE. There is no undo short of a restore. Take a backup first:
        BACKUP DATABASE CGS TO DISK = 'CGS_before_purge.bak';

    NOTE ON THE AUDIT LOG. AuditLogEntries is a hash chain: each row carries a Sequence and a
    RecordHash over its own fields plus the previous row's hash, and IAuditLogIntegrity.VerifyAsync
    checks the chain is unbroken and gapless. Deleting any row breaks it permanently, and the
    Audit Trail report would then open with "the record may have been tampered with" forever
    after. So the log keeps its entries about deleted people. Those entries name actors by id,
    with no foreign key, so nothing here fails -- the names simply no longer resolve to a Member.

    TWO THINGS THIS HAS TO CLEAR FIRST, because their foreign keys are Restrict and would
    otherwise block the delete. Both are reported by name when they happen:
      * Companies.ApprovingAuthorityMemberId / DelegateAuthorityMemberId pointing at a deleted
        member are set to NULL. Company.Validate REQUIRES an approving authority unless
        ApprovingAuthorityNotApplicable is set, so any company listed in that report needs a new
        approver assigned in Admin Panel > Company before it will save again.
      * Members.ReportingManagerId on a SURVIVING admin pointing at a deleted member is set to
        NULL.

    RelatedPartyTransactions raised BY a deleted member are deleted. A transaction raised by a
    survivor but merely APPROVED by a deleted member is KEPT, with ApproverMemberId set to NULL --
    deleting it would destroy a surviving administrator's own record.
    ============================================================================================
*/

SET NOCOUNT ON;
SET XACT_ABORT ON;

DECLARE @DryRun                bit = 1;   -- 1 = report only (default). 0 = actually delete.
DECLARE @KeepUnlinkedMembers   bit = 0;   -- 1 = keep Members that have no sign-in account.

BEGIN TRANSACTION;

/* -- who survives -------------------------------------------------------------------------- */

DROP TABLE IF EXISTS #KeepUsers, #KeepMembers, #GoUsers, #GoMembers;

CREATE TABLE #KeepUsers   (Id nvarchar(450) NOT NULL PRIMARY KEY);
CREATE TABLE #KeepMembers (Id int           NOT NULL PRIMARY KEY);
CREATE TABLE #GoUsers     (Id nvarchar(450) NOT NULL PRIMARY KEY);
CREATE TABLE #GoMembers   (Id int           NOT NULL PRIMARY KEY);

INSERT INTO #KeepUsers (Id)
SELECT DISTINCT ur.UserId
FROM   AspNetUserRoles ur
JOIN   AspNetRoles r ON r.Id = ur.RoleId
WHERE  r.Name = N'Administrator';

IF NOT EXISTS (SELECT 1 FROM #KeepUsers)
BEGIN
    ROLLBACK TRANSACTION;
    RAISERROR(N'Refusing to run: no account holds the Administrator role, so this would delete every user and lock you out. Give someone Administrator first.', 16, 1);
    RETURN;
END;

INSERT INTO #KeepMembers (Id)
SELECT m.Id
FROM   Members m
WHERE  (m.ApplicationUserId IS NOT NULL AND m.ApplicationUserId IN (SELECT Id FROM #KeepUsers))
    OR (@KeepUnlinkedMembers = 1 AND m.ApplicationUserId IS NULL);

INSERT INTO #GoUsers (Id)
SELECT u.Id FROM AspNetUsers u WHERE u.Id NOT IN (SELECT Id FROM #KeepUsers);

INSERT INTO #GoMembers (Id)
SELECT m.Id FROM Members m WHERE m.Id NOT IN (SELECT Id FROM #KeepMembers);

PRINT N'--- WHO SURVIVES ---';
SELECT u.Id, u.UserName, u.Email, N'KEEP (Administrator)' AS Outcome FROM AspNetUsers u JOIN #KeepUsers k ON k.Id = u.Id;

PRINT N'--- WHO GOES ---';
SELECT u.Id, u.UserName, u.Email, N'DELETE' AS Outcome FROM AspNetUsers u JOIN #GoUsers g ON g.Id = u.Id;
SELECT m.Id, m.FullName, m.Email, CASE WHEN m.ApplicationUserId IS NULL THEN N'DELETE (no sign-in account)' ELSE N'DELETE' END AS Outcome
FROM   Members m JOIN #GoMembers g ON g.Id = m.Id;

/* -- clear the Restrict references that would block the delete ------------------------------ */

PRINT N'--- COMPANIES THAT WILL LOSE THEIR APPROVER (reassign these afterwards) ---';
SELECT c.Id, c.Name,
       CASE WHEN c.ApprovingAuthorityMemberId IN (SELECT Id FROM #GoMembers) THEN N'Approving authority cleared' ELSE N'' END
     + CASE WHEN c.DelegateAuthorityMemberId  IN (SELECT Id FROM #GoMembers) THEN N' Delegate cleared' ELSE N'' END AS Effect
FROM   Companies c
WHERE  c.ApprovingAuthorityMemberId IN (SELECT Id FROM #GoMembers)
    OR c.DelegateAuthorityMemberId  IN (SELECT Id FROM #GoMembers);

UPDATE Companies SET ApprovingAuthorityMemberId = NULL WHERE ApprovingAuthorityMemberId IN (SELECT Id FROM #GoMembers);
UPDATE Companies SET DelegateAuthorityMemberId  = NULL WHERE DelegateAuthorityMemberId  IN (SELECT Id FROM #GoMembers);

-- Both survivors and the doomed: a doomed member reporting to another doomed member is fine
-- in one set-based DELETE (the self-FK is checked at statement end), but clearing it first
-- keeps that from depending on a subtlety.
UPDATE Members SET ReportingManagerId = NULL WHERE ReportingManagerId IN (SELECT Id FROM #GoMembers);

-- Config rows that carry a MemberId with no foreign key behind it. Nothing blocks the delete,
-- so they are only reported: the id in them stops resolving and the cycle needs re-pointing.
IF OBJECT_ID('dbo.DeclarationCycleSetups') IS NOT NULL
BEGIN
    PRINT N'--- DECLARATION CYCLE SETUPS POINTING AT A DELETED MEMBER (re-point these) ---';
    SELECT Id, MemberId FROM DeclarationCycleSetups WHERE MemberId IN (SELECT Id FROM #GoMembers);
END;

IF OBJECT_ID('dbo.RelatedPartyTransactions') IS NOT NULL
    UPDATE RelatedPartyTransactions SET ApproverMemberId = NULL
    WHERE  ApproverMemberId IN (SELECT Id FROM #GoMembers)
      AND  MemberId NOT IN (SELECT Id FROM #GoMembers);

/* -- delete, children first ------------------------------------------------------------------ */

IF OBJECT_ID('dbo.FamilyMemberHoldings') IS NOT NULL
    DELETE FROM FamilyMemberHoldings WHERE FamilyMemberId IN (SELECT Id FROM FamilyMembers WHERE MemberId IN (SELECT Id FROM #GoMembers));
IF OBJECT_ID('dbo.FamilyMembers') IS NOT NULL
    DELETE FROM FamilyMembers WHERE MemberId IN (SELECT Id FROM #GoMembers);

IF OBJECT_ID('dbo.InsiderDeclarationNinHolders') IS NOT NULL
    DELETE FROM InsiderDeclarationNinHolders WHERE InsiderDeclarationId IN (SELECT Id FROM InsiderDeclarations WHERE MemberId IN (SELECT Id FROM #GoMembers));
IF OBJECT_ID('dbo.InsiderDeclarationRelatives') IS NOT NULL
    DELETE FROM InsiderDeclarationRelatives WHERE InsiderDeclarationId IN (SELECT Id FROM InsiderDeclarations WHERE MemberId IN (SELECT Id FROM #GoMembers));
IF OBJECT_ID('dbo.InsiderDeclarations') IS NOT NULL
    DELETE FROM InsiderDeclarations WHERE MemberId IN (SELECT Id FROM #GoMembers);

IF OBJECT_ID('dbo.CoiTradeLicenseDocuments') IS NOT NULL
    DELETE FROM CoiTradeLicenseDocuments WHERE CoiCompanyEntryId IN (
        SELECT Id FROM CoiCompanyEntries WHERE RelatedPartyCoiDeclarationId IN (
            SELECT Id FROM RelatedPartyCoiDeclarations WHERE MemberId IN (SELECT Id FROM #GoMembers)));
IF OBJECT_ID('dbo.CoiCompanyEntries') IS NOT NULL
    DELETE FROM CoiCompanyEntries WHERE RelatedPartyCoiDeclarationId IN (SELECT Id FROM RelatedPartyCoiDeclarations WHERE MemberId IN (SELECT Id FROM #GoMembers));
IF OBJECT_ID('dbo.CoiConflictEntries') IS NOT NULL
    DELETE FROM CoiConflictEntries WHERE RelatedPartyCoiDeclarationId IN (SELECT Id FROM RelatedPartyCoiDeclarations WHERE MemberId IN (SELECT Id FROM #GoMembers));
IF OBJECT_ID('dbo.CoiRelatives') IS NOT NULL
    DELETE FROM CoiRelatives WHERE RelatedPartyCoiDeclarationId IN (SELECT Id FROM RelatedPartyCoiDeclarations WHERE MemberId IN (SELECT Id FROM #GoMembers));
IF OBJECT_ID('dbo.RelatedPartyCoiDeclarations') IS NOT NULL
    DELETE FROM RelatedPartyCoiDeclarations WHERE MemberId IN (SELECT Id FROM #GoMembers);

IF OBJECT_ID('dbo.RelatedPartyTransactionDocuments') IS NOT NULL
    DELETE FROM RelatedPartyTransactionDocuments WHERE RelatedPartyTransactionId IN (SELECT Id FROM RelatedPartyTransactions WHERE MemberId IN (SELECT Id FROM #GoMembers));
IF OBJECT_ID('dbo.RelatedPartyTransactions') IS NOT NULL
    DELETE FROM RelatedPartyTransactions WHERE MemberId IN (SELECT Id FROM #GoMembers);

IF OBJECT_ID('dbo.Transactions') IS NOT NULL
    DELETE FROM Transactions WHERE MemberId IN (SELECT Id FROM #GoMembers);
IF OBJECT_ID('dbo.DeclarationSubmissions') IS NOT NULL
    DELETE FROM DeclarationSubmissions WHERE MemberId IN (SELECT Id FROM #GoMembers);
IF OBJECT_ID('dbo.DeclarationReminderLogs') IS NOT NULL
    DELETE FROM DeclarationReminderLogs WHERE MemberId IN (SELECT Id FROM #GoMembers);
IF OBJECT_ID('dbo.MemberNotifications') IS NOT NULL
    DELETE FROM MemberNotifications WHERE MemberId IN (SELECT Id FROM #GoMembers);
IF OBJECT_ID('dbo.MemberDocuments') IS NOT NULL
    DELETE FROM MemberDocuments WHERE MemberId IN (SELECT Id FROM #GoMembers);
IF OBJECT_ID('dbo.OwnedCompanies') IS NOT NULL
    DELETE FROM OwnedCompanies WHERE MemberId IN (SELECT Id FROM #GoMembers);

-- ImpersonatorId is NOT NULL, so an approval naming a deleted impersonator cannot be kept.
IF OBJECT_ID('dbo.MemberImpersonationApprovals') IS NOT NULL
    DELETE FROM MemberImpersonationApprovals
    WHERE  MemberId IN (SELECT Id FROM #GoMembers) OR ImpersonatorId IN (SELECT Id FROM #GoMembers);

DELETE FROM Members WHERE Id IN (SELECT Id FROM #GoMembers);

DELETE FROM AspNetUserClaims WHERE UserId IN (SELECT Id FROM #GoUsers);
DELETE FROM AspNetUserLogins WHERE UserId IN (SELECT Id FROM #GoUsers);
DELETE FROM AspNetUserRoles  WHERE UserId IN (SELECT Id FROM #GoUsers);
DELETE FROM AspNetUserTokens WHERE UserId IN (SELECT Id FROM #GoUsers);
DELETE FROM AspNetUsers      WHERE Id     IN (SELECT Id FROM #GoUsers);

/* -- commit or roll back --------------------------------------------------------------------- */

DECLARE @UsersDeleted int = (SELECT COUNT(*) FROM #GoUsers);
DECLARE @MembersDeleted int = (SELECT COUNT(*) FROM #GoMembers);
DECLARE @AdminsKept int = (SELECT COUNT(*) FROM #KeepUsers);
SELECT @UsersDeleted AS UsersDeleted, @MembersDeleted AS MembersDeleted, @AdminsKept AS AdminsKept;

IF @DryRun = 1
BEGIN
    ROLLBACK TRANSACTION;
    PRINT N'DRY RUN -- everything above was rolled back. Set @DryRun = 0 to commit.';
END
ELSE
BEGIN
    COMMIT TRANSACTION;
    PRINT N'COMMITTED -- non-admin users and members deleted.';
END;

-- Dropped AFTER the transaction closes. A DROP inside it is itself rolled back on a dry run,
-- leaving the tables behind for the rest of the session and failing the next run's CREATE.
DROP TABLE #KeepUsers, #KeepMembers, #GoUsers, #GoMembers;
