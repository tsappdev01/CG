/*
    CGTOOL stored procedures for all application-level write operations
    (insert/update/deactivate/delete) on this app's own domain tables.

    Scope, deliberately: ASP.NET Core Identity's own tables/operations
    (AspNetUsers/AspNetRoles/AspNetUserRoles — login, lockout, password
    hashing, role assignment via UserManager/RoleManager/SignInManager)
    are NOT covered here. Identity owns that storage; replacing its
    internal data access is a much larger, security-sensitive rewrite
    with little practical benefit, so it stays on EF Core's built-in store.
    One-time development seed data (GovernanceSeeder) is also left as EF
    Core — it's startup fixture data, not a runtime write path.

    Read queries (search, filters, pagination, dashboard listings, audit
    log browsing) are unaffected and remain LINQ/EF Core — only writes are
    covered here.

    Custom error numbers used with THROW (mirroring the app's previous
    DbUpdateException-catch behavior, now surfaced as SqlException.Number
    on the C# side):
      50001 - Duplicate Company.ShortCode
      50002 - Duplicate Department.Code
      50003 - Member.ApplicationUserId already linked to another Member
      50004 - Reporting manager assignment would create a cycle
      50005 - Duplicate DeclarationSubmission (Member already submitted this period)
      50006 - Duplicate ScheduledActivityDate (Month/Day already configured)
      50007 - DeclarationSetup.DueDate is before Date
      50010 - Duplicate JobTitle.Name
      50011 - Duplicate InsiderDeclaration (Member already submitted this cycle)
      50012 - Duplicate RelatedPartyCoiDeclaration (Member already submitted this cycle)

    Safe to re-run: every procedure uses CREATE OR ALTER.
*/

USE [CGS];
GO

-- =========================== Company ===========================

CREATE OR ALTER PROCEDURE dbo.usp_Company_Insert
    @Name nvarchar(160),
    @ShortCode nvarchar(20),
    @Address nvarchar(240) = NULL,
    @City nvarchar(80) = NULL,
    @Country nvarchar(80) = NULL,
    @Sector nvarchar(80) = NULL,
    @GroupName nvarchar(80) = NULL,
    @ApprovingAuthorityMemberId int = NULL,
    @DelegateAuthorityMemberId int = NULL,
    @Active bit = 1,
    @NewId int OUTPUT
AS
BEGIN
    SET NOCOUNT ON;

    IF EXISTS (SELECT 1 FROM dbo.Companies WHERE ShortCode = @ShortCode)
        THROW 50001, 'A company with this short code already exists.', 1;

    INSERT INTO dbo.Companies
        (Name, ShortCode, Address, City, Country, Sector, GroupName, ApprovingAuthorityMemberId, DelegateAuthorityMemberId, Active)
    VALUES
        (@Name, @ShortCode, @Address, @City, @Country, @Sector, @GroupName, @ApprovingAuthorityMemberId, @DelegateAuthorityMemberId, @Active);

    SET @NewId = SCOPE_IDENTITY();
END
GO

CREATE OR ALTER PROCEDURE dbo.usp_Company_Update
    @Id int,
    @Name nvarchar(160),
    @ShortCode nvarchar(20),
    @Address nvarchar(240) = NULL,
    @City nvarchar(80) = NULL,
    @Country nvarchar(80) = NULL,
    @Sector nvarchar(80) = NULL,
    @GroupName nvarchar(80) = NULL,
    @ApprovingAuthorityMemberId int = NULL,
    @DelegateAuthorityMemberId int = NULL,
    @Active bit = 1
AS
BEGIN
    SET NOCOUNT ON;

    IF EXISTS (SELECT 1 FROM dbo.Companies WHERE ShortCode = @ShortCode AND Id <> @Id)
        THROW 50001, 'A company with this short code already exists.', 1;

    UPDATE dbo.Companies
    SET Name = @Name,
        ShortCode = @ShortCode,
        Address = @Address,
        City = @City,
        Country = @Country,
        Sector = @Sector,
        GroupName = @GroupName,
        ApprovingAuthorityMemberId = @ApprovingAuthorityMemberId,
        DelegateAuthorityMemberId = @DelegateAuthorityMemberId,
        Active = @Active
    WHERE Id = @Id;
END
GO

CREATE OR ALTER PROCEDURE dbo.usp_Company_SetActive
    @Id int,
    @Active bit
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE dbo.Companies SET Active = @Active WHERE Id = @Id;
END
GO

CREATE OR ALTER PROCEDURE dbo.usp_Company_SetLogoPath
    @Id int,
    @LogoPath nvarchar(260) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE dbo.Companies SET LogoPath = @LogoPath WHERE Id = @Id;
END
GO

-- =========================== Department ===========================

CREATE OR ALTER PROCEDURE dbo.usp_Department_Insert
    @Code nvarchar(20),
    @Name nvarchar(120),
    @Active bit = 1,
    @NewId int OUTPUT
AS
BEGIN
    SET NOCOUNT ON;

    IF EXISTS (SELECT 1 FROM dbo.Departments WHERE Code = @Code)
        THROW 50002, 'A department with this code already exists.', 1;

    INSERT INTO dbo.Departments (Code, Name, Active) VALUES (@Code, @Name, @Active);
    SET @NewId = SCOPE_IDENTITY();
END
GO

CREATE OR ALTER PROCEDURE dbo.usp_Department_Update
    @Id int,
    @Code nvarchar(20),
    @Name nvarchar(120),
    @Active bit = 1
AS
BEGIN
    SET NOCOUNT ON;

    IF EXISTS (SELECT 1 FROM dbo.Departments WHERE Code = @Code AND Id <> @Id)
        THROW 50002, 'A department with this code already exists.', 1;

    UPDATE dbo.Departments SET Code = @Code, Name = @Name, Active = @Active WHERE Id = @Id;
END
GO

CREATE OR ALTER PROCEDURE dbo.usp_Department_SetActive
    @Id int,
    @Active bit
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE dbo.Departments SET Active = @Active WHERE Id = @Id;
END
GO

-- =========================== JobTitle ===========================

CREATE OR ALTER PROCEDURE dbo.usp_JobTitle_Insert
    @Name nvarchar(120),
    @Active bit = 1,
    @NewId int OUTPUT
AS
BEGIN
    SET NOCOUNT ON;

    IF EXISTS (SELECT 1 FROM dbo.JobTitles WHERE Name = @Name)
        THROW 50010, 'A job title with this name already exists.', 1;

    INSERT INTO dbo.JobTitles (Name, Active) VALUES (@Name, @Active);
    SET @NewId = SCOPE_IDENTITY();
END
GO

CREATE OR ALTER PROCEDURE dbo.usp_JobTitle_Update
    @Id int,
    @Name nvarchar(120),
    @Active bit
AS
BEGIN
    SET NOCOUNT ON;

    IF EXISTS (SELECT 1 FROM dbo.JobTitles WHERE Name = @Name AND Id <> @Id)
        THROW 50010, 'A job title with this name already exists.', 1;

    UPDATE dbo.JobTitles SET Name = @Name, Active = @Active WHERE Id = @Id;
END
GO

CREATE OR ALTER PROCEDURE dbo.usp_JobTitle_SetActive
    @Id int,
    @Active bit
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE dbo.JobTitles SET Active = @Active WHERE Id = @Id;
END
GO

-- =========================== Member ===========================

CREATE OR ALTER PROCEDURE dbo.usp_Member_Insert
    @CompanyId int,
    @FullName nvarchar(120),
    @JobTitle nvarchar(120) = NULL,
    @DepartmentId int = NULL,
    @Email nvarchar(160) = NULL,
    @AzureAdObjectId nvarchar(80) = NULL,
    @Signature nvarchar(160) = NULL,
    @ReportingManagerId int = NULL,
    @ApplicationUserId nvarchar(450) = NULL,
    @IsBoardMember bit = 0,
    @IsManualEntry bit = 0,
    @IsExternalMember bit = 0,
    @IsExecutiveManagement bit = 0,
    @Active bit = 1,
    @InsiderTradingAccess bit = 0,
    @ConflictOfInterestAccess bit = 0,
    @RelatedPartyRegisterAccess bit = 0,
    @RelatedPartyTransactionAccess bit = 0,
    @RpTransactionRole int = 0,
    @CanBeImpersonated bit = 0,
    @NewId int OUTPUT
