USE [CGS]
GO

SET NOCOUNT ON;
SET XACT_ABORT ON;

BEGIN TRANSACTION;

DECLARE @Companies TABLE (
    [Name]      NVARCHAR(160) NOT NULL,
    [ShortCode] NVARCHAR(20)  NOT NULL
);

INSERT INTO @Companies ([Name], [ShortCode])
VALUES
    (N'TechSource',                        N'TS'),
    (N'Dubai Investments PJSC',            N'DI'),
    (N'Dubai Investments Park',            N'DIP'),
    (N'Dubai Investments Industries',      N'DII'),
    (N'Emirates Building System',          N'EBS'),
    (N'Dubai Investment Realestate',       N'DIRC'),
    (N'Emirates Extruded Polystyrene',     N'EEP'),
    (N'Masharie',                          N'MASHARIE'),
    (N'Al Mujama Real Estate',             N'AMRE'),
    (N'Emirates Extrusion Factory',        N'EEF'),
    (N'White Aluminium Extrusion',         N'WAE'),
    (N'Gulf Metal Craft',                  N'GMC'),
    (N'Properties Investment',             N'PI'),
    (N'Emirates Glass',                    N'EG'),
    (N'Palisades',                         N'PALISADES'),
    (N'Lite-Tech Industries',              N'LTI'),
    (N'Al Taif Investment',                N'ATI'),
    (N'Mujama',                            N'MUJAMA'),
    (N'Al Mal Capital',                    N'AMC'),
    (N'Mirdif Hills',                      N'MH'),
    (N'PID Owners Association Management', N'PIDOAM'),
    (N'Glass LLC',                         N'GLASS'),
    (N'Danah Bay',                         N'DB'),
    (N'DIP Angola',                        N'DIPA'),
    (N'DI Investment Holding Limited',     N'DIIHL');

-- Insert only companies whose name or short code is not already present.
-- Address, City, Country, LogoPath, authority members, GroupName and Sector are left NULL.
INSERT INTO [dbo].[Companies] ([Name], [ShortCode], [Active])
SELECT c.[Name], c.[ShortCode], 1
FROM @Companies c
WHERE NOT EXISTS (
    SELECT 1
    FROM [dbo].[Companies] x
    WHERE x.[Name] = c.[Name]
       OR x.[ShortCode] = c.[ShortCode]
);

SELECT @@ROWCOUNT AS RowsInserted;

COMMIT TRANSACTION;
GO