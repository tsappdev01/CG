-- Why is a member still being asked to upload their Emirates ID?
--
-- Two different things have to be true for onboarding to show somebody their details, and this
-- tells you which one is missing.
--
--   1. A row in dbo.MemberIdentityReferences, matched on their email address. Written by the
--      user-list upload in User Management.
--   2. Their dbo.MemberOnboardings row having picked it up. That read happens when the row is
--      created, and (from the build carrying this script) also whenever a step is still both
--      blank and unanswered.
--
-- An older build only read it at creation, so anyone who opened onboarding before their
-- spreadsheet was loaded stays on the "supply your own" path until that build is deployed.

DECLARE @Email nvarchar(320) = N'';   -- <-- the person you are checking

SELECT
    m.Id                        AS MemberId,
    m.FullName,
    m.Email,
    m.OnboardingRequired,
    CASE WHEN r.Id IS NULL THEN 'NO - the upload has not loaded them'
         ELSE 'yes' END         AS HasReferenceRow,
    r.EmiratesIdNumber,
    r.Nin,
    r.Nationality,
    CASE WHEN r.EmiratesIdBlobId IS NULL THEN 'no scan attached' ELSE 'scan attached' END AS Scan,
    r.ImportedAtUtc,
    o.Status                    AS OnboardingStatus,
    o.EmiratesIdStatus,
    o.EmiratesIdSource,         -- 0 None, 1 Reference, 2 EmployeeSubmitted
    o.DfmStatus,
    o.DfmSource,
    o.StartedAtUtc,
    o.CompletedAtUtc
FROM dbo.Members m
LEFT JOIN dbo.MemberIdentityReferences r ON r.Email = m.Email
LEFT JOIN dbo.MemberOnboardings o        ON o.MemberId = m.Id
WHERE @Email = N'' OR m.Email = @Email
ORDER BY m.FullName;

-- How to read it:
--
--   HasReferenceRow = NO           the upload did not load them. Check the spreadsheet has their
--                                  email, and that their domain is not in Onboarding:ExcludedEmailDomains.
--
--   HasReferenceRow = yes          the data is there. If onboarding still asks them to upload,
--   and EmiratesIdSource = 0       their onboarding row was created before the data arrived and
--                                  this deployment is too old to re-read it. Deploy, then reopen
--                                  /onboarding -- nothing needs deleting.
--
--   EmiratesIdSource = 1           they are being shown the reference data, as intended.
--   EmiratesIdSource = 2           they supplied their own; that is kept and not overwritten.