AS
BEGIN
    SET NOCOUNT ON;

    IF @ApplicationUserId IS NOT NULL AND EXISTS (SELECT 1 FROM dbo.Members WHERE ApplicationUserId = @ApplicationUserId)
        THROW 50003, 'That login account is already linked to another user.', 1;

    INSERT INTO dbo.Members
        (CompanyId, FullName, JobTitle, DepartmentId, Email, AzureAdObjectId, Signature, ReportingManagerId,
         ApplicationUserId, IsBoardMember, IsManualEntry, IsExternalMember, IsExecutiveManagement, Active,
         InsiderTradingAccess, ConflictOfInterestAccess, RelatedPartyRegisterAccess, RelatedPartyTransactionAccess, RpTransactionRole, CanBeImpersonated,
         CreatedAtUtc, ModifiedAtUtc)
    VALUES
        (@CompanyId, @FullName, @JobTitle, @DepartmentId, @Email, @AzureAdObjectId, @Signature, @ReportingManagerId,
         @ApplicationUserId, @IsBoardMember, @IsManualEntry, @IsExternalMember, @IsExecutiveManagement, @Active,
         @InsiderTradingAccess, @ConflictOfInterestAccess, @RelatedPartyRegisterAccess, @RelatedPartyTransactionAccess, @RpTransactionRole, @CanBeImpersonated,
         SYSUTCDATETIME(), SYSUTCDATETIME());

    SET @NewId = SCOPE_IDENTITY();
END
GO

CREATE OR ALTER PROCEDURE dbo.usp_Member_Update
    @Id int,
    @CompanyId int,
    @FullName nvarchar(120),
    @JobTitle nvarchar(120) = NULL,
    @DepartmentId int = NULL,
    @Email nvarchar(160) = NULL,
    @AzureAdObjectId nvarchar(80) = NULL,
    @Signature nvarchar(160) = NULL,
    @ReportingManagerId int = NULL,
    @ApplicationUserId nvarchar(450) = NULL,
    @IsBoardMember bit = 0,
    @IsManualEntry bit = 0,
    @IsExternalMember bit = 0,
    @IsExecutiveManagement bit = 0,
    @Active bit = 1,
    @InsiderTradingAccess bit = 0,
    @ConflictOfInterestAccess bit = 0,
    @RelatedPartyRegisterAccess bit = 0,
    @RelatedPartyTransactionAccess bit = 0,
    @RpTransactionRole int = 0,
    @CanBeImpersonated bit = 0
AS
BEGIN
    SET NOCOUNT ON;

    IF @ApplicationUserId IS NOT NULL AND EXISTS (SELECT 1 FROM dbo.Members WHERE ApplicationUserId = @ApplicationUserId AND Id <> @Id)
        THROW 50003, 'That login account is already linked to another user.', 1;

    -- Reporting-manager cycle check: walk up the chain from @ReportingManagerId; if we ever reach @Id, reject.
    -- (A CTE can't be nested directly inside an EXISTS(...) subquery -- WITH must lead its own
    -- statement -- so the walk result is captured into a variable first, then checked separately.)
    IF @ReportingManagerId IS NOT NULL
    BEGIN
        DECLARE @CreatesCycle bit = 0;

        ;WITH Chain AS (
            SELECT Id, ReportingManagerId, 1 AS Depth
            FROM dbo.Members WHERE Id = @ReportingManagerId
            UNION ALL
            SELECT m.Id, m.ReportingManagerId, c.Depth + 1
            FROM dbo.Members m
            JOIN Chain c ON m.Id = c.ReportingManagerId
            WHERE c.Depth < 1000
        )
        SELECT @CreatesCycle = 1 FROM Chain WHERE Id = @Id;

        IF @CreatesCycle = 1
            THROW 50004, 'That reporting manager would create a cycle in the reporting chain.', 1;
    END

    UPDATE dbo.Members
    SET CompanyId = @CompanyId,
        FullName = @FullName,
        JobTitle = @JobTitle,
        DepartmentId = @DepartmentId,
        Email = @Email,
        AzureAdObjectId = @AzureAdObjectId,
        Signature = @Signature,
        ReportingManagerId = @ReportingManagerId,
        ApplicationUserId = @ApplicationUserId,
        IsBoardMember = @IsBoardMember,
        IsManualEntry = @IsManualEntry,
        IsExternalMember = @IsExternalMember,
        IsExecutiveManagement = @IsExecutiveManagement,
        Active = @Active,
        InsiderTradingAccess = @InsiderTradingAccess,
        ConflictOfInterestAccess = @ConflictOfInterestAccess,
        RelatedPartyRegisterAccess = @RelatedPartyRegisterAccess,
        RelatedPartyTransactionAccess = @RelatedPartyTransactionAccess,
        RpTransactionRole = @RpTransactionRole,
        CanBeImpersonated = @CanBeImpersonated,
        ModifiedAtUtc = SYSUTCDATETIME()
    WHERE Id = @Id;
END
GO

CREATE OR ALTER PROCEDURE dbo.usp_Member_SetActive
    @Id int,
    @Active bit
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE dbo.Members SET Active = @Active, ModifiedAtUtc = SYSUTCDATETIME() WHERE Id = @Id;
END
GO

-- =========================== MemberImpersonationApproval ===========================

CREATE OR ALTER PROCEDURE dbo.usp_MemberImpersonationApproval_Grant
    @MemberId int,
    @ImpersonatorId int
AS
BEGIN
    SET NOCOUNT ON;

    IF NOT EXISTS (SELECT 1 FROM dbo.MemberImpersonationApprovals WHERE MemberId = @MemberId AND ImpersonatorId = @ImpersonatorId)
        INSERT INTO dbo.MemberImpersonationApprovals (MemberId, ImpersonatorId) VALUES (@MemberId, @ImpersonatorId);
END
GO

CREATE OR ALTER PROCEDURE dbo.usp_MemberImpersonationApproval_Revoke
    @MemberId int,
    @ImpersonatorId int
AS
BEGIN
    SET NOCOUNT ON;
    DELETE FROM dbo.MemberImpersonationApprovals WHERE MemberId = @MemberId AND ImpersonatorId = @ImpersonatorId;
END
GO

-- =========================== Transaction ===========================

CREATE OR ALTER PROCEDURE dbo.usp_Transaction_SetStatus
    @Id int,
    @Status int
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE dbo.Transactions SET Status = @Status WHERE Id = @Id;
END
GO

-- =========================== DeclarationSetup ===========================

CREATE OR ALTER PROCEDURE dbo.usp_DeclarationSetup_Insert
    @Type int,
    @Date date,
    @DueDate date,
    @Active bit = 1,
    @NewId int OUTPUT
AS
BEGIN
    SET NOCOUNT ON;

    IF @DueDate < @Date
        THROW 50007, 'Due date must be on or after the declaration date.', 1;

    INSERT INTO dbo.DeclarationSetups (Type, Date, DueDate, Active) VALUES (@Type, @Date, @DueDate, @Active);
    SET @NewId = SCOPE_IDENTITY();
END
GO

CREATE OR ALTER PROCEDURE dbo.usp_DeclarationSetup_Update
    @Id int,
    @Type int,
    @Date date,
    @DueDate date,
    @Active bit
AS
BEGIN
    SET NOCOUNT ON;

    IF @DueDate < @Date
        THROW 50007, 'Due date must be on or after the declaration date.', 1;

    UPDATE dbo.DeclarationSetups SET Type = @Type, Date = @Date, DueDate = @DueDate, Active = @Active WHERE Id = @Id;
END
GO

CREATE OR ALTER PROCEDURE dbo.usp_DeclarationSetup_SetLastReminderSentUtc
    @Id int,
    @LastReminderSentUtc datetime2
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE dbo.DeclarationSetups SET LastReminderSentUtc = @LastReminderSentUtc WHERE Id = @Id;
END
GO

-- =========================== DeclarationSubmission ===========================

CREATE OR ALTER PROCEDURE dbo.usp_DeclarationSubmission_Insert
    @MemberId int,
    @DeclarationSetupId int,
    @NewId int OUTPUT
