/*
    Enable SQL Server Change Data Capture (CDC) on every master-data and
    transactional table in CGTOOL, and configure the cleanup job so
    captured change history is never purged.

    NOT part of the EF Core migration pipeline (deliberately): CDC requires
    sysadmin rights and a running SQL Server Agent, which may not be present
    in every environment the app is deployed to. Running this script is a
    one-time, DBA-run setup step against the target database (CGS on
    UATWEB01), separate from `dotnet ef database update`.

    Safe to re-run: every step first checks whether CDC is already enabled
    before enabling it again.
*/

USE [CGS];
GO

-- 1. Enable CDC at the database level.
IF NOT EXISTS (SELECT 1 FROM sys.databases WHERE name = DB_NAME() AND is_cdc_enabled = 1)
BEGIN
    EXEC sys.sp_cdc_enable_db;
END
GO

-- 2. Enable CDC on every master-data and transactional table.
--    @role_name = NULL means no additional gating role is required to read
--    the change tables beyond normal db_owner/sysadmin access.
DECLARE @tables TABLE (schema_name sysname, table_name sysname);
INSERT INTO @tables (schema_name, table_name) VALUES
    -- Governance master data
    ('dbo', 'Companies'),
    ('dbo', 'Departments'),
    ('dbo', 'Members'),
    ('dbo', 'MemberImpersonationApprovals'),
    ('dbo', 'DeclarationSetups'),
    ('dbo', 'ScheduledActivities'),
    ('dbo', 'ScheduledActivityDates'),
    -- Transactional data
    ('dbo', 'Transactions'),
    -- Audit trail (must itself be captured — it is the system of record for WHO/WHY/HOW/WHEN)
    ('dbo', 'AuditLogEntries'),
    -- ASP.NET Core Identity tables (login accounts, roles, role assignments)
    ('dbo', 'AspNetUsers'),
    ('dbo', 'AspNetRoles'),
    ('dbo', 'AspNetUserRoles'),
    ('dbo', 'AspNetUserClaims'),
    ('dbo', 'AspNetUserLogins'),
    ('dbo', 'AspNetUserTokens'),
    ('dbo', 'AspNetRoleClaims');

DECLARE @schema sysname, @table sysname, @captureInstance sysname;
DECLARE cdc_cursor CURSOR LOCAL FAST_FORWARD FOR SELECT schema_name, table_name FROM @tables;
OPEN cdc_cursor;
FETCH NEXT FROM cdc_cursor INTO @schema, @table;

WHILE @@FETCH_STATUS = 0
BEGIN
    SET @captureInstance = @schema + '_' + @table;

    IF NOT EXISTS (
        SELECT 1
        FROM cdc.change_tables ct
        JOIN sys.tables t ON t.object_id = ct.source_object_id
        JOIN sys.schemas s ON s.schema_id = t.schema_id
        WHERE s.name = @schema AND t.name = @table
    )
    BEGIN
        EXEC sys.sp_cdc_enable_table
            @source_schema = @schema,
            @source_name = @table,
            @role_name = NULL,
            @supports_net_changes = 1,
            @capture_instance = @captureInstance;
    END

    FETCH NEXT FROM cdc_cursor INTO @schema, @table;
END

CLOSE cdc_cursor;
DEALLOCATE cdc_cursor;
GO

-- 3. Ensure captured change history is never purged.
--    CDC's default cleanup job deletes change-table rows older than a
--    retention window (3 days by default). Per policy, CDC history must be
--    retained indefinitely, so:
--      a) push retention out to an effectively unbounded window, and
--      b) disable the cleanup job outright, so no automatic deletion runs
--         even if retention is ever misconfigured later.
IF EXISTS (SELECT 1 FROM msdb.dbo.cdc_jobs WHERE job_type = 'cleanup' AND database_id = DB_ID())
BEGIN
    -- 5,256,000 minutes = 10 years; belt-and-braces alongside disabling the job below.
    EXEC sys.sp_cdc_change_job @job_type = 'cleanup', @retention = 5256000;
    EXEC sys.sp_cdc_stop_job @job_type = 'cleanup';
END
GO

-- 4. Sanity check: list every table now under CDC capture.
SELECT
    s.name AS schema_name,
    t.name AS table_name,
    ct.capture_instance,
    ct.start_lsn,
    ct.supports_net_changes
FROM cdc.change_tables ct
JOIN sys.tables t ON t.object_id = ct.source_object_id
JOIN sys.schemas s ON s.schema_id = t.schema_id
ORDER BY s.name, t.name;
GO
