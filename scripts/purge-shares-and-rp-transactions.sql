/*
    Deletes the shareholder register, the share trading data and the Related Party transactions.

    WHAT GOES
        dbo.ShareholderRecords          every row
        dbo.ShareholderRegisterUploads  every row
        dbo.ShareTradingRecords         every row
        dbo.ShareTradingUploads         every row
        dbo.RelatedPartyTransactionDocuments  every row
        dbo.RelatedPartyTransactions          every row

    WHAT STAYS
        dbo.AuditLogEntries. Not an oversight. The audit log is a hash chain with a gapless
        Sequence, verified by IAuditLogIntegrity.VerifyAsync(); deleting a row breaks it
        permanently and there is no way to repair it afterwards. The log keeps saying that these
        transactions existed and were deleted, which is the point of having one.

        Members, Companies, declarations and everything else. This script is about three things.

    DRY RUN BY DEFAULT. It reports what it would delete and rolls back. Set @DryRun = 0 to commit.

    IT DOES NOT TOUCH FILES ON DISK. The RP transaction attachments live in
    wwwroot\uploads\rp-transactions on the web server. Once the rows are gone nothing references
    them, so clear that folder by hand afterwards:
        Remove-Item 'C:\Websites\CGDIP\wwwroot\uploads\rp-transactions\*' -Recurse -Force

    IDENTITY VALUES ARE LEFT ALONE. Reseeding would make new rows take ids that audit entries
    already name, so an old entry about transaction 14 would appear to describe the new one.

    Re-runnable: deleting nothing is a successful run.
*/

SET NOCOUNT ON;
SET XACT_ABORT ON;

DECLARE @DryRun bit = 1;   -- <<< set to 0 to actually delete

-- Narrow the job if only part of it is wanted.
DECLARE @DeleteShareholderRegister bit = 1;
DECLARE @DeleteShareTrading        bit = 1;
DECLARE @DeleteRpTransactions      bit = 1;

/* ---------------------------------------------------------------------------
   Refuse rather than half-work: a missing table means this is the wrong
   database, or one whose schema is behind the application.
   --------------------------------------------------------------------------- */
IF @DeleteShareholderRegister = 1
   AND (OBJECT_ID('dbo.ShareholderRecords', 'U') IS NULL
     OR OBJECT_ID('dbo.ShareholderRegisterUploads', 'U') IS NULL)
BEGIN
    THROW 52000, 'Shareholder register tables not found. Wrong database, or its schema is behind the application.', 1;
END

IF @DeleteShareTrading = 1
   AND (OBJECT_ID('dbo.ShareTradingRecords', 'U') IS NULL
     OR OBJECT_ID('dbo.ShareTradingUploads', 'U') IS NULL)
BEGIN
    THROW 52001, 'Share trading tables not found. Wrong database, or its schema is behind the application.', 1;
END

IF @DeleteRpTransactions = 1
   AND (OBJECT_ID('dbo.RelatedPartyTransactionDocuments', 'U') IS NULL
     OR OBJECT_ID('dbo.RelatedPartyTransactions', 'U') IS NULL)
BEGIN
    THROW 52002, 'Related Party transaction tables not found. Wrong database, or its schema is behind the application.', 1;
END

DECLARE @BeforeShareholderRecords int = 0, @BeforeShareholderUploads int = 0;
DECLARE @BeforeTradingRecords     int = 0, @BeforeTradingUploads     int = 0;
DECLARE @BeforeRpDocuments        int = 0, @BeforeRpTransactions     int = 0;

IF @DeleteShareholderRegister = 1
BEGIN
    SELECT @BeforeShareholderRecords = COUNT(*) FROM dbo.ShareholderRecords;
    SELECT @BeforeShareholderUploads = COUNT(*) FROM dbo.ShareholderRegisterUploads;
END

IF @DeleteShareTrading = 1
BEGIN
    SELECT @BeforeTradingRecords = COUNT(*) FROM dbo.ShareTradingRecords;
    SELECT @BeforeTradingUploads = COUNT(*) FROM dbo.ShareTradingUploads;