AS
BEGIN
    SET NOCOUNT ON;

    IF EXISTS (SELECT 1 FROM dbo.DeclarationSubmissions WHERE MemberId = @MemberId AND DeclarationSetupId = @DeclarationSetupId)
        THROW 50005, 'That declaration was already submitted.', 1;

    INSERT INTO dbo.DeclarationSubmissions (MemberId, DeclarationSetupId, SubmittedAtUtc)
    VALUES (@MemberId, @DeclarationSetupId, SYSUTCDATETIME());

    SET @NewId = SCOPE_IDENTITY();
END
GO

-- =========================== ScheduledActivity / ScheduledActivityDate ===========================

-- Idempotent "ensure a row of this Type exists" for the type-filtered Schedule Activities views
-- (Blackout/Outages/etc.) -- there's no general "add activity" UI since each Type is meant to be a
-- singleton; this creates it (inactive, no dates) the first time that type's page is opened.
CREATE OR ALTER PROCEDURE dbo.usp_ScheduledActivity_EnsureExists
    @Type int,
    @NewId int OUTPUT
AS
BEGIN
    SET NOCOUNT ON;

    SELECT @NewId = Id FROM dbo.ScheduledActivities WHERE Type = @Type;
    IF @NewId IS NULL
    BEGIN
        INSERT INTO dbo.ScheduledActivities (Type, Active) VALUES (@Type, 0);
        SET @NewId = SCOPE_IDENTITY();
    END
END
GO

CREATE OR ALTER PROCEDURE dbo.usp_ScheduledActivity_SetActive
    @Id int,
    @Active bit
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE dbo.ScheduledActivities SET Active = @Active WHERE Id = @Id;
END
GO

CREATE OR ALTER PROCEDURE dbo.usp_ScheduledActivityDate_Insert
    @ScheduledActivityId int,
    @Month int,
    @Day int,
    @NewId int OUTPUT
AS
BEGIN
    SET NOCOUNT ON;

    IF EXISTS (SELECT 1 FROM dbo.ScheduledActivityDates WHERE ScheduledActivityId = @ScheduledActivityId AND Month = @Month AND Day = @Day)
        THROW 50006, 'That trigger date is already configured for this activity.', 1;

    INSERT INTO dbo.ScheduledActivityDates (ScheduledActivityId, Month, Day) VALUES (@ScheduledActivityId, @Month, @Day);
    SET @NewId = SCOPE_IDENTITY();
END
GO

CREATE OR ALTER PROCEDURE dbo.usp_ScheduledActivityDate_Delete
    @Id int
AS
BEGIN
    SET NOCOUNT ON;
    DELETE FROM dbo.ScheduledActivityDates WHERE Id = @Id;
END
GO

CREATE OR ALTER PROCEDURE dbo.usp_ScheduledActivityDate_SetLastTriggered
    @Id int,
    @LastTriggeredUtc datetime2
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE dbo.ScheduledActivityDates SET LastTriggeredUtc = @LastTriggeredUtc WHERE Id = @Id;
END
GO

-- =========================== AuditLogEntry ===========================

CREATE OR ALTER PROCEDURE dbo.usp_AuditLogEntry_Insert
    @ActorDisplayName nvarchar(160),
    @ActingOnBehalfOf nvarchar(160) = NULL,
    @Action int,
    @EntityType nvarchar(80),
    @EntityId nvarchar(80) = NULL,
    @Details nvarchar(2000) = NULL,
    @Justification nvarchar(1000) = NULL,
    @IpAddress nvarchar(64) = NULL,
    @UserAgent nvarchar(400) = NULL,
    @NewId int OUTPUT
AS
BEGIN
    SET NOCOUNT ON;

    INSERT INTO dbo.AuditLogEntries
        (ActorDisplayName, ActingOnBehalfOf, Action, EntityType, EntityId, Details, Justification, IpAddress, UserAgent, OccurredAtUtc)
    VALUES
        (@ActorDisplayName, @ActingOnBehalfOf, @Action, @EntityType, @EntityId, @Details, @Justification, @IpAddress, @UserAgent, SYSUTCDATETIME());

    SET @NewId = SCOPE_IDENTITY();
END
GO

-- =========================== MemberNotification (FRD Sec. 2.2 "notify user?" prompt) ===========================

CREATE OR ALTER PROCEDURE dbo.usp_MemberNotification_Insert
    @MemberId int,
    @Recipient nvarchar(160),
    @Status int,
    @NewId int OUTPUT
AS
BEGIN
    SET NOCOUNT ON;

    INSERT INTO dbo.MemberNotifications (MemberId, Recipient, Status, CreatedAtUtc)
    VALUES (@MemberId, @Recipient, @Status, SYSUTCDATETIME());

    SET @NewId = SCOPE_IDENTITY();
END
GO

CREATE OR ALTER PROCEDURE dbo.usp_MemberNotification_SetStatus
    @Id int,
    @Status int,
    @SentAtUtc datetime2 = NULL
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE dbo.MemberNotifications SET Status = @Status, SentAtUtc = @SentAtUtc WHERE Id = @Id;
END
GO

-- =========================== DeclarationCycleSetup / DeclarationCycleRun (Data Management > Declarations Setup, 31-Jul-2026 requirement note) ===========================

CREATE OR ALTER PROCEDURE dbo.usp_DeclarationCycleSetup_EnsureExists
    @Type int,
    @NewId int OUTPUT
AS
BEGIN
    SET NOCOUNT ON;

    SELECT @NewId = Id FROM dbo.DeclarationCycleSetups WHERE Type = @Type;
    IF @NewId IS NULL
    BEGIN
        INSERT INTO dbo.DeclarationCycleSetups
            (Type, ReminderCount, ReminderFrequency, EmailSubject, EmailBody, CreatedAtUtc, ModifiedAtUtc)
        VALUES
            (@Type, 0, 1 /* Weekly */, '', '', SYSUTCDATETIME(), SYSUTCDATETIME());
        SET @NewId = SCOPE_IDENTITY();
    END
END
GO

CREATE OR ALTER PROCEDURE dbo.usp_DeclarationCycleSetup_Update
    @Id int,
    @ReminderCount int,
    @ReminderFrequency int,
    @EmailSubject nvarchar(200),
    @EmailBody nvarchar(max)
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE dbo.DeclarationCycleSetups
    SET ReminderCount = @ReminderCount,
        ReminderFrequency = @ReminderFrequency,
        EmailSubject = @EmailSubject,
        EmailBody = @EmailBody,
        ModifiedAtUtc = SYSUTCDATETIME()
    WHERE Id = @Id;
END
GO

-- @SentAtUtc is either "now" (Send Now) or a future date at 08:00 (Schedule Send, @Sent = 0 until the
-- hosted service fires it and calls usp_DeclarationCycleRun_MarkSent).
CREATE OR ALTER PROCEDURE dbo.usp_DeclarationCycleRun_Insert
    @DeclarationCycleSetupId int,
    @Type int,
    @CompanyId int = NULL,
    @SentAtUtc datetime2,
    @PeriodYear int,
    @PeriodQuarter int,
    @DueDateUtc datetime2,
    @Sent bit,
    @RecipientCount int,
    @NewId int OUTPUT
AS
BEGIN
    SET NOCOUNT ON;

    INSERT INTO dbo.DeclarationCycleRuns
        (DeclarationCycleSetupId, Type, CompanyId, SentAtUtc, PeriodYear, PeriodQuarter, DueDateUtc, Sent, RecipientCount, RemindersSent)
    VALUES
        (@DeclarationCycleSetupId, @Type, @CompanyId, @SentAtUtc, @PeriodYear, @PeriodQuarter, @DueDateUtc, @Sent, @RecipientCount, 0);

    SET @NewId = SCOPE_IDENTITY();
END
GO

-- Transitions a scheduled (Sent = 0) run to actually sent, once the hosted service has resolved
-- recipients and sent the notification at its target time.
CREATE OR ALTER PROCEDURE dbo.usp_DeclarationCycleRun_MarkSent
    @Id int,
    @SentAtUtc datetime2,
    @RecipientCount int
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE dbo.DeclarationCycleRuns
    SET Sent = 1, SentAtUtc = @SentAtUtc, RecipientCount = @RecipientCount
    WHERE Id = @Id;
END
GO

