/*
    What did the last Insider Trading save actually write?

    "I saved as draft and it was all gone when I came back" has two very different causes, and
    this tells them apart in one run:

      * the header row is there and the child counts are 0  -> the save wrote a header and lost
        the grids, which is a bug in the save
      * the child counts are right                          -> the save worked and the screen is
        not reading it back, which is a bug in the load
      * no row at all                                       -> the save never happened

    Run against CGS. Put the member whose draft went missing in @Email first -- this repository is
    public, so the address goes in when the query is run and not before.
*/
DECLARE @Email nvarchar(256) = N'';

DECLARE @MemberId int = (SELECT TOP (1) Id FROM dbo.Members WHERE Email = @Email);

IF @MemberId IS NULL
BEGIN
    PRINT 'Set @Email at the top of this script to the member''s address, as it appears in dbo.Members.';
    RETURN;
END

SELECT  d.Id,
        d.DeclarationCycleRunId     AS RunId,
        r.PeriodQuarter,
        r.PeriodYear,
        d.IsDraft,
        d.HasNin,
        d.NinNumber,
        d.HoldsShares,
        d.RelativesHoldShares,
        Relatives   = (SELECT COUNT(*) FROM dbo.InsiderDeclarationRelatives   x WHERE x.InsiderDeclarationId = d.Id),
        NinHolders  = (SELECT COUNT(*) FROM dbo.InsiderDeclarationNinHolders  x WHERE x.InsiderDeclarationId = d.Id),
        ShareEntries= (SELECT COUNT(*) FROM dbo.InsiderDeclarationShareEntries x WHERE x.InsiderDeclarationId = d.Id),
        Documents   = (SELECT COUNT(*) FROM dbo.InsiderDeclarationDocuments   x WHERE x.InsiderDeclarationId = d.Id)
FROM    dbo.InsiderDeclarations AS d
        LEFT JOIN dbo.DeclarationCycleRuns AS r ON r.Id = d.DeclarationCycleRunId
WHERE   d.MemberId = @MemberId
ORDER BY d.Id DESC;

-- The share lines themselves, newest declaration first.
SELECT  e.InsiderDeclarationId AS DeclarationId,
        e.IsSelf,
        e.Relationship,
        e.HolderName,
        e.HolderNin,
        e.EntryKind,
        e.TransactionDate,
        e.NumberOfShares,
        e.Rate
FROM    dbo.InsiderDeclarationShareEntries AS e
        INNER JOIN dbo.InsiderDeclarations AS d ON d.Id = e.InsiderDeclarationId
WHERE   d.MemberId = @MemberId
ORDER BY e.InsiderDeclarationId DESC, e.Id;

-- Which run the plain URL will open, and whether a draft is attached to it. A draft on a run that
-- is NOT this one cannot be reached from the menu: the form looks at the newest run with nothing
-- submitted against it, and opens the draft belonging to that run only.
SELECT TOP (5)
        r.Id            AS RunId,
        r.PeriodQuarter,
        r.PeriodYear,
        r.SentAtUtc,
        r.DueDateUtc,
        r.Recalled,
        DraftHere   = (SELECT COUNT(*) FROM dbo.InsiderDeclarations d
                       WHERE d.MemberId = @MemberId AND d.DeclarationCycleRunId = r.Id AND d.IsDraft = 1),
        SubmittedHere=(SELECT COUNT(*) FROM dbo.InsiderDeclarations d
                       WHERE d.MemberId = @MemberId AND d.DeclarationCycleRunId = r.Id AND d.IsDraft = 0)
FROM    dbo.DeclarationCycleRuns AS r
WHERE   r.Type = 0
ORDER BY r.SentAtUtc DESC, r.Id DESC;