END

IF @DeleteRpTransactions = 1
BEGIN
    SELECT @BeforeRpDocuments    = COUNT(*) FROM dbo.RelatedPartyTransactionDocuments;
    SELECT @BeforeRpTransactions = COUNT(*) FROM dbo.RelatedPartyTransactions;
END

PRINT '--- before ---';
PRINT 'ShareholderRecords                 : ' + CAST(@BeforeShareholderRecords AS varchar(12));
PRINT 'ShareholderRegisterUploads         : ' + CAST(@BeforeShareholderUploads AS varchar(12));
PRINT 'ShareTradingRecords                : ' + CAST(@BeforeTradingRecords     AS varchar(12));
PRINT 'ShareTradingUploads                : ' + CAST(@BeforeTradingUploads     AS varchar(12));
PRINT 'RelatedPartyTransactionDocuments   : ' + CAST(@BeforeRpDocuments        AS varchar(12));
PRINT 'RelatedPartyTransactions           : ' + CAST(@BeforeRpTransactions     AS varchar(12));

BEGIN TRANSACTION;

    /* Children first. Every one of these is a plain parent/child pair -- records belong to the
       upload that brought them in, documents to the transaction they were attached to -- so there
       is no cycle to work around and no third table pointing at either. */

    IF @DeleteShareholderRegister = 1
    BEGIN
        DELETE FROM dbo.ShareholderRecords;
        DELETE FROM dbo.ShareholderRegisterUploads;
    END

    IF @DeleteShareTrading = 1
    BEGIN
        DELETE FROM dbo.ShareTradingRecords;
        DELETE FROM dbo.ShareTradingUploads;
    END

    IF @DeleteRpTransactions = 1
    BEGIN
        DELETE FROM dbo.RelatedPartyTransactionDocuments;
        DELETE FROM dbo.RelatedPartyTransactions;
    END

    /* Reported the way the before-counts were gathered, and for the same reason: a section that
       was switched off may be switched off because its tables are not in this database, and a
       PRINT that reads them anyway would fail on a run that was deliberately not touching them. */
    PRINT '';
    PRINT '--- after ---';

    IF @DeleteShareholderRegister = 1
    BEGIN
        PRINT 'ShareholderRecords                 : ' + CAST((SELECT COUNT(*) FROM dbo.ShareholderRecords) AS varchar(12));
        PRINT 'ShareholderRegisterUploads         : ' + CAST((SELECT COUNT(*) FROM dbo.ShareholderRegisterUploads) AS varchar(12));
    END
    ELSE PRINT 'Shareholder register               : skipped';

    IF @DeleteShareTrading = 1
    BEGIN
        PRINT 'ShareTradingRecords                : ' + CAST((SELECT COUNT(*) FROM dbo.ShareTradingRecords) AS varchar(12));
        PRINT 'ShareTradingUploads                : ' + CAST((SELECT COUNT(*) FROM dbo.ShareTradingUploads) AS varchar(12));
    END
    ELSE PRINT 'Share trading                      : skipped';

    IF @DeleteRpTransactions = 1
    BEGIN
        PRINT 'RelatedPartyTransactionDocuments   : ' + CAST((SELECT COUNT(*) FROM dbo.RelatedPartyTransactionDocuments) AS varchar(12));
        PRINT 'RelatedPartyTransactions           : ' + CAST((SELECT COUNT(*) FROM dbo.RelatedPartyTransactions) AS varchar(12));
    END
    ELSE PRINT 'Related Party transactions         : skipped';

IF @DryRun = 1
BEGIN
    ROLLBACK TRANSACTION;
    PRINT '';
    PRINT 'DRY RUN -- rolled back. Nothing was deleted. Set @DryRun = 0 to commit.';
END
ELSE
BEGIN
    COMMIT TRANSACTION;
    PRINT '';
    PRINT 'COMMITTED. Now clear wwwroot\uploads\rp-transactions on the web server -- the rows that';
    PRINT 'referenced those files are gone, so nothing points at them any more.';
END
GO
