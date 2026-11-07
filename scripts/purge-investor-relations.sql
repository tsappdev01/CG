/*
    Clears the Investor Relations uploads -- the Share Register snapshots and the Shares Trading
    extracts -- so they can be loaded again from scratch.

    Reads as a dry run. It prints what it would delete and changes nothing until @DryRun is set to
    0. Children are deleted before their parents, so the foreign keys are satisfied at every step.

    What it does NOT touch:
      * dbo.AuditLogEntries -- the record that an upload happened, and by whom, outlives the data
        it loaded. Deleting the trail to tidy up the thing it describes is how an audit finding is
        written.
      * The uploaded workbooks kept under wwwroot\uploads\investor-relations. The rows go; the
        files they came from stay, and a row deleted here leaves its file orphaned on disk. Delete
        those from the file system if they are meant to go too.

    Pick what to clear with the two switches. Both default to on.
*/
SET NOCOUNT ON;

DECLARE @DryRun bit = 1;            -- 0 actually deletes
DECLARE @ShareRegister bit = 1;     -- the Share Register snapshots
DECLARE @ShareTrading bit = 1;      -- the Shares Trading extracts

DECLARE @RegisterUploads int = 0, @RegisterRows int = 0;
DECLARE @TradingUploads int = 0, @TradingRows int = 0;

IF @ShareRegister = 1
BEGIN
    SELECT @RegisterUploads = COUNT(*) FROM dbo.ShareholderRegisterUploads;
    SELECT @RegisterRows    = COUNT(*) FROM dbo.ShareholderRecords;
END

IF @ShareTrading = 1
BEGIN
    SELECT @TradingUploads = COUNT(*) FROM dbo.ShareTradingUploads;
    SELECT @TradingRows    = COUNT(*) FROM dbo.ShareTradingRecords;
END

PRINT 'Share Register : ' + CAST(@RegisterUploads AS varchar(12)) + ' upload(s), '
      + CAST(@RegisterRows AS varchar(12)) + ' shareholder row(s)';
PRINT 'Shares Trading : ' + CAST(@TradingUploads AS varchar(12)) + ' upload(s), '
      + CAST(@TradingRows AS varchar(12)) + ' movement row(s)';

-- The files on disk, so whoever runs this knows what is left behind.
IF @ShareRegister = 1 AND EXISTS (SELECT 1 FROM dbo.ShareholderRegisterUploads WHERE FilePath IS NOT NULL)
BEGIN
    PRINT '';
    PRINT 'Kept workbooks that will be orphaned (delete from the file system separately):';
    SELECT FilePath, FileName, AsOnDate, UploadedAtUtc
    FROM   dbo.ShareholderRegisterUploads
    WHERE  FilePath IS NOT NULL
    ORDER BY AsOnDate DESC;
END

IF @DryRun = 1
BEGIN
    PRINT '';
    PRINT 'DRY RUN -- nothing was deleted. Set @DryRun = 0 at the top and run again.';
    RETURN;
END

BEGIN TRY
    BEGIN TRANSACTION;

    IF @ShareTrading = 1
    BEGIN
        DELETE FROM dbo.ShareTradingRecords;
        DELETE FROM dbo.ShareTradingUploads;
    END

    IF @ShareRegister = 1
    BEGIN
        DELETE FROM dbo.ShareholderRecords;
        DELETE FROM dbo.ShareholderRegisterUploads;
    END

    COMMIT TRANSACTION;

    PRINT '';
    PRINT 'Deleted. Upload again from the Investor Relations screens.';
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    PRINT 'Rolled back -- nothing was deleted.';
    THROW;
END CATCH
