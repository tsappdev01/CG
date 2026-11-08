/*  What onboarding will show each member, and where it comes from.

    Onboarding pre-fills from two places, in this order:
      1. dbo.MemberIdentityReferences  -- the imported spreadsheet, matched on email
      2. the member's own My Workspace -- dbo.MemberDocuments and dbo.MemberDfmProfiles

    Read-only. Nothing here changes anything.

    Section 3 is the one to look at first: it lists members whose email is shared
    with somebody else. The reference import keeps one row per address, so every
    member on a shared address is matched to the SAME imported row -- they will all
    be shown the same Emirates ID and the same NIN, which for all but one of them
    is somebody else's.
*/

SET NOCOUNT ON;

PRINT '=== 1. Emirates ID: what each member would be shown ===';

SELECT
    m.Id                AS MemberId,
    m.FullName,
    m.Email,
    m.OnboardingRequired,
    CASE
        WHEN r.EmiratesIdNumber IS NOT NULL THEN 'imported spreadsheet'
        WHEN d.Id               IS NOT NULL THEN 'their My Workspace'
        ELSE                                     'nothing -- will be asked to upload'
    END                 AS SourceOnboardingUses,
    COALESCE(r.EmiratesIdNumber, d.DocumentNumber)        AS NumberShown,
    COALESCE(r.NameOnEmiratesId, r.FullName, d.HolderName) AS NameShown,
    COALESCE(r.EmiratesIdBlobId, d.BlobId)                AS BlobId,
    -- The name on the card against the name on the member record. A mismatch is not
    -- proof of anything (people are registered under fuller names than they use),
    -- but it is where a card attached to the wrong person shows up.
    CASE WHEN COALESCE(r.NameOnEmiratesId, r.FullName, d.HolderName) IS NOT NULL
          AND COALESCE(r.NameOnEmiratesId, r.FullName, d.HolderName) <> m.FullName
         THEN 'CHECK -- name differs from the member record' END AS Note
FROM dbo.Members AS m
LEFT JOIN dbo.MemberIdentityReferences AS r
       ON r.Email = m.Email
LEFT JOIN dbo.MemberDocuments AS d
       ON d.MemberId = m.Id
      AND d.Kind = 0                      -- MemberDocumentKind.EmiratesId
      AND (d.BlobId IS NOT NULL OR d.DocumentNumber IS NOT NULL)
WHERE m.Active = 1
ORDER BY SourceOnboardingUses, m.FullName;

PRINT '';
PRINT '=== 2. DFM / NIN: what each member would be shown ===';

SELECT
    m.Id                AS MemberId,
    m.FullName,
    m.Email,
    CASE
        WHEN r.Nin              IS NOT NULL THEN 'imported spreadsheet'
        WHEN p.InvestorNumber   IS NOT NULL THEN 'their My Workspace'
        ELSE                                     'nothing -- will be asked'
    END                 AS SourceOnboardingUses,
    COALESCE(r.Nin, p.InvestorNumber)                AS NinShown,
    COALESCE(r.NameOnEmiratesId, r.FullName, p.FullName) AS RegisteredNameShown,
    CASE WHEN LEN(LTRIM(RTRIM(COALESCE(r.Nin, p.InvestorNumber)))) <> 10
         THEN 'CHECK -- a NIN is exactly 10 characters' END AS Note
FROM dbo.Members AS m
LEFT JOIN dbo.MemberIdentityReferences AS r
       ON r.Email = m.Email
LEFT JOIN dbo.MemberDfmProfiles AS p
       ON p.MemberId = m.Id
WHERE m.Active = 1
ORDER BY SourceOnboardingUses, m.FullName;

PRINT '';
PRINT '=== 3. Shared email addresses -- these members are matched to the SAME imported row ===';

SELECT
    m.Email,
    COUNT(*)                                   AS MembersOnThisAddress,
    STRING_AGG(CONVERT(nvarchar(max), m.FullName), ' | ') AS Members,
    MAX(r.EmiratesIdNumber)                    AS EmiratesIdTheyAllGet,
    MAX(r.Nin)                                 AS NinTheyAllGet
FROM dbo.Members AS m
LEFT JOIN dbo.MemberIdentityReferences AS r ON r.Email = m.Email
WHERE m.Active = 1 AND m.Email IS NOT NULL AND m.Email <> ''
GROUP BY m.Email
HAVING COUNT(*) > 1
ORDER BY COUNT(*) DESC;

PRINT '';
PRINT '=== 4. The same Emirates ID number against more than one member ===';

WITH Shown AS (
    SELECT m.Id, m.FullName,
           COALESCE(r.EmiratesIdNumber, d.DocumentNumber) AS Number
    FROM dbo.Members AS m
    LEFT JOIN dbo.MemberIdentityReferences AS r ON r.Email = m.Email
    LEFT JOIN dbo.MemberDocuments AS d
           ON d.MemberId = m.Id AND d.Kind = 0
    WHERE m.Active = 1
)
SELECT Number AS EmiratesIdNumber,
       COUNT(*) AS MemberCount,
       STRING_AGG(CONVERT(nvarchar(max), FullName), ' | ') AS Members
FROM Shown
WHERE Number IS NOT NULL
GROUP BY Number
HAVING COUNT(*) > 1
ORDER BY COUNT(*) DESC;

PRINT '';
PRINT '=== 5. The same NIN against more than one member ===';

WITH Shown AS (
    SELECT m.Id, m.FullName,
           COALESCE(r.Nin, p.InvestorNumber) AS Nin
    FROM dbo.Members AS m
    LEFT JOIN dbo.MemberIdentityReferences AS r ON r.Email = m.Email
    LEFT JOIN dbo.MemberDfmProfiles AS p ON p.MemberId = m.Id
    WHERE m.Active = 1
)
SELECT Nin,
       COUNT(*) AS MemberCount,
       STRING_AGG(CONVERT(nvarchar(max), FullName), ' | ') AS Members
FROM Shown
WHERE Nin IS NOT NULL
GROUP BY Nin
HAVING COUNT(*) > 1
ORDER BY COUNT(*) DESC;