-- Lets an administrator push out (or pull in) a run's due date after the fact -- e.g. an extension
-- granted after the original due date, without needing to cancel/resend the whole notification.
CREATE OR ALTER PROCEDURE dbo.usp_DeclarationCycleRun_UpdateDueDate
    @Id int,
    @DueDateUtc datetime2
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE dbo.DeclarationCycleRuns
    SET DueDateUtc = @DueDateUtc
    WHERE Id = @Id;
END
GO

-- Cancels a scheduled (Sent = 0) run before it fires. Deleting is safe here since a not-yet-sent run
-- has no recipient rows and nothing else references it.
CREATE OR ALTER PROCEDURE dbo.usp_DeclarationCycleRun_Delete
    @Id int
AS
BEGIN
    SET NOCOUNT ON;
    DELETE FROM dbo.DeclarationCycleRuns WHERE Id = @Id AND Sent = 0;
END
GO

-- Recalls a Sent run (admin action, only offered in the UI while it has zero submissions). Kept as
-- a row (not deleted) so History still shows it happened; Recalled=1 is what every "due" query and
-- the reminder loop check to stop treating it as pending.
CREATE OR ALTER PROCEDURE dbo.usp_DeclarationCycleRun_Recall
    @Id int,
    @RecalledAtUtc datetime2,
    @RecalledByName nvarchar(160)
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE dbo.DeclarationCycleRuns
    SET Recalled = 1, RecalledAtUtc = @RecalledAtUtc, RecalledByName = @RecalledByName
    WHERE Id = @Id AND Sent = 1;
END
GO

CREATE OR ALTER PROCEDURE dbo.usp_DeclarationCycleRun_IncrementReminder
    @Id int,
    @LastReminderSentUtc datetime2
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE dbo.DeclarationCycleRuns
    SET RemindersSent = RemindersSent + 1, LastReminderSentUtc = @LastReminderSentUtc
    WHERE Id = @Id;
END
GO

CREATE OR ALTER PROCEDURE dbo.usp_DeclarationCycleRunRecipient_Insert
    @DeclarationCycleRunId int,
    @MemberId int,
    @MemberName nvarchar(120),
    @Email nvarchar(160),
    @CompanyName nvarchar(120) = NULL,
    @Success bit,
    @NewId int OUTPUT
AS
BEGIN
    SET NOCOUNT ON;

    INSERT INTO dbo.DeclarationCycleRunRecipients (DeclarationCycleRunId, MemberId, MemberName, Email, CompanyName, SentAtUtc, Success)
    VALUES (@DeclarationCycleRunId, @MemberId, @MemberName, @Email, @CompanyName, SYSUTCDATETIME(), @Success);

    SET @NewId = SCOPE_IDENTITY();
END
GO

-- =========================== InsiderDeclaration / InsiderDeclarationRelative (FRD §7 Insider Trading Declaration business process) ===========================

CREATE OR ALTER PROCEDURE dbo.usp_InsiderDeclaration_Insert
    @MemberId int,
    @DeclarationCycleRunId int,
    @HasNin bit,
    @NinNumber nvarchar(40) = NULL,
    @HoldsShares bit,
    @SharesNinNumber nvarchar(40) = NULL,
    @NumberOfSharesHeld int = NULL,
    @RelativesHoldShares bit,
    @RelativesHaveNin bit = 0,
    @IsDraft bit = 0,
    @EmiratesIdPath nvarchar(260) = NULL,
    @EmiratesIdNumber nvarchar(40) = NULL,
    @EmiratesIdNameOnCard nvarchar(120) = NULL,
    @EmiratesIdExpiryDate date = NULL,
    @PassportPath nvarchar(260) = NULL,
    @PassportNumber nvarchar(40) = NULL,
    @PassportExpiryDate date = NULL,
    @PassportIssuingCountry nvarchar(80) = NULL,
    @TradeLicencePath nvarchar(260) = NULL,
    @TradeLicenceNumber nvarchar(40) = NULL,
    @TradeLicenceLegalName nvarchar(160) = NULL,
    @TradeLicenceIssuingAuthority nvarchar(160) = NULL,
    @TradeLicenceExpiryDate date = NULL,
    @OtherDocumentPath nvarchar(260) = NULL,
    @SubmittedByName nvarchar(160),
    @SubmittedOnBehalfOf nvarchar(160) = NULL,
    @NewId int OUTPUT
AS
BEGIN
    SET NOCOUNT ON;

    IF EXISTS (SELECT 1 FROM dbo.InsiderDeclarations WHERE MemberId = @MemberId AND DeclarationCycleRunId = @DeclarationCycleRunId)
        THROW 50011, 'An Insider Trading declaration was already submitted for this cycle.', 1;

    INSERT INTO dbo.InsiderDeclarations
        (MemberId, DeclarationCycleRunId, HasNin, NinNumber, HoldsShares, SharesNinNumber, NumberOfSharesHeld,
         RelativesHoldShares, RelativesHaveNin, IsDraft,
         EmiratesIdPath, EmiratesIdNumber, EmiratesIdNameOnCard, EmiratesIdExpiryDate,
         PassportPath, PassportNumber, PassportExpiryDate, PassportIssuingCountry,
         TradeLicencePath, TradeLicenceNumber, TradeLicenceLegalName, TradeLicenceIssuingAuthority, TradeLicenceExpiryDate,
         OtherDocumentPath, SubmittedAtUtc, SubmittedByName, SubmittedOnBehalfOf)
    VALUES
        (@MemberId, @DeclarationCycleRunId, @HasNin, @NinNumber, @HoldsShares, @SharesNinNumber, @NumberOfSharesHeld,
         @RelativesHoldShares, @RelativesHaveNin, @IsDraft,
         @EmiratesIdPath, @EmiratesIdNumber, @EmiratesIdNameOnCard, @EmiratesIdExpiryDate,
         @PassportPath, @PassportNumber, @PassportExpiryDate, @PassportIssuingCountry,
         @TradeLicencePath, @TradeLicenceNumber, @TradeLicenceLegalName, @TradeLicenceIssuingAuthority, @TradeLicenceExpiryDate,
         @OtherDocumentPath, SYSUTCDATETIME(), @SubmittedByName, @SubmittedOnBehalfOf);

    SET @NewId = SCOPE_IDENTITY();
END
GO

CREATE OR ALTER PROCEDURE dbo.usp_InsiderDeclarationRelative_Insert
    @InsiderDeclarationId int,
    @NinNumber nvarchar(40) = NULL,
    @RelativeName nvarchar(120),
    @Relationship int,
    @NumberOfShares int,
    @Additional nvarchar(400) = NULL,
    @IsSelf bit = 0,
    @NewId int OUTPUT
AS
BEGIN
    SET NOCOUNT ON;

    INSERT INTO dbo.InsiderDeclarationRelatives (InsiderDeclarationId, NinNumber, RelativeName, Relationship, NumberOfShares, Additional, IsSelf)
    VALUES (@InsiderDeclarationId, @NinNumber, @RelativeName, @Relationship, @NumberOfShares, @Additional, @IsSelf);

    SET @NewId = SCOPE_IDENTITY();
END
GO

-- Functional Spec §3.2 "Relatives' NIN" grid rows -- distinct from InsiderDeclarationRelatives,
-- which records relatives holding DI shares rather than relatives simply holding a NIN.
CREATE OR ALTER PROCEDURE dbo.usp_InsiderDeclarationNinHolder_Insert
    @InsiderDeclarationId int,
    @Relationship int,
    @NameOfShareHolder nvarchar(120),
    @NinNumber nvarchar(40),
    @Additional nvarchar(400) = NULL,
    @NewId int OUTPUT
AS
BEGIN
    SET NOCOUNT ON;

    INSERT INTO dbo.InsiderDeclarationNinHolders (InsiderDeclarationId, Relationship, NameOfShareHolder, NinNumber, Additional)
    VALUES (@InsiderDeclarationId, @Relationship, @NameOfShareHolder, @NinNumber, @Additional);

    SET @NewId = SCOPE_IDENTITY();
END
GO

