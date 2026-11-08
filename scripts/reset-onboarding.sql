-- Reset a member's onboarding so they go through it again.
--
-- Set @Email to one person, or leave it blank to reset EVERYONE. Nothing runs until you set
-- @Confirm = 1, because a blank @Email plus a careless Execute is the whole population.
--
-- What this does NOT touch, on purpose:
--
--   dbo.MemberIdentityReferences  what the spreadsheet loaded about them. Resetting onboarding is
--                                 re-asking the questions, not throwing away the answers the
--                                 organisation already held. Leave it, and the member is shown
--                                 their details to confirm, which is the path worth testing.
--
--   dbo.MemberDocuments           anything already copied into My Workspace. A completed
--                                 onboarding seeds the member's Emirates ID there, and deleting
--                                 it would take away a document they can see and rely on.
--                                 Onboarding fills blanks only, so leaving it is also what makes
--                                 a second run behave like a real second run.
--
--   dbo.DocumentBlobs             the bytes of anything they uploaded. The onboarding row that
--                                 pointed at them goes, so they are left unreferenced -- a few
--                                 rows, not a correctness problem. The query at the bottom lists
--                                 them if you want them gone.
--
--   dbo.AuditLogs                 what they did and when. That is the record of the first run and
--                                 is not rewritten by a second.

SET NOCOUNT ON;

DECLARE @Email   nvarchar(320) = N'';   -- the person to reset; blank = every member
DECLARE @Confirm bit           = 0;                          -- set to 1 to actually run it

-- ---------------------------------------------------------------- what would change

SELECT
    m.Id              AS MemberId,
    m.FullName,
    m.Email,
    o.Status          AS OnboardingStatus,    -- 0 Pending, 1 In progress, 2 Completed
    o.EmiratesIdStatus,
    o.EmiratesIdSource,                       -- 0 None, 1 Reference, 2 EmployeeSubmitted
    o.DfmStatus,
    o.NinResponse,
    o.StartedAtUtc,
    o.CompletedAtUtc,
    CASE WHEN o.Id IS NULL THEN 'nothing to reset' ELSE 'will be reset' END AS Effect
FROM dbo.Members m
LEFT JOIN dbo.MemberOnboardings o ON o.MemberId = m.Id
WHERE (@Email = N'' OR m.Email = @Email)
ORDER BY m.FullName;

IF @Confirm <> 1
BEGIN
    PRINT 'Nothing was changed. Set @Confirm = 1 to reset the rows listed above.';
    RETURN;
END

-- ---------------------------------------------------------------- reset

BEGIN TRANSACTION;

    -- Deleted rather than blanked. The next time the member opens onboarding a fresh row is
    -- created, and creating one is what reads the identity reference data -- so they get the
    -- "here is what we hold, is it right?" path if their details are on file, exactly as a
    -- first-time member would.
    DELETE o
    FROM dbo.MemberOnboardings o
    JOIN dbo.Members m ON m.Id = o.MemberId
    WHERE @Email = N'' OR m.Email = @Email;

    PRINT CONCAT(@@ROWCOUNT, ' onboarding record(s) reset.');

    -- Onboarding only runs for members flagged for it, so a reset is pointless without this.
    UPDATE m
    SET OnboardingRequired = 1
    FROM dbo.Members m
    WHERE (@Email = N'' OR m.Email = @Email)
      AND m.OnboardingRequired = 0;

    PRINT CONCAT(@@ROWCOUNT, ' member(s) had OnboardingRequired switched back on.');

COMMIT TRANSACTION;

-- ---------------------------------------------------------------- afterwards

-- Documents nothing points at any more, left behind by the records just deleted.
-- Review before deleting: a blob can be referenced from more than one place.
SELECT b.Id, b.FileName, b.ContentType, b.SizeBytes, b.UploadedAtUtc
FROM dbo.DocumentBlobs b
WHERE NOT EXISTS (SELECT 1 FROM dbo.MemberIdentityReferences r WHERE r.EmiratesIdBlobId    = b.Id)
  AND NOT EXISTS (SELECT 1 FROM dbo.MemberOnboardings        o WHERE o.EmiratesIdBlobId    = b.Id
                                                                  OR o.NinDocumentBlobId   = b.Id)
  AND NOT EXISTS (SELECT 1 FROM dbo.MemberDocuments          d WHERE d.BlobId              = b.Id)
ORDER BY b.UploadedAtUtc DESC;
