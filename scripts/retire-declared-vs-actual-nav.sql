-- Retires the "Declared vs Actual" report's nav menu settings.
--
-- The report compared what insiders declared buying and selling against what the DFM recorded.
-- Its only input was the share transactions section of the Insider declaration, which the business
-- asked to have removed, so the report and its page went with it.
--
-- The catalogue entry is gone from the code, so nothing reads these rows any more; they are
-- deleted so an administrator's saved order, label and visibility for a menu item that no longer
-- exists does not sit in the tables for good. Deleting them loses nothing: the item cannot come
-- back without a code change, and a code change would set them afresh.
--
-- Re-runnable: deleting nothing is the expected result on the second run.

SET NOCOUNT ON;
GO

DECLARE @ItemKey nvarchar(100) = N'Reports.InsiderReconciliation';

IF OBJECT_ID('dbo.NavMenuItemOrders', 'U') IS NOT NULL
    DELETE FROM dbo.NavMenuItemOrders WHERE ItemKey = @ItemKey;

IF OBJECT_ID('dbo.NavMenuItemLabels', 'U') IS NOT NULL
    DELETE FROM dbo.NavMenuItemLabels WHERE ItemKey = @ItemKey;

IF OBJECT_ID('dbo.NavMenuItemVisibilities', 'U') IS NOT NULL
    DELETE FROM dbo.NavMenuItemVisibilities WHERE ItemKey = @ItemKey;

PRINT 'Declared vs Actual nav settings retired.';
GO