-- Editing is allowed up to the cycle's due date (FRD §7.4) -- the page enforces the due-date check;
-- this proc just overwrites the header fields and stamps ModifiedAtUtc.
CREATE OR ALTER PROCEDURE dbo.usp_InsiderDeclaration_Update
    @Id int,
    @HasNin bit,
    @NinNumber nvarchar(40) = NULL,
    @HoldsShares bit,
    @SharesNinNumber nvarchar(40) = NULL,
    @NumberOfSharesHeld int = NULL,
    @RelativesHoldShares bit,
    @RelativesHaveNin bit = 0,
    @IsDraft bit = 0,
    @EmiratesIdPath nvarchar(260) = NULL,
    @EmiratesIdNumber nvarchar(40) = NULL,
    @EmiratesIdNameOnCard nvarchar(120) = NULL,
    @EmiratesIdExpiryDate date = NULL,
    @PassportPath nvarchar(260) = NULL,
    @PassportNumber nvarchar(40) = NULL,
    @PassportExpiryDate date = NULL,
    @PassportIssuingCountry nvarchar(80) = NULL,
    @TradeLicencePath nvarchar(260) = NULL,
    @TradeLicenceNumber nvarchar(40) = NULL,
    @TradeLicenceLegalName nvarchar(160) = NULL,
    @TradeLicenceIssuingAuthority nvarchar(160) = NULL,
    @TradeLicenceExpiryDate date = NULL,
    @OtherDocumentPath nvarchar(260) = NULL,
    @SubmittedByName nvarchar(160),
    @SubmittedOnBehalfOf nvarchar(160) = NULL
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE dbo.InsiderDeclarations
    SET HasNin = @HasNin,
        NinNumber = @NinNumber,
        HoldsShares = @HoldsShares,
        SharesNinNumber = @SharesNinNumber,
        NumberOfSharesHeld = @NumberOfSharesHeld,
        RelativesHoldShares = @RelativesHoldShares,
        RelativesHaveNin = @RelativesHaveNin,
        IsDraft = @IsDraft,
        EmiratesIdPath = @EmiratesIdPath,
        EmiratesIdNumber = @EmiratesIdNumber,
        EmiratesIdNameOnCard = @EmiratesIdNameOnCard,
        EmiratesIdExpiryDate = @EmiratesIdExpiryDate,
        PassportPath = @PassportPath,
        PassportNumber = @PassportNumber,
        PassportExpiryDate = @PassportExpiryDate,
        PassportIssuingCountry = @PassportIssuingCountry,
        TradeLicencePath = @TradeLicencePath,
        TradeLicenceNumber = @TradeLicenceNumber,
        TradeLicenceLegalName = @TradeLicenceLegalName,
        TradeLicenceIssuingAuthority = @TradeLicenceIssuingAuthority,
        TradeLicenceExpiryDate = @TradeLicenceExpiryDate,
        OtherDocumentPath = @OtherDocumentPath,
        SubmittedByName = @SubmittedByName,
        SubmittedOnBehalfOf = @SubmittedOnBehalfOf,
        ModifiedAtUtc = SYSUTCDATETIME()
    WHERE Id = @Id;
END
GO

-- Relatives are fully replaced on every edit (delete then re-insert) rather than diffed, since the
-- editor always resubmits the complete current list. Per the Functional Spec §4 compliance
-- requirement, the page (not this proc) writes an audit-log entry for every individual row add/edit/
-- remove as it happens, so the full history survives even though the storage itself is snapshot-style.
CREATE OR ALTER PROCEDURE dbo.usp_InsiderDeclarationRelative_DeleteByDeclaration
    @InsiderDeclarationId int
AS
BEGIN
    SET NOCOUNT ON;
    DELETE FROM dbo.InsiderDeclarationRelatives WHERE InsiderDeclarationId = @InsiderDeclarationId;
END
GO

CREATE OR ALTER PROCEDURE dbo.usp_InsiderDeclarationNinHolder_DeleteByDeclaration
    @InsiderDeclarationId int
AS
BEGIN
    SET NOCOUNT ON;
    DELETE FROM dbo.InsiderDeclarationNinHolders WHERE InsiderDeclarationId = @InsiderDeclarationId;
END
GO

-- =========================== RelatedPartyCoiDeclaration ===========================

CREATE OR ALTER PROCEDURE dbo.usp_RelatedPartyCoiDeclaration_Insert
    @MemberId int,
    @DeclarationCycleRunId int,
    @NothingToDeclareRelatives bit = 0,
    @NothingToDeclareSelfOwned bit = 0,
    @NothingToDeclareRelativeOwned bit = 0,
    @NothingToDeclareBoardRoles bit = 0,
    @NothingToDeclareConflicts bit = 0,
    @IsDraft bit = 0,
    @AttestationName nvarchar(160),
    @AttestationConfirmed bit = 0,
    @SubmittedByName nvarchar(160),
    @SubmittedOnBehalfOf nvarchar(160) = NULL,
    @NewId int OUTPUT
AS
BEGIN
    SET NOCOUNT ON;

    IF EXISTS (SELECT 1 FROM dbo.RelatedPartyCoiDeclarations WHERE MemberId = @MemberId AND DeclarationCycleRunId = @DeclarationCycleRunId)
        THROW 50012, 'A Related Party & COI declaration was already submitted for this cycle.', 1;

    INSERT INTO dbo.RelatedPartyCoiDeclarations
        (MemberId, DeclarationCycleRunId, NothingToDeclareRelatives, NothingToDeclareSelfOwned,
         NothingToDeclareRelativeOwned, NothingToDeclareBoardRoles, NothingToDeclareConflicts, IsDraft,
         AttestationName, AttestationConfirmed, SubmittedAtUtc, SubmittedByName, SubmittedOnBehalfOf)
    VALUES
        (@MemberId, @DeclarationCycleRunId, @NothingToDeclareRelatives, @NothingToDeclareSelfOwned,
         @NothingToDeclareRelativeOwned, @NothingToDeclareBoardRoles, @NothingToDeclareConflicts, @IsDraft,
         @AttestationName, @AttestationConfirmed, SYSUTCDATETIME(), @SubmittedByName, @SubmittedOnBehalfOf);

    SET @NewId = SCOPE_IDENTITY();
END
GO

CREATE OR ALTER PROCEDURE dbo.usp_RelatedPartyCoiDeclaration_Update
    @Id int,
    @NothingToDeclareRelatives bit = 0,
    @NothingToDeclareSelfOwned bit = 0,
    @NothingToDeclareRelativeOwned bit = 0,
    @NothingToDeclareBoardRoles bit = 0,
    @NothingToDeclareConflicts bit = 0,
    @IsDraft bit = 0,
    @AttestationName nvarchar(160),
    @AttestationConfirmed bit = 0,
    @SubmittedByName nvarchar(160),
    @SubmittedOnBehalfOf nvarchar(160) = NULL
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE dbo.RelatedPartyCoiDeclarations
    SET NothingToDeclareRelatives = @NothingToDeclareRelatives,
        NothingToDeclareSelfOwned = @NothingToDeclareSelfOwned,
        NothingToDeclareRelativeOwned = @NothingToDeclareRelativeOwned,
        NothingToDeclareBoardRoles = @NothingToDeclareBoardRoles,
        NothingToDeclareConflicts = @NothingToDeclareConflicts,
        IsDraft = @IsDraft,
        AttestationName = @AttestationName,
        AttestationConfirmed = @AttestationConfirmed,
        SubmittedByName = @SubmittedByName,
        SubmittedOnBehalfOf = @SubmittedOnBehalfOf,
        ModifiedAtUtc = SYSUTCDATETIME()
    WHERE Id = @Id;
END
GO

-- Set independently from usp_RelatedPartyCoiDeclaration_Update: fired from the /uaepass/callback
-- endpoint after the declarant completes UAE PASS login, which runs outside the wizard's normal
-- save flow and must not touch (or require re-sending) the rest of the declaration's fields.
CREATE OR ALTER PROCEDURE dbo.usp_RelatedPartyCoiDeclaration_SetUaePassVerification
    @Id int,
    @UaePassVerifiedAtUtc datetime2,
    @UaePassVerifiedName nvarchar(160),
    @UaePassUuid nvarchar(120)
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE dbo.RelatedPartyCoiDeclarations
    SET UaePassVerifiedAtUtc = @UaePassVerifiedAtUtc,
        UaePassVerifiedName = @UaePassVerifiedName,
        UaePassUuid = @UaePassUuid
    WHERE Id = @Id;
END
GO

CREATE OR ALTER PROCEDURE dbo.usp_CoiRelative_Insert
    @RelatedPartyCoiDeclarationId int,
    @Name nvarchar(120),
    @Relationship int,
    @NewId int OUTPUT
AS
BEGIN
    SET NOCOUNT ON;

    INSERT INTO dbo.CoiRelatives (RelatedPartyCoiDeclarationId, Name, Relationship)
    VALUES (@RelatedPartyCoiDeclarationId, @Name, @Relationship);

    SET @NewId = SCOPE_IDENTITY();
END
GO

-- Rows are fully replaced on every edit (delete then re-insert), same snapshot-style pattern as
-- InsiderDeclarationRelative -- the editor always resubmits the complete current list.
CREATE OR ALTER PROCEDURE dbo.usp_CoiRelative_DeleteByDeclaration
    @RelatedPartyCoiDeclarationId int
AS
BEGIN
    SET NOCOUNT ON;
    DELETE FROM dbo.CoiRelatives WHERE RelatedPartyCoiDeclarationId = @RelatedPartyCoiDeclarationId;
END
GO

CREATE OR ALTER PROCEDURE dbo.usp_CoiCompanyEntry_Insert
    @RelatedPartyCoiDeclarationId int,
    @OwnerType int,
    @CoiRelativeId int = NULL,
    @LegalCompanyName nvarchar(160),
    @PrincipalBusinessActivity nvarchar(400) = NULL,
    @TradeLicenseNumber nvarchar(40) = NULL,
    @TradeLicenseExpiryDate date = NULL,
    @LicenseActivities nvarchar(400) = NULL,
    @NewId int OUTPUT
AS
BEGIN
    SET NOCOUNT ON;

    INSERT INTO dbo.CoiCompanyEntries
        (RelatedPartyCoiDeclarationId, OwnerType, CoiRelativeId, LegalCompanyName,
         PrincipalBusinessActivity, TradeLicenseNumber, TradeLicenseExpiryDate, LicenseActivities)
    VALUES
        (@RelatedPartyCoiDeclarationId, @OwnerType, @CoiRelativeId, @LegalCompanyName,
         @PrincipalBusinessActivity, @TradeLicenseNumber, @TradeLicenseExpiryDate, @LicenseActivities);

    SET @NewId = SCOPE_IDENTITY();
END
GO

-- Documents are deleted explicitly here (not left to FK cascade) so this stays auditable/consistent
-- with the rest of this script, which never relies on implicit cascade behavior.
CREATE OR ALTER PROCEDURE dbo.usp_CoiCompanyEntry_DeleteByDeclaration
    @RelatedPartyCoiDeclarationId int
AS
BEGIN
    SET NOCOUNT ON;

    DELETE d FROM dbo.CoiTradeLicenseDocuments d
    JOIN dbo.CoiCompanyEntries c ON c.Id = d.CoiCompanyEntryId
    WHERE c.RelatedPartyCoiDeclarationId = @RelatedPartyCoiDeclarationId;

    DELETE FROM dbo.CoiCompanyEntries WHERE RelatedPartyCoiDeclarationId = @RelatedPartyCoiDeclarationId;
END
GO

CREATE OR ALTER PROCEDURE dbo.usp_CoiTradeLicenseDocument_Insert
    @CoiCompanyEntryId int,
    @FilePath nvarchar(260),
    @FileName nvarchar(160),
    @NewId int OUTPUT
AS
BEGIN
    SET NOCOUNT ON;

    INSERT INTO dbo.CoiTradeLicenseDocuments (CoiCompanyEntryId, FilePath, FileName, UploadedAtUtc)
    VALUES (@CoiCompanyEntryId, @FilePath, @FileName, SYSUTCDATETIME());

    SET @NewId = SCOPE_IDENTITY();
END
GO

CREATE OR ALTER PROCEDURE dbo.usp_CoiConflictEntry_Insert
    @RelatedPartyCoiDeclarationId int,
    @CompanyOrCounterpartyName nvarchar(160),
    @PrincipalBusinessActivity nvarchar(400) = NULL,
    @NatureOfHolding nvarchar(1000) = NULL,
    @NewId int OUTPUT
AS
BEGIN
    SET NOCOUNT ON;

    INSERT INTO dbo.CoiConflictEntries (RelatedPartyCoiDeclarationId, CompanyOrCounterpartyName, PrincipalBusinessActivity, NatureOfHolding)
    VALUES (@RelatedPartyCoiDeclarationId, @CompanyOrCounterpartyName, @PrincipalBusinessActivity, @NatureOfHolding);

    SET @NewId = SCOPE_IDENTITY();
END
GO

CREATE OR ALTER PROCEDURE dbo.usp_CoiConflictEntry_DeleteByDeclaration
    @RelatedPartyCoiDeclarationId int
AS
BEGIN
    SET NOCOUNT ON;
    DELETE FROM dbo.CoiConflictEntries WHERE RelatedPartyCoiDeclarationId = @RelatedPartyCoiDeclarationId;
END
GO

-- =========================== PolicyDocumentVersion ===========================

-- One row per replaced policy.pdf -- FilePath points at the backup copy made just before the
-- overwrite (see Admin > Settings), so this is the complete change log/version history, not just
-- an audit note.
CREATE OR ALTER PROCEDURE dbo.usp_PolicyDocumentVersion_Insert
    @FilePath nvarchar(260),
    @OriginalFileName nvarchar(260),
    @UploadedByName nvarchar(160),
    @NewId int OUTPUT
AS
BEGIN
    SET NOCOUNT ON;

    INSERT INTO dbo.PolicyDocumentVersions (FilePath, OriginalFileName, UploadedAtUtc, UploadedByName)
    VALUES (@FilePath, @OriginalFileName, SYSUTCDATETIME(), @UploadedByName);

    SET @NewId = SCOPE_IDENTITY();
END
GO

-- =========================== NavMenuItemOrder ===========================

-- Upsert, not plain insert: Settings > Menu Order calls this once per item on every Move Up/Down,
-- and most items already have a row from the first time they were touched.
CREATE OR ALTER PROCEDURE dbo.usp_NavMenuItemOrder_Upsert
    @ItemKey nvarchar(80),
    @SortOrder int
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE dbo.NavMenuItemOrders SET SortOrder = @SortOrder WHERE ItemKey = @ItemKey;
    IF @@ROWCOUNT = 0
        INSERT INTO dbo.NavMenuItemOrders (ItemKey, SortOrder) VALUES (@ItemKey, @SortOrder);
END
GO

-- =========================== NavMenuItemLabel ===========================

-- Upsert, not plain insert: Settings > Menu Order calls this once per rename, and a previously
-- renamed item already has a row.
CREATE OR ALTER PROCEDURE dbo.usp_NavMenuItemLabel_Upsert
    @ItemKey nvarchar(80),
    @CustomLabel nvarchar(120)
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE dbo.NavMenuItemLabels SET CustomLabel = @CustomLabel WHERE ItemKey = @ItemKey;
    IF @@ROWCOUNT = 0
        INSERT INTO dbo.NavMenuItemLabels (ItemKey, CustomLabel) VALUES (@ItemKey, @CustomLabel);
END
GO

-- Reverts an item back to its catalog default label by removing the override row entirely.
CREATE OR ALTER PROCEDURE dbo.usp_NavMenuItemLabel_Delete
    @ItemKey nvarchar(80)
AS
BEGIN
    SET NOCOUNT ON;

    DELETE FROM dbo.NavMenuItemLabels WHERE ItemKey = @ItemKey;
END
GO

-- =========================== Investor Relations: Shareholder Register ===========================

-- Table type carrying one full parsed row of an uploaded Share Register .xlsx -- passed as a single
-- table-valued parameter to usp_ShareholderRecord_BulkInsert so a whole upload (thousands of rows)
-- is one set-based INSERT instead of one round trip per row.
--
-- A table type can't be ALTERed, and can't be dropped while a procedure still references it as a
-- parameter type -- so widening it (e.g. adding LinkedNinsReference/LinkedNins below) means dropping
-- the dependent procedure first, then the type, then recreating both. That makes this block
-- non-idempotent-by-skip (unlike the plain IF-NOT-EXISTS elsewhere in this script): rerunning it
-- always drops and recreates, which is safe since ShareholderRecords itself is never re-derived from
-- this type -- it's just the upload's transport shape.
IF OBJECT_ID(N'dbo.usp_ShareholderRecord_BulkInsert', N'P') IS NOT NULL
    DROP PROCEDURE dbo.usp_ShareholderRecord_BulkInsert;
GO
IF TYPE_ID(N'dbo.ShareholderRecordTableType') IS NOT NULL
    DROP TYPE dbo.ShareholderRecordTableType;
GO
CREATE TYPE dbo.ShareholderRecordTableType AS TABLE
(
    SerialNo int NULL,
    Nin nvarchar(40) NOT NULL,
    CdsUpdated nvarchar(20) NULL,
    Name nvarchar(200) NULL,
    EnglishName nvarchar(200) NULL,
    LifeStatus nvarchar(40) NULL,
    ClientType nvarchar(40) NULL,
    PassportNo nvarchar(40) NULL,
    FamilyId nvarchar(40) NULL,
    NationalId nvarchar(40) NULL,
    VisaNo nvarchar(40) NULL,
    CommercialLicenseNo nvarchar(40) NULL,
    TradeRegistrationNo nvarchar(40) NULL,
    Citizenship nvarchar(20) NULL,
    CitizenshipDescription nvarchar(120) NULL,
    PoBox nvarchar(40) NULL,
    City nvarchar(80) NULL,
    CountryCode nvarchar(20) NULL,
    CountryName nvarchar(120) NULL,
    Address1 nvarchar(240) NULL,
    Address2 nvarchar(240) NULL,
    Address3 nvarchar(240) NULL,
    Phone1 nvarchar(40) NULL,
    Phone2 nvarchar(40) NULL,
    Fax nvarchar(40) NULL,
    Email nvarchar(160) NULL,
    Qty decimal(18,2) NOT NULL,
    QtyPercent decimal(9,4) NOT NULL,
    Frozen decimal(18,2) NOT NULL,
    LastTransDate date NULL,
    PaymentPreference nvarchar(80) NULL,
    LinkedNinsReference nvarchar(40) NULL,
    LinkedNins nvarchar(200) NULL
);
GO

-- One row per upload -- every upload is its own dated snapshot (see ShareholderRegisterUpload doc
-- comment), never overwritten, so this table is both the print-by-as-on-date source and the upload
-- history/change log the Investor Relations spec calls for.
CREATE OR ALTER PROCEDURE dbo.usp_ShareholderRegisterUpload_Insert
    @AsOnDate date,
    @FileName nvarchar(260),
    @RecordCount int,
    @UploadedByName nvarchar(160),
    @NewId int OUTPUT
AS
BEGIN
    SET NOCOUNT ON;

    INSERT INTO dbo.ShareholderRegisterUploads (AsOnDate, FileName, RecordCount, UploadedAtUtc, UploadedByName)
    VALUES (@AsOnDate, @FileName, @RecordCount, SYSUTCDATETIME(), @UploadedByName);

    SET @NewId = SCOPE_IDENTITY();
END
GO

CREATE OR ALTER PROCEDURE dbo.usp_ShareholderRecord_BulkInsert
    @ShareholderRegisterUploadId int,
    @Rows dbo.ShareholderRecordTableType READONLY
AS
BEGIN
    SET NOCOUNT ON;

    INSERT INTO dbo.ShareholderRecords
        (ShareholderRegisterUploadId, SerialNo, Nin, CdsUpdated, Name, EnglishName, LifeStatus, ClientType,
         PassportNo, FamilyId, NationalId, VisaNo, CommercialLicenseNo, TradeRegistrationNo, Citizenship,
         CitizenshipDescription, PoBox, City, CountryCode, CountryName, Address1, Address2, Address3,
         Phone1, Phone2, Fax, Email, Qty, QtyPercent, Frozen, LastTransDate, PaymentPreference,
         LinkedNinsReference, LinkedNins)
    SELECT @ShareholderRegisterUploadId, SerialNo, Nin, CdsUpdated, Name, EnglishName, LifeStatus, ClientType,
         PassportNo, FamilyId, NationalId, VisaNo, CommercialLicenseNo, TradeRegistrationNo, Citizenship,
         CitizenshipDescription, PoBox, City, CountryCode, CountryName, Address1, Address2, Address3,
         Phone1, Phone2, Fax, Email, Qty, QtyPercent, Frozen, LastTransDate, PaymentPreference,
         LinkedNinsReference, LinkedNins
    FROM @Rows;
END
GO

-- =========================== Investor Relations: Shares Trading ===========================

IF TYPE_ID(N'dbo.ShareTradingRecordTableType') IS NULL
BEGIN
    CREATE TYPE dbo.ShareTradingRecordTableType AS TABLE
    (
        ReportDate date NOT NULL,
        Symbol nvarchar(20) NOT NULL,
        Nin nvarchar(40) NOT NULL,
        InvestorName nvarchar(200) NULL,
        ClientType nvarchar(40) NULL,
        Nationality nvarchar(20) NULL,
        PreviousOwnQty decimal(18,2) NOT NULL,
        CurrentOwnQty decimal(18,2) NOT NULL,
        OwnedQtyChange decimal(18,2) NOT NULL
    );
END
GO

CREATE OR ALTER PROCEDURE dbo.usp_ShareTradingUpload_Insert
    @FileName nvarchar(260),
    @RecordCount int,
    @UploadedByName nvarchar(160),
    @NewId int OUTPUT
AS
BEGIN
    SET NOCOUNT ON;

    INSERT INTO dbo.ShareTradingUploads (FileName, RecordCount, UploadedAtUtc, UploadedByName)
    VALUES (@FileName, @RecordCount, SYSUTCDATETIME(), @UploadedByName);

    SET @NewId = SCOPE_IDENTITY();
END
GO

CREATE OR ALTER PROCEDURE dbo.usp_ShareTradingRecord_BulkInsert
    @ShareTradingUploadId int,
    @Rows dbo.ShareTradingRecordTableType READONLY
AS
BEGIN
    SET NOCOUNT ON;

    INSERT INTO dbo.ShareTradingRecords
        (ShareTradingUploadId, ReportDate, Symbol, Nin, InvestorName, ClientType, Nationality,
         PreviousOwnQty, CurrentOwnQty, OwnedQtyChange)
    SELECT @ShareTradingUploadId, ReportDate, Symbol, Nin, InvestorName, ClientType, Nationality,
         PreviousOwnQty, CurrentOwnQty, OwnedQtyChange
    FROM @Rows;
END
GO

-- =========================== Related Party Transaction ===========================

CREATE OR ALTER PROCEDURE dbo.usp_RelatedPartyTransaction_Insert
    @CompanyId int,
    @MemberId int,
    @CounterPartyName nvarchar(200),
    @TransactionValue decimal(18,2),
    @Description nvarchar(4000),
    @DateOfRequest datetime2,
    @Status int,
    @ApproverMemberId int = NULL,
    @EscalationReason int,
    @EscalatedAtUtc datetime2 = NULL,
    @SubmittedByName nvarchar(160),
    @SubmittedOnBehalfOf nvarchar(160) = NULL,
    @NewId int OUTPUT
AS
BEGIN
    SET NOCOUNT ON;

    INSERT INTO dbo.RelatedPartyTransactions
        (CompanyId, MemberId, CounterPartyName, TransactionValue, Description, DateOfRequest, Status,
         ApproverMemberId, EscalationReason, EscalatedAtUtc, CreatedAtUtc, ModifiedAtUtc, AmendmentCount,
         SubmittedByName, SubmittedOnBehalfOf, DocumentationConfirmed)
    VALUES
        (@CompanyId, @MemberId, @CounterPartyName, @TransactionValue, @Description, @DateOfRequest, @Status,
         @ApproverMemberId, @EscalationReason, @EscalatedAtUtc, SYSUTCDATETIME(), SYSUTCDATETIME(), 0,
         @SubmittedByName, @SubmittedOnBehalfOf, 0);

    SET @NewId = SCOPE_IDENTITY();
END
GO

-- User can amend counter-party/value/description any time before Approval/Rejection (FRD §3.3 Note 2).
CREATE OR ALTER PROCEDURE dbo.usp_RelatedPartyTransaction_Amend
    @Id int,
    @CounterPartyName nvarchar(200),
    @TransactionValue decimal(18,2),
    @Description nvarchar(4000)
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE dbo.RelatedPartyTransactions
    SET CounterPartyName = @CounterPartyName,
        TransactionValue = @TransactionValue,
        Description = @Description,
        AmendmentCount = AmendmentCount + 1,
        ModifiedAtUtc = SYSUTCDATETIME()
    WHERE Id = @Id;
END
GO

-- Approver's decision (Form 2: Approve/Reject/Escalate).
CREATE OR ALTER PROCEDURE dbo.usp_RelatedPartyTransaction_RecordApproverAction
    @Id int,
    @ApproverAction int,
    @ApproverRemarks nvarchar(2000),
    @ApproverActionAtUtc datetime2,
    @Status int,
    @EscalationReason int,
    @EscalatedAtUtc datetime2 = NULL
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE dbo.RelatedPartyTransactions
    SET ApproverAction = @ApproverAction,
        ApproverRemarks = @ApproverRemarks,
        ApproverActionAtUtc = @ApproverActionAtUtc,
        Status = @Status,
        EscalationReason = @EscalationReason,
        EscalatedAtUtc = COALESCE(@EscalatedAtUtc, EscalatedAtUtc),
        ModifiedAtUtc = SYSUTCDATETIME()
    WHERE Id = @Id;
END
GO

-- CCAO's Form 3 decision (Approve*/Reject, based on offline MD&CEO/Board/AC/GA feedback).
CREATE OR ALTER PROCEDURE dbo.usp_RelatedPartyTransaction_RecordCcaoAction
    @Id int,
    @CcaoAction int,
    @CcaoRemarks nvarchar(2000),
    @CcaoActionAtUtc datetime2,
    @Status int
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE dbo.RelatedPartyTransactions
    SET CcaoAction = @CcaoAction,
        CcaoRemarks = @CcaoRemarks,
        CcaoActionAtUtc = @CcaoActionAtUtc,
        Status = @Status,
        ModifiedAtUtc = SYSUTCDATETIME()
    WHERE Id = @Id;
END
GO

-- CCAO's Form 4 (Release to RP Register), gated by the documentation-confirmed checkbox.
CREATE OR ALTER PROCEDURE dbo.usp_RelatedPartyTransaction_Release
    @Id int,
    @ReleasedAtUtc datetime2
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE dbo.RelatedPartyTransactions
    SET DocumentationConfirmed = 1,
        ReleasedAtUtc = @ReleasedAtUtc,
        Status = 4, -- RpTransactionStatus.ReleasedToRegister
        ModifiedAtUtc = SYSUTCDATETIME()
    WHERE Id = @Id;
END
GO

-- Background job: 30-day timeout or conflict-of-interest auto-escalation (no manual Approver action).
CREATE OR ALTER PROCEDURE dbo.usp_RelatedPartyTransaction_AutoEscalate
    @Id int,
    @EscalationReason int,
    @EscalatedAtUtc datetime2
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE dbo.RelatedPartyTransactions
    SET Status = 3, -- RpTransactionStatus.Escalated
        EscalationReason = @EscalationReason,
        EscalatedAtUtc = @EscalatedAtUtc,
        ModifiedAtUtc = SYSUTCDATETIME()
    WHERE Id = @Id;
END
GO

-- Background job: records that a periodic reminder was just sent, so the hourly check doesn't resend
-- the same day.
CREATE OR ALTER PROCEDURE dbo.usp_RelatedPartyTransaction_SetLastReminderSent
    @Id int,
    @LastReminderSentUtc datetime2
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE dbo.RelatedPartyTransactions SET LastReminderSentUtc = @LastReminderSentUtc WHERE Id = @Id;
END
GO

CREATE OR ALTER PROCEDURE dbo.usp_RelatedPartyTransactionDocument_Insert
    @RelatedPartyTransactionId int,
    @FilePath nvarchar(260),
    @FileName nvarchar(160),
    @NewId int OUTPUT
AS
BEGIN
    SET NOCOUNT ON;

    INSERT INTO dbo.RelatedPartyTransactionDocuments (RelatedPartyTransactionId, FilePath, FileName, UploadedAtUtc)
    VALUES (@RelatedPartyTransactionId, @FilePath, @FileName, SYSUTCDATETIME());

    SET @NewId = SCOPE_IDENTITY();
END
GO

-- =========================== Dashboard manual reminders ===========================

CREATE OR ALTER PROCEDURE dbo.usp_DeclarationReminderLog_Insert
    @MemberId int,
    @MemberName nvarchar(120),
    @Email nvarchar(160),
    @CompanyName nvarchar(120) = NULL,
    @Category int,
    @Year int,
    @Quarter int,
    @SentByName nvarchar(160),
    @NewId int OUTPUT
AS
BEGIN
    SET NOCOUNT ON;

    INSERT INTO dbo.DeclarationReminderLogs
        (MemberId, MemberName, Email, CompanyName, Category, Year, Quarter, SentAtUtc, SentByName)
    VALUES
        (@MemberId, @MemberName, @Email, @CompanyName, @Category, @Year, @Quarter, SYSUTCDATETIME(), @SentByName);

    SET @NewId = SCOPE_IDENTITY();
END
GO

-- =========================== My Workspace (Family Members / Owned Companies) ===========================

CREATE OR ALTER PROCEDURE dbo.usp_FamilyMember_Insert
    @MemberId int,
    @Name nvarchar(120),
    @Relationship int,
    @NewId int OUTPUT
AS
BEGIN
    SET NOCOUNT ON;

    INSERT INTO dbo.FamilyMembers (MemberId, Name, Relationship)
    VALUES (@MemberId, @Name, @Relationship);

    SET @NewId = SCOPE_IDENTITY();
END
GO

CREATE OR ALTER PROCEDURE dbo.usp_FamilyMember_Update
    @Id int,
    @Name nvarchar(120),
    @Relationship int,
    @EmiratesIdPath nvarchar(260) = NULL,
    @PassportPath nvarchar(260) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE dbo.FamilyMembers
    SET Name = @Name, Relationship = @Relationship, EmiratesIdPath = @EmiratesIdPath, PassportPath = @PassportPath
    WHERE Id = @Id;
END
GO

CREATE OR ALTER PROCEDURE dbo.usp_FamilyMember_Delete
    @Id int
AS
BEGIN
    SET NOCOUNT ON;
    DELETE FROM dbo.FamilyMembers WHERE Id = @Id;
END
GO

CREATE OR ALTER PROCEDURE dbo.usp_OwnedCompany_Insert
    @MemberId int,
    @CompanyName nvarchar(160),
    @TradeLicenseDetails nvarchar(400) = NULL,
    @OwnershipPercentage decimal(5,2) = NULL,
    @NewId int OUTPUT
AS
BEGIN
    SET NOCOUNT ON;

    INSERT INTO dbo.OwnedCompanies (MemberId, CompanyName, TradeLicenseDetails, OwnershipPercentage)
    VALUES (@MemberId, @CompanyName, @TradeLicenseDetails, @OwnershipPercentage);

    SET @NewId = SCOPE_IDENTITY();
END
GO

CREATE OR ALTER PROCEDURE dbo.usp_OwnedCompany_Update
    @Id int,
    @CompanyName nvarchar(160),
    @TradeLicenseDetails nvarchar(400) = NULL,
    @OwnershipPercentage decimal(5,2) = NULL,
    @TradeLicensePath nvarchar(260) = NULL,
    @MoaPath nvarchar(260) = NULL,
    @PoaPath nvarchar(260) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE dbo.OwnedCompanies
    SET CompanyName = @CompanyName, TradeLicenseDetails = @TradeLicenseDetails, OwnershipPercentage = @OwnershipPercentage,
        TradeLicensePath = @TradeLicensePath, MoaPath = @MoaPath, PoaPath = @PoaPath
    WHERE Id = @Id;
END
GO

CREATE OR ALTER PROCEDURE dbo.usp_OwnedCompany_Delete
    @Id int
AS
BEGIN
    SET NOCOUNT ON;
    DELETE FROM dbo.OwnedCompanies WHERE Id = @Id;
END
GO
